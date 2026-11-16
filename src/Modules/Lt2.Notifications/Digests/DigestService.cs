using System.Text.Json;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Notifications.Digests;

/// <summary>
/// The summaries (design 8.4, 8.7, 8.9, 8.19): the weekly and before-vote rules, the per-MP ceiling, eligibility by threshold,
/// held sending, the frozen snapshot with only arguments a moderator has seen, both sides with the 30-day counts and the surge
/// note. While sending is off (<c>Digest:Sending</c>, or a lock of 8.19.1) a summary is a dry run and nothing reaches the
/// outbox; otherwise it waits for a moderator or is sent, each MP's email queued with its address from the Parliament module's
/// one call for sending. Every summary is made, let go, declined and expired under one lock, one at a time.
/// </summary>
internal sealed class DigestService(
    IDbContextFactory<NotificationsDb> contexts,
    IDigestSource source,
    IMpDirectory mps,
    ISettings settings,
    IAuditLog audit,
    MpSender sender,
    MailLocks locks,
    MpMailTemplates templates,
    TimeProvider clock) : IDigests
{
    /// <summary>Summaries are made one at a time, so the weekly ceiling, "one urgent a week" and one send hold (design 6.8).</summary>
    internal const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended('lt2.digests', 0))";

    /// <summary>The import does not know the bills' lead committees yet (design 8.19.3): that group of the order is empty.</summary>
    private static readonly IReadOnlySet<string> NoLeadCommittees = new HashSet<string>();

    public async Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        now = now.ToUniversalTime();
        await ExpireDueAsync(now, cancellationToken);
        var local = TimeZoneInfo.ConvertTime(now, VilniusTime.Zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var weekStart = DigestRules.WeekStart(today);
        var weeklyAt = DigestRules.TimeOf(await settings.GetAsync(SettingKeys.DigestWeekly, cancellationToken));
        var beforeVoteAt = DigestRules.TimeOf(await settings.GetAsync(SettingKeys.DigestBeforeVote, cancellationToken));
        var sittings = await mps.SittingDaysAsync(weekStart, today.AddDays(10), cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var sendingOn = (await StateAsync(db, cancellationToken)).On;
        var made = new List<DigestRunInfo>();
        IReadOnlyList<DigestCandidate>? candidates = null;

        // "Already made" counts a weekly summary of any status: a declined one is not made again that week.
        Task<bool> WeeklyMadeAsync() =>
            db.DigestRuns.AnyAsync(r => r.Kind == DigestRules.Weekly && r.RunDate >= weekStart && r.RunDate < weekStart.AddDays(7), cancellationToken);
        if (DigestRules.WeeklyDue(today, time, weeklyAt, sittings.Any(d => d >= weekStart && d < weekStart.AddDays(7)), await WeeklyMadeAsync()))
        {
            candidates ??= await source.CandidatesAsync(cancellationToken);
            var last = await LastParticipantsAsync(db, sendingOn, cancellationToken);
            var notable = await settings.GetAsync(SettingKeys.DigestNotableChange, cancellationToken);
            var items = DigestRules.WeeklyItems(candidates, weekStart, last, notable);
            if (items.Count > 0
                && await MakeAsync(db, DigestRules.Weekly, today, null, items, null, now, cancellationToken, stillDue: async () => !await WeeklyMadeAsync()) is { IsSuccess: true } weekly)
            {
                made.Add(weekly.Value);
            }
        }

        foreach (var sitting in sittings.Where(d => d > today))
        {
            Task<bool> ExistsAsync() => db.DigestRuns.AnyAsync(r => r.Kind == DigestRules.BeforeVote && r.SittingDate == sitting, cancellationToken);
            if (!DigestRules.BeforeVoteDue(today, time, beforeVoteAt, sitting, await ExistsAsync()))
            {
                continue;
            }

            candidates ??= await source.CandidatesAsync(cancellationToken);
            var covered = await CoveredByMondayAsync(db, DigestRules.WeekStart(sitting), sitting, sendingOn, cancellationToken);
            var items = DigestRules.BeforeVoteItems(candidates, sitting, covered);
            if (items.Count > 0
                && await MakeAsync(db, DigestRules.BeforeVote, today, sitting, items, null, now, cancellationToken, stillDue: async () => !await ExistsAsync()) is { IsSuccess: true } beforeVote)
            {
                made.Add(beforeVote.Value);
            }
        }

        return made;
    }

    public async Task<Result<DigestRunInfo>> UrgentAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        // The moderator's statement is checked where it is stored (design 6.8), not only by the form.
        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var today = VilniusTime.DateOf(now);
        var candidate = (await source.CandidatesAsync(cancellationToken)).FirstOrDefault(c => c.RecordId == recordId);
        if (candidate is null)
        {
            return Error.NotFound("record.not-found");
        }

        if (candidate.Held)
        {
            return Error.Conflict("digest.held");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var weekStart = DigestRules.WeekStart(today);

        // Checked again under the lock, so two moderators approving at once make one urgent summary.
        return await MakeAsync(
            db, DigestRules.Urgent, today, null, [candidate], moderator, now, cancellationToken, statement.Trim(),
            stillDue: async () => !await (from r in db.DigestRuns
                                          join i in db.DigestItems on r.Id equals i.RunId
                                          where r.Kind == DigestRules.Urgent && i.RecordId == recordId && r.RunDate >= weekStart && r.RunDate < weekStart.AddDays(7)
                                          select r.Id).AnyAsync(cancellationToken),
            notDue: Error.Conflict("digest.urgent-already"));
    }

    public async Task<DigestSending> SendingAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var state = await StateAsync(db, cancellationToken);
        return new DigestSending(state.Mode, state.Setting, state.Lock);
    }

    public async Task<Result<DigestRunInfo>> SendAsync(Guid runId, StaffActor moderator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moderator);
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var recipients = (await mps.SittingMpsAsync(cancellationToken)).Where(m => m.HasOfficialAddress).ToList();
        var maxPerWeek = await settings.GetAsync(SettingKeys.DigestMaxPerWeek, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
        var run = await db.DigestRuns.FirstOrDefaultAsync(r => r.Id == runId && r.Kind != SendingRules.Introduction, cancellationToken);
        if (run is null)
        {
            return Error.NotFound("digest.not-found");
        }

        // Read under the lock: a second moderator pressing at the same moment finds it sent already.
        if (run.Status != RunStatuses.Waiting)
        {
            return Error.Conflict("digest.not-waiting");
        }

        if (SendingRules.Expired(now, run.ExpiresAt))
        {
            await ExpireAsync(db, run, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Error.Conflict("digest.expired");
        }

        var state = await StateAsync(db, cancellationToken);
        if (!state.On)
        {
            return Error.Conflict("digest.sending-off");
        }

        // Recipients are worked out now, not when it was made (design 8.19.2): preferences, suppression and the ceiling as they are.
        var info = (await InfoAsync(db, [run], cancellationToken))[0];
        var weekStart = DigestRules.WeekStart(run.RunDate);
        var counts = await CeilingCountsAsync(db, weekStart, sendingOn: true, exclude: run.Id, cancellationToken);
        var plan = await PlanAsync(db, run.Kind, recipients, counts, maxPerWeek, cancellationToken);
        var waiting = await db.DigestDeliveries.Where(d => d.RunId == run.Id).ToListAsync(cancellationToken);
        db.DigestDeliveries.RemoveRange(waiting);
        await db.SaveChangesAsync(cancellationToken);

        // The MP's party numbers as frozen when it was made; an MP who joined since has none in this summary.
        var partyOfMp = waiting.Where(d => d.Recipient == Recipients.Official).ToDictionary(d => d.MpSlug, d => d.PartyNumbers);
        await LetGoAsync(db, run, info, plan, slug => partyOfMp.GetValueOrDefault(slug) ?? "[]", now, cancellationToken);
        run.DecidedBy = moderator.UserId;
        run.DecidedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("digest.sent", moderator.UserId, moderator.Role, "digest:" + run.Id,
                JsonSerializer.SerializeToElement(new { kind = run.Kind, mps = run.SentCount }, JsonSerializerOptions.Web), moderator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await InfoAsync(db, [run], cancellationToken))[0];
    }

    public async Task<Result<DigestRunInfo>> CancelAsync(Guid runId, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moderator);
        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        if (statement.Trim().Length < SendingRules.CancelStatementMinimum)
        {
            return Error.Validation("statement.too-short", "statement");
        }

        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
        var run = await db.DigestRuns.FirstOrDefaultAsync(r => r.Id == runId && r.Kind != SendingRules.Introduction, cancellationToken);
        if (run is null)
        {
            return Error.NotFound("digest.not-found");
        }

        if (run.Status != RunStatuses.Waiting)
        {
            return Error.Conflict("digest.not-waiting");
        }

        run.Status = RunStatuses.Cancelled;
        run.Statement = statement.Trim();
        run.DecidedBy = moderator.UserId;
        run.DecidedAt = now;
        await db.DigestDeliveries.Where(d => d.RunId == run.Id && d.Status == DeliveryStatuses.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeliveryStatuses.NotSent), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("digest.cancelled", moderator.UserId, moderator.Role, "digest:" + run.Id,
                JsonSerializer.SerializeToElement(new { kind = run.Kind, statement = run.Statement }, JsonSerializerOptions.Web), moderator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await InfoAsync(db, [run], cancellationToken))[0];
    }

    public async Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        now = now.ToUniversalTime();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.DigestRuns.AnyAsync(r => r.Status == RunStatuses.Waiting && r.ExpiresAt <= now, cancellationToken))
        {
            return 0;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
        var due = await db.DigestRuns.Where(r => r.Status == RunStatuses.Waiting && r.ExpiresAt <= now).ToListAsync(cancellationToken);
        foreach (var run in due)
        {
            await ExpireAsync(db, run, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return due.Count;
    }

    public async Task<IReadOnlyList<DigestRunInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => r.Kind != SendingRules.Introduction)
            .OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken);
    }

    public async Task<IReadOnlyList<DigestRunInfo>> OnDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => r.RunDate == date && r.Kind != SendingRules.Introduction)
            .OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken);
    }

    public async Task<IReadOnlyList<DigestRunInfo>> ForRecordAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runIds = db.DigestItems.Where(i => i.RecordId == recordId).Select(i => i.RunId);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken, recordId);
    }

    public async Task<Result<DigestPreview>> PreviewAsync(Guid runId, string mpSlug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var run = await db.DigestRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && r.Kind != SendingRules.Introduction, cancellationToken);
        var delivery = await db.DigestDeliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.RunId == runId && d.MpSlug == mpSlug && d.Recipient == Recipients.Official, cancellationToken);
        var mp = await mps.FindMpAsync(mpSlug, cancellationToken);
        if (run is null || delivery is null || mp is null)
        {
            return Error.NotFound("digest.not-found");
        }

        // The same text the email carries (design 8.9), with the MP's own numbers and response as they are now; the MP's
        // personal links are left out.
        var info = (await InfoAsync(db, [run], cancellationToken))[0];
        var party = JsonSerializer.Deserialize<List<DeliveryParty>>(delivery.PartyNumbers, JsonSerializerOptions.Web) ?? [];
        var responses = await source.PublishedResponsesAsync(info.Items.Select(i => i.BillNumber).ToList(), cancellationToken);
        var model = Email(info, new MpRecipient(mp.Slug, mp.FirstName, mp.LastName, null, true, mp.SeimasPersonId), "", Recipients.Official,
            bill => party.FirstOrDefault(p => p.BillNumber == bill), responses, MpMailPaths.Preferences("…"), null, null, null);
        var (subject, paragraphs) = templates.Text(model);
        var text = string.Join("\n\n", paragraphs) + "\n";
        if (templates.SkippedNote(delivery.Status) is { } note)
        {
            text += "\n" + note + "\n";
        }

        return new DigestPreview(subject, text);
    }

    public async Task<IReadOnlyList<DigestMpInfo>> MpsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.DigestDeliveries.AsNoTracking().Where(d => d.RunId == runId && d.Recipient == Recipients.Official)
            .Select(d => new { d.MpSlug, d.SeimasPersonId, d.Status }).ToListAsync(cancellationToken);
        var names = await mps.MpsAsync(rows.Select(r => r.SeimasPersonId).Where(p => p != 0).Distinct().ToList(), cancellationToken);
        var lithuanian = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("lt-LT"), false);

        // The names in the nominative, by last name (a row from before 8.19 has no person number and keeps its slug).
        return rows
            .Select(r => names.GetValueOrDefault(r.SeimasPersonId) is { } mp
                ? new DigestMpInfo(r.MpSlug, mp.FirstName, mp.LastName, r.Status)
                : new DigestMpInfo(r.MpSlug, "", r.MpSlug, r.Status))
            .OrderBy(m => m.LastName, lithuanian)
            .ThenBy(m => m.FirstName, lithuanian)
            .ToList();
    }

    /// <summary>Sending as it is now (design 8.19.1): the setting, the letter, and the deployment's word on invented data.</summary>
    private async Task<SendingState> StateAsync(NotificationsDb db, CancellationToken ct) =>
        SendingRules.State(
            await settings.GetAsync(SettingKeys.DigestSending, ct),
            await db.IntroductionLetters.AnyAsync(l => l.SentAt != null, ct),
            await locks.InventedDataAsync(ct),
            locks.MailCaught);

    /// <summary>
    /// Makes one summary under the summaries' lock: <paramref name="stillDue"/> is asked again under it (another worker or
    /// moderator may have made the same one meanwhile), and the per-MP ceiling counts what is there by then. A dry run while
    /// sending is off; otherwise waiting (each MP's delivery worked out for the preview) or sent at once.
    /// </summary>
    private async Task<Result<DigestRunInfo>> MakeAsync(
        NotificationsDb db, string kind, DateOnly today, DateOnly? sitting, IReadOnlyList<DigestCandidate> items, StaffActor? approvedBy,
        DateTimeOffset now, CancellationToken ct, string? statement = null, Func<Task<bool>>? stillDue = null, Error? notDue = null)
    {
        var recipients = (await mps.SittingMpsAsync(ct)).Where(m => m.HasOfficialAddress).ToList();
        var maxPerWeek = await settings.GetAsync(SettingKeys.DigestMaxPerWeek, ct);
        var approvalHours = await settings.GetAsync(SettingKeys.DigestApprovalHours, ct);
        var weekStart = DigestRules.WeekStart(today);
        var party = await source.PartyNumbersAsync(items.Select(i => i.RecordId).ToList(), ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(LockSql, ct);
        if (stillDue is not null && !await stillDue())
        {
            return notDue ?? Error.Conflict("digest.made-already");
        }

        var state = await StateAsync(db, ct);
        var counts = await CeilingCountsAsync(db, weekStart, state.On, exclude: null, ct);
        var run = new DigestRun
        {
            Id = Ids.New(clock),
            Kind = kind,
            RunDate = today,
            SittingDate = sitting,
            CreatedAt = now.ToUniversalTime(),
            ApprovedBy = approvedBy?.UserId,
            Status = SendingRules.NewRunStatus(kind, state.Mode),
        };
        if (run.Status == RunStatuses.Waiting)
        {
            run.ExpiresAt = SendingRules.ExpiresAt(run.CreatedAt, approvalHours, SendingRules.SittingOf(sitting, today, items.Select(i => i.Planned?.Date)));
        }

        db.DigestRuns.Add(run);
        var position = 0;
        foreach (var c in items)
        {
            db.DigestItems.Add(new DigestItem
            {
                Id = Ids.New(clock),
                RunId = run.Id,
                Position = position++,
                RecordId = c.RecordId,
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

        string PartyJson(MpRecipient mp) => JsonSerializer.Serialize(
            mp.Party is null
                ? []
                : items.Select(i => party.GetValueOrDefault(i.RecordId)?.FirstOrDefault(p => p.Party == mp.Party) is { } n
                        ? new DeliveryParty(i.BillNumber, n.Party, n.For, n.Against)
                        : null)
                    .OfType<DeliveryParty>()
                    .ToList(),
            JsonSerializerOptions.Web);

        if (run.Status == RunStatuses.DryRun)
        {
            // Sending off: as before 8.19, every MP within the ceiling would have had it.
            foreach (var mp in recipients)
            {
                var within = DigestRules.WithinCeiling(counts.GetValueOrDefault(mp.Slug), maxPerWeek, kind == DigestRules.Urgent);
                db.DigestDeliveries.Add(new DigestDelivery
                {
                    RunId = run.Id,
                    MpSlug = mp.Slug,
                    SeimasPersonId = mp.SeimasPersonId,
                    Status = within ? DeliveryStatuses.DryRun : DeliveryStatuses.SkippedCeiling,
                    PartyNumbers = PartyJson(mp),
                });
                run.MpCount += within ? 1 : 0;
            }
        }
        else
        {
            var plan = await PlanAsync(db, kind, recipients, counts, maxPerWeek, ct);
            run.MpCount = plan.Count(p => p.Official is DeliveryStatuses.Queued or DeliveryStatuses.Introduced);
            if (run.Status == RunStatuses.Waiting)
            {
                // Frozen now, queued only when a moderator lets it go: one row per MP for the preview and the count.
                foreach (var p in plan)
                {
                    db.DigestDeliveries.Add(new DigestDelivery
                    {
                        RunId = run.Id,
                        MpSlug = p.Mp.Slug,
                        SeimasPersonId = p.Mp.SeimasPersonId,
                        Status = p.Official == DeliveryStatuses.Queued ? DeliveryStatuses.Waiting : p.Official,
                        PartyNumbers = PartyJson(p.Mp),
                    });
                }
            }
            else
            {
                await db.SaveChangesAsync(ct);
                var info = (await InfoAsync(db, [run], ct))[0];
                var byslug = recipients.ToDictionary(m => m.Slug);
                await LetGoAsync(db, run, info, plan, slug => PartyJson(byslug[slug]), now, ct);
                run.DecidedBy = approvedBy?.UserId;
                run.DecidedAt = now.ToUniversalTime();
            }
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

        if (run.Status == RunStatuses.Sent)
        {
            await audit.WriteAsync(
                new AuditEntry("digest.sent", approvedBy?.UserId, approvedBy?.Role, "digest:" + run.Id,
                    JsonSerializer.SerializeToElement(new { kind = run.Kind, mps = run.SentCount }, JsonSerializerOptions.Web), approvedBy?.IpHash),
                transaction.GetDbTransaction(),
                ct);
        }

        await transaction.CommitAsync(ct);
        return (await InfoAsync(db, [run], ct))[0];
    }

    /// <summary>
    /// Each sitting MP's delivery as things are now (design 8.19.3): the suppression list, the MP's choice, the introduction
    /// letter, the ceiling; and the confirmed extra recipient's copy.
    /// </summary>
    private async Task<IReadOnlyList<PlannedDelivery>> PlanAsync(
        NotificationsDb db, string kind, IReadOnlyList<MpRecipient> recipients, IReadOnlyDictionary<string, int> counts, int maxPerWeek, CancellationToken ct)
    {
        var addresses = await sender.AddressesAsync(recipients.Select(m => m.SeimasPersonId), ct);
        var prefs = await MpSender.PrefsAsync(db, recipients.Select(m => m.SeimasPersonId).ToList(), ct);
        var introduced = await MpSender.IntroducedAsync(db, ct);
        var suppressed = await sender.SuppressedAsync(
            db, addresses.Values.Concat(prefs.Values.Select(p => p.ExtraAddress).OfType<string>()), ct);
        var plan = new List<PlannedDelivery>();
        foreach (var mp in recipients)
        {
            if (!addresses.TryGetValue(mp.SeimasPersonId, out var address))
            {
                continue;
            }

            var pref = prefs.GetValueOrDefault(mp.SeimasPersonId);
            var official = SendingRules.DeliveryStatus(
                sender.IsSuppressed(suppressed, address), pref?.Frequency ?? Frequencies.Default, kind, introduced.Contains(mp.SeimasPersonId),
                counts.GetValueOrDefault(mp.Slug), maxPerWeek);
            var extra = SendingRules.ExtraStatus(official, pref is { ExtraAddress: not null, ExtraConfirmedAt: not null }, sender.IsSuppressed(suppressed, pref?.ExtraAddress));
            plan.Add(new PlannedDelivery(mp, official, extra, address, pref));
        }

        return plan;
    }

    /// <summary>
    /// Lets a summary go in the caller's transaction: one delivery row per MP and recipient, each email queued with its
    /// address, the introduction letter to an MP who has not had it, and the records counted as summarised (design 8.12.2).
    /// </summary>
    private async Task LetGoAsync(
        NotificationsDb db, DigestRun run, DigestRunInfo info, IReadOnlyList<PlannedDelivery> plan, Func<string, string> partyJson, DateTimeOffset now, CancellationToken ct)
    {
        var letter = await MpSender.SentLetterAsync(db, ct);
        var responses = await source.PublishedResponsesAsync(info.Items.Select(i => i.BillNumber).ToList(), ct);
        foreach (var p in plan)
        {
            var json = partyJson(p.Mp.Slug);
            var party = JsonSerializer.Deserialize<List<DeliveryParty>>(json, JsonSerializerOptions.Web) ?? [];
            var official = p.Official;
            if (official == DeliveryStatuses.Introduced && letter is null)
            {
                // Cannot happen while sending is on (the lock of 8.19.1); kept from writing an email without its letter.
                official = DeliveryStatuses.SkippedPreference;
            }

            db.DigestDeliveries.Add(new DigestDelivery
            {
                RunId = run.Id, MpSlug = p.Mp.Slug, SeimasPersonId = p.Mp.SeimasPersonId, Recipient = Recipients.Official, Status = official, PartyNumbers = json,
            });
            if (official == DeliveryStatuses.Queued)
            {
                var token = await sender.PreferencesTokenAsync(db, p.Mp.SeimasPersonId, p.Pref?.TokenGeneration ?? 0, ct);
                await sender.EnqueueAsync(
                    db,
                    MpMailKinds.Digest,
                    Email(info, p.Mp, p.Address!, Recipients.Official, bill => party.FirstOrDefault(x => x.BillNumber == bill), responses,
                        MpMailPaths.Preferences(token), MpMailPaths.OneClick(token), null, new DeliveryRef(run.Id, p.Mp.Slug, Recipients.Official)),
                    ct);
            }
            else if (official == DeliveryStatuses.Introduced)
            {
                await sender.EnqueueIntroductionAsync(db, letter!, p.Mp, p.Address!, p.Pref, run.Id, ct);
            }

            if (p.Extra is { } extra && p.Pref is { ExtraAddress: { } extraAddress } pref)
            {
                db.DigestDeliveries.Add(new DigestDelivery
                {
                    RunId = run.Id, MpSlug = p.Mp.Slug, SeimasPersonId = p.Mp.SeimasPersonId, Recipient = Recipients.Extra, Status = extra, PartyNumbers = json,
                });
                if (extra == DeliveryStatuses.Queued)
                {
                    var stop = await sender.ExtraStopTokenAsync(db, p.Mp.SeimasPersonId, pref.ExtraGeneration, ct);
                    await sender.EnqueueAsync(
                        db,
                        MpMailKinds.Digest,
                        Email(info, p.Mp, extraAddress, Recipients.Extra, bill => party.FirstOrDefault(x => x.BillNumber == bill), responses,
                            null, null, MpMailPaths.StopCopies(stop), new DeliveryRef(run.Id, p.Mp.Slug, Recipients.Extra)),
                        ct);
                }
            }
        }

        run.Status = RunStatuses.Sent;
        run.SentCount = plan.Count(p => p.Official == DeliveryStatuses.Queued);
        await source.MarkSummarisedAsync(
            await db.DigestItems.Where(i => i.RunId == run.Id).Select(i => i.RecordId).ToListAsync(ct), now, db.Database.CurrentTransaction!.GetDbTransaction(), ct);
    }

    /// <summary>One MP's email: the bills in the MP's order, each with the MP's party numbers and own response.</summary>
    private static MpDigestEmail Email(
        DigestRunInfo info, MpRecipient mp, string to, string recipient, Func<string, DeliveryParty?> party, IReadOnlyList<MpStance> responses,
        string? preferencesPath, string? unsubscribePath, string? copyStopPath, DeliveryRef? delivery) =>
        new(
            to,
            recipient,
            mp.FirstName + " " + mp.LastName,
            info.Kind,
            info.RunDate,
            SendingRules.OrderForMp(info.Items, NoLeadCommittees)
                .Select(i => new MpDigestBill(i, party(i.BillNumber), responses.FirstOrDefault(r => r.SeimasPersonId == mp.SeimasPersonId && r.BillNumber == i.BillNumber)))
                .ToList(),
            MpMailPaths.PublicCopy(info.RunDate),
            preferencesPath,
            unsubscribePath,
            copyStopPath,
            delivery);

    /// <summary>A waiting run whose time passed: nothing is sent late (design 8.19.2).</summary>
    private async Task ExpireAsync(NotificationsDb db, DigestRun run, DateTimeOffset now, CancellationToken ct)
    {
        run.Status = RunStatuses.Expired;
        run.DecidedAt = now;
        await db.DigestDeliveries.Where(d => d.RunId == run.Id && d.Status == DeliveryStatuses.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeliveryStatuses.NotSent), ct);
        await audit.WriteAsync(
            new AuditEntry("digest.expired", null, null, "digest:" + run.Id, JsonSerializer.SerializeToElement(new { kind = run.Kind }, JsonSerializerOptions.Web)),
            db.Database.CurrentTransaction?.GetDbTransaction(),
            ct);
    }

    /// <summary>How many summaries each MP has this week, counting only the runs and deliveries of design 8.19.1.</summary>
    private static async Task<IReadOnlyDictionary<string, int>> CeilingCountsAsync(NotificationsDb db, DateOnly weekStart, bool sendingOn, Guid? exclude, CancellationToken ct)
    {
        var runStatuses = SendingRules.CountingStatuses(sendingOn).ToList();
        var deliveryStatuses = SendingRules.CeilingDeliveryStatuses.ToList();
        var weekEnd = weekStart.AddDays(7);
        return await (from d in db.DigestDeliveries
                      join r in db.DigestRuns on d.RunId equals r.Id
                      where d.Recipient == Recipients.Official && r.Kind != SendingRules.Introduction
                            && runStatuses.Contains(r.Status) && deliveryStatuses.Contains(d.Status)
                            && r.RunDate >= weekStart && r.RunDate < weekEnd && (exclude == null || r.Id != exclude)
                      group d by d.MpSlug into g
                      select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    /// <summary>
    /// Participants (both sides) of each record in the latest counting summary that held it (design 8.19.1): one row per
    /// record, from SQL.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> LastParticipantsAsync(NotificationsDb db, bool sendingOn, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT ON (i.record_id) i.record_id, i.for_count + i.against_count
            FROM notify.digest_item i JOIN notify.digest_run r ON r.id = i.run_id
            WHERE r.status = ANY(@statuses)
            ORDER BY i.record_id, r.created_at DESC
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "statuses";
        parameter.Value = SendingRules.CountingStatuses(sendingOn).ToArray();
        command.Parameters.Add(parameter);
        var result = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetGuid(0)] = reader.GetInt32(1);
        }

        return result;
    }

    /// <summary>Records that week's Monday summary (a counting one) already showed with the same sitting date.</summary>
    private static async Task<IReadOnlySet<Guid>> CoveredByMondayAsync(NotificationsDb db, DateOnly weekStart, DateOnly sitting, bool sendingOn, CancellationToken ct)
    {
        var statuses = SendingRules.CountingStatuses(sendingOn).ToList();
        return (await (from i in db.DigestItems
                       join r in db.DigestRuns on i.RunId equals r.Id
                       where r.Kind == DigestRules.Weekly && r.RunDate == weekStart && i.PlannedDate == sitting && statuses.Contains(r.Status)
                       select i.RecordId).ToListAsync(ct)).ToHashSet();
    }

    /// <summary>The runs as their public copy shows them; with <paramref name="recordId"/>, only that record's item.</summary>
    private static async Task<IReadOnlyList<DigestRunInfo>> InfoAsync(NotificationsDb db, IReadOnlyList<DigestRun> runs, CancellationToken ct, Guid? recordId = null)
    {
        var ids = runs.Select(r => r.Id).ToList();
        var items = await db.DigestItems.AsNoTracking()
            .Where(i => ids.Contains(i.RunId) && (recordId == null || i.RecordId == recordId))
            .OrderBy(i => i.Position).ToListAsync(ct);
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
                    JsonSerializer.Deserialize<List<DigestReason>>(i.Reasons, JsonSerializerOptions.Web) ?? [])).ToList(),
                r.SentCount,
                r.DecidedAt,
                r.Status == RunStatuses.Cancelled ? r.Statement : null,
                r.Status == RunStatuses.Waiting ? r.ExpiresAt : null))
            .ToList();
    }
}

/// <summary>The MP's party numbers for one bill in a delivery (groups of at least 20 only).</summary>
internal sealed record DeliveryParty(string BillNumber, string Party, int? For, int? Against);
