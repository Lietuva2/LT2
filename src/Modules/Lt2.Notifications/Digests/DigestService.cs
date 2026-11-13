using System.Globalization;
using System.Text;
using System.Text.Json;
using Lt2.Notifications.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Notifications.Digests;

/// <summary>
/// Summary generation in dry-run (design 8.4, 8.7, 8.9): the weekly and before-vote rules, the per-MP ceiling, eligibility
/// by threshold, held sending, the frozen snapshot with only arguments a moderator has seen, both sides with the 30-day
/// counts and the surge note. Nothing is sent: no outbox row is written, and no MP address ever enters this module.
/// </summary>
internal sealed class DigestService(
    IDbContextFactory<NotificationsDb> contexts,
    IDigestSource source,
    IMpDirectory mps,
    ISettings settings,
    IAuditLog audit,
    DigestText texts,
    TimeProvider clock) : IDigests
{
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    public async Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var local = TimeZoneInfo.ConvertTime(now, Vilnius);
        var today = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var weekStart = DigestRules.WeekStart(today);
        var weeklyAt = DigestRules.TimeOf(await settings.GetAsync(SettingKeys.DigestWeekly, cancellationToken));
        var beforeVoteAt = DigestRules.TimeOf(await settings.GetAsync(SettingKeys.DigestBeforeVote, cancellationToken));
        var sittings = await mps.SittingDaysAsync(weekStart, today.AddDays(10), cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var made = new List<DigestRunInfo>();
        IReadOnlyList<DigestCandidate>? candidates = null;

        var weeklyMade = await db.DigestRuns.AnyAsync(r => r.Kind == DigestRules.Weekly && r.RunDate >= weekStart && r.RunDate < weekStart.AddDays(7), cancellationToken);
        if (DigestRules.WeeklyDue(today, time, weeklyAt, sittings.Any(d => d >= weekStart && d < weekStart.AddDays(7)), weeklyMade))
        {
            candidates ??= await source.CandidatesAsync(cancellationToken);
            var last = await LastParticipantsAsync(db, cancellationToken);
            var notable = await settings.GetAsync(SettingKeys.DigestNotableChange, cancellationToken);
            var items = DigestRules.WeeklyItems(candidates, weekStart, last, notable);
            if (items.Count > 0)
            {
                made.Add(await MakeAsync(db, DigestRules.Weekly, today, null, items, null, now, cancellationToken));
            }
        }

        foreach (var sitting in sittings.Where(d => d > today))
        {
            var exists = await db.DigestRuns.AnyAsync(r => r.Kind == DigestRules.BeforeVote && r.SittingDate == sitting, cancellationToken);
            if (!DigestRules.BeforeVoteDue(today, time, beforeVoteAt, sitting, exists))
            {
                continue;
            }

            candidates ??= await source.CandidatesAsync(cancellationToken);
            var covered = await CoveredByMondayAsync(db, DigestRules.WeekStart(sitting), sitting, cancellationToken);
            var items = DigestRules.BeforeVoteItems(candidates, sitting, covered);
            if (items.Count > 0)
            {
                made.Add(await MakeAsync(db, DigestRules.BeforeVote, today, sitting, items, null, now, cancellationToken));
            }
        }

        return made;
    }

    public async Task<Result<DigestRunInfo>> UrgentAsync(Guid appealId, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Vilnius).DateTime);
        var candidate = (await source.CandidatesAsync(cancellationToken)).FirstOrDefault(c => c.AppealId == appealId);
        if (candidate is null)
        {
            return Error.NotFound("appeal.not-found");
        }

        if (candidate.Held)
        {
            return Error.Conflict("digest.held");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var weekStart = DigestRules.WeekStart(today);
        var already = await (from r in db.DigestRuns
                             join i in db.DigestItems on r.Id equals i.RunId
                             where r.Kind == DigestRules.Urgent && i.AppealId == appealId && r.RunDate >= weekStart && r.RunDate < weekStart.AddDays(7)
                             select r.Id).AnyAsync(cancellationToken);
        if (already)
        {
            return Error.Conflict("digest.urgent-already");
        }

        return await MakeAsync(db, DigestRules.Urgent, today, null, [candidate], moderator, now, cancellationToken, statement);
    }

    public async Task<IReadOnlyList<DigestRunInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.DigestRuns.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken);
    }

    public async Task<IReadOnlyList<DigestRunInfo>> OnDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => r.RunDate == date).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken);
    }

    public async Task<Result<DigestPreview>> PreviewAsync(Guid runId, string mpSlug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var run = await db.DigestRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        var delivery = await db.DigestDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.RunId == runId && d.MpSlug == mpSlug, cancellationToken);
        if (run is null || delivery is null)
        {
            return Error.NotFound("digest.not-found");
        }

        var info = (await InfoAsync(db, [run], cancellationToken))[0];
        var party = JsonSerializer.Deserialize<List<DeliveryParty>>(delivery.PartyNumbers, JsonSerializerOptions.Web) ?? [];
        return texts.Render(info, party, delivery.Status);
    }

    public async Task<IReadOnlyList<(string MpSlug, string Status)>> MpsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.DigestDeliveries.AsNoTracking().Where(d => d.RunId == runId).OrderBy(d => d.MpSlug)
            .Select(d => new { d.MpSlug, d.Status }).ToListAsync(cancellationToken);
        return rows.Select(r => (r.MpSlug, r.Status)).ToList();
    }

    private async Task<DigestRunInfo> MakeAsync(
        NotificationsDb db, string kind, DateOnly today, DateOnly? sitting, IReadOnlyList<DigestCandidate> items, StaffActor? approvedBy,
        DateTimeOffset now, CancellationToken ct, string? statement = null)
    {
        var recipients = (await mps.SittingMpsAsync(ct)).Where(m => m.HasOfficialAddress).ToList();
        var maxPerWeek = await settings.GetAsync(SettingKeys.DigestMaxPerWeek, ct);
        var weekStart = DigestRules.WeekStart(today);
        var party = await source.PartyNumbersAsync(items.Select(i => i.AppealId).ToList(), ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var counts = await (from d in db.DigestDeliveries
                            join r in db.DigestRuns on d.RunId equals r.Id
                            where d.Status == "dry-run" && r.RunDate >= weekStart && r.RunDate < weekStart.AddDays(7)
                            group d by d.MpSlug into g
                            select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var run = new DigestRun
        {
            Id = Ids.New(clock),
            Kind = kind,
            RunDate = today,
            SittingDate = sitting,
            CreatedAt = now.ToUniversalTime(),
            ApprovedBy = approvedBy?.UserId,
        };
        db.DigestRuns.Add(run);
        var position = 0;
        foreach (var c in items)
        {
            db.DigestItems.Add(new DigestItem
            {
                Id = Ids.New(clock),
                RunId = run.Id,
                Position = position++,
                AppealId = c.AppealId,
                BillNumber = c.BillNumber,
                BillTitle = c.BillTitle,
                Stage = c.Planned?.Stage,
                PlannedDate = c.Planned?.Date,
                ForCount = c.For,
                ForOlderThan30Days = c.ForOlderThan30Days,
                AgainstCount = c.Against,
                AgainstOlderThan30Days = c.AgainstOlderThan30Days,
                SurgeSharePercent = c.SurgeSharePercent,
                SurgeWindowHours = c.SurgeWindowHours,
                Reasons = JsonSerializer.Serialize(c.Reasons, JsonSerializerOptions.Web),
            });
        }

        foreach (var mp in recipients)
        {
            var within = DigestRules.WithinCeiling(counts.GetValueOrDefault(mp.Slug), maxPerWeek, kind == DigestRules.Urgent);
            var mine = mp.Party is null
                ? []
                : items.Select(i => party.GetValueOrDefault(i.AppealId)?.FirstOrDefault(p => p.Party == mp.Party) is { } n
                        ? new DeliveryParty(i.BillNumber, n.Party, n.For, n.Against)
                        : null)
                    .OfType<DeliveryParty>()
                    .ToList();
            db.DigestDeliveries.Add(new DigestDelivery
            {
                RunId = run.Id,
                MpSlug = mp.Slug,
                Status = within ? "dry-run" : "skipped-ceiling",
                PartyNumbers = JsonSerializer.Serialize(mine, JsonSerializerOptions.Web),
            });
            run.MpCount += within ? 1 : 0;
        }

        await db.SaveChangesAsync(ct);
        if (approvedBy is not null)
        {
            await audit.WriteAsync(
                new AuditEntry("digest.urgent.approved", approvedBy.UserId, approvedBy.Role, "digest:" + run.Id,
                    JsonSerializer.SerializeToElement(new { bills = items.Select(i => i.BillNumber), statement }, JsonSerializerOptions.Web), approvedBy.IpHash),
                transaction.GetDbTransaction(),
                ct);
        }

        await transaction.CommitAsync(ct);
        return (await InfoAsync(db, [run], ct))[0];
    }

    /// <summary>Participants (both sides) of each appeal in the latest summary that held it.</summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> LastParticipantsAsync(NotificationsDb db, CancellationToken ct)
    {
        var rows = await (from i in db.DigestItems
                          join r in db.DigestRuns on i.RunId equals r.Id
                          select new { i.AppealId, r.CreatedAt, Participants = i.ForCount + i.AgainstCount }).ToListAsync(ct);
        return rows.GroupBy(r => r.AppealId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First().Participants);
    }

    /// <summary>Appeals that week's Monday summary already showed with the same sitting date.</summary>
    private static async Task<IReadOnlySet<Guid>> CoveredByMondayAsync(NotificationsDb db, DateOnly weekStart, DateOnly sitting, CancellationToken ct) =>
        (await (from i in db.DigestItems
                join r in db.DigestRuns on i.RunId equals r.Id
                where r.Kind == DigestRules.Weekly && r.RunDate == weekStart && i.PlannedDate == sitting
                select i.AppealId).ToListAsync(ct)).ToHashSet();

    private static async Task<IReadOnlyList<DigestRunInfo>> InfoAsync(NotificationsDb db, IReadOnlyList<DigestRun> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.Id).ToList();
        var items = await db.DigestItems.AsNoTracking().Where(i => ids.Contains(i.RunId)).OrderBy(i => i.Position).ToListAsync(ct);
        return runs.Select(r => new DigestRunInfo(
                r.Id,
                r.Kind,
                r.RunDate,
                r.SittingDate,
                r.CreatedAt,
                r.Status,
                r.MpCount,
                items.Where(i => i.RunId == r.Id).Select(i => new DigestItemInfo(
                    i.BillNumber,
                    i.BillTitle,
                    i.Stage,
                    i.PlannedDate,
                    i.ForCount,
                    i.ForOlderThan30Days,
                    i.AgainstCount,
                    i.AgainstOlderThan30Days,
                    i.SurgeSharePercent,
                    i.SurgeWindowHours,
                    JsonSerializer.Deserialize<List<DigestReason>>(i.Reasons, JsonSerializerOptions.Web) ?? [])).ToList()))
            .ToList();
    }
}

/// <summary>The MP's party numbers for one bill in a delivery (groups of at least 20 only).</summary>
internal sealed record DeliveryParty(string BillNumber, string Party, int? For, int? Against);

/// <summary>The text an MP would read (design appendix A), for the moderators' preview. Never sent in M2a.</summary>
internal sealed class DigestText(Microsoft.Extensions.Localization.IStringLocalizer<Templates.EmailTemplates> l)
{
    public DigestPreview Render(DigestRunInfo run, IReadOnlyList<DeliveryParty> party, string status)
    {
        var lt = CultureInfo.GetCultureInfo("lt-LT");
        var numbers = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = [3] };
        string N(int value) => value.ToString("#,0", numbers);
        string F(string key, params object?[] values) => string.Format(CultureInfo.InvariantCulture, l[key], values);
        var text = new StringBuilder();
        text.AppendLine(l["SignInCode.Greeting"]).AppendLine();
        text.AppendLine(F("Digest.Intro", run.Items.Count)).AppendLine();
        foreach (var item in run.Items)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{item.BillNumber} · {item.BillTitle}");
            if (item.PlannedDate is { } planned)
            {
                text.AppendLine(F("Digest.Planned", planned.ToString("MMMM d", lt)));
            }

            text.AppendLine(F("Digest.For", N(item.For), N(item.ForOlderThan30Days)));
            text.AppendLine(F("Digest.Against", N(item.Against), N(item.AgainstOlderThan30Days)));
            if (party.FirstOrDefault(p => p.BillNumber == item.BillNumber) is { } mine)
            {
                if (mine.For is { } f)
                {
                    text.AppendLine(F("Digest.PartyFor", mine.Party, N(f)));
                }

                if (mine.Against is { } a)
                {
                    text.AppendLine(F("Digest.PartyAgainst", mine.Party, N(a)));
                }
            }

            if (item.SurgeSharePercent is { } share)
            {
                text.AppendLine(F("Digest.Surge", item.SurgeWindowHours, share));
            }

            foreach (var side in new[] { "for", "against" })
            {
                var reasons = item.Reasons.Where(r => r.Side == side).ToList();
                if (reasons.Count > 0)
                {
                    text.AppendLine(l["Digest.Reasons." + side]);
                    foreach (var r in reasons)
                    {
                        text.AppendLine(CultureInfo.InvariantCulture, $"  · {r.Text} ({N(r.Endorsements)})");
                    }
                }
                else
                {
                    // An appeal is opened without arguments (design 8.1): a side may have none yet.
                    text.AppendLine(l["Digest.NoReasons." + side]);
                }
            }

            text.AppendLine();
        }

        text.AppendLine(l["Digest.NotAPoll"]);
        text.AppendLine(F("Digest.PublicCopy", run.RunDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        if (status != "dry-run")
        {
            text.AppendLine(l["Digest.Skipped"]);
        }

        return new DigestPreview(F("Digest.Subject", run.RunDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), text.ToString());
    }
}