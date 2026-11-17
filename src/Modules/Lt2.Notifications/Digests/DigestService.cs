using System.Text.Json;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Notifications.Digests;

/// <summary>
/// The summaries (design 8.4, 8.7, 8.9, 8.19; owner 2026-11-17: reviewed a day ahead). A weekly or before-vote summary is
/// prepared <c>Digest:ReviewHours</c> before its time: which bills (eligible and not held then) and, per bill and side, which
/// arguments (the most endorsed published ones) are fixed, and every moderator and administrator is told. Moderators may hide
/// an argument (the next of its side comes in), hold a bill (it drops out), approve it ("Patvirtinti": reviewed, it still goes at
/// its time) or decline it ("Nesiųsti"); if nobody acts it goes at its time as prepared. When it goes every number is read
/// then and the snapshot and public copy are made: a dry run while sending is off (<c>Digest:Sending</c>, or a lock of 8.19.1),
/// otherwise each MP's email is queued with its address from the Parliament module's one call for sending. Every summary is
/// prepared, changed and let go under one lock, one at a time.
/// </summary>
internal sealed class DigestService(
    IDbContextFactory<NotificationsDb> contexts,
    IDigestSource source,
    IMpDirectory mps,
    ISettings settings,
    IAuditLog audit,
    IOutbox outbox,
    IStaffAddresses staff,
    IMpLinks links,
    MpSender sender,
    MailKeys keys,
    MailLocks locks,
    TimeProvider clock) : IDigests
{
    /// <summary>Summaries are made one at a time, so the weekly ceiling, "one urgent a week" and one send hold (design 6.8).</summary>
    internal const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended('lt2.digests', 0))";

    /// <summary>The moderators' summaries screen (<c>DigestRoutes.ModerationPage</c> in the contracts; a unit test pins them).</summary>
    internal const string ReviewPath = "/moderavimas/suvestines";

    /// <summary>The import does not know the bills' lead committees yet (design 8.19.3): that group of the order is empty.</summary>
    private static readonly IReadOnlySet<string> NoLeadCommittees = new HashSet<string>();

    /// <summary>The statuses of a summary that has gone or was declined, which the public copy shows.</summary>
    private static readonly string[] PublicStatuses = [RunStatuses.DryRun, RunStatuses.Sent, RunStatuses.Cancelled];

    public async Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        now = now.ToUniversalTime();
        var today = VilniusTime.DateOf(now);
        var reviewHours = await settings.GetAsync(SettingKeys.DigestReviewHours, cancellationToken);
        var weeklyAt = DigestRules.TimeOf(await settings.GetAsync(SettingKeys.DigestWeekly, cancellationToken));
        var beforeVoteAt = DigestRules.TimeOf(await settings.GetAsync(SettingKeys.DigestBeforeVote, cancellationToken));
        var touched = new List<Guid>();

        await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            var sendingOn = (await StateAsync(db, cancellationToken)).On;
            IReadOnlyList<DigestCandidate>? candidates = null;

            // The weekly summary: the coming Monday's (today's on a Monday), in a week when the Seimas sits, once a week. "Already
            // made" counts a summary of any status: a declined one is not made again that week.
            if (weeklyAt is { } weeklyTime)
            {
                var monday = DigestRules.WeeklySendingDay(today);
                var sendAt = DigestRules.At(monday, weeklyTime);
                Task<bool> WeeklyMadeAsync() =>
                    db.DigestRuns.AnyAsync(r => r.Kind == DigestRules.Weekly && r.RunDate >= monday && r.RunDate < monday.AddDays(7), cancellationToken);
                if (DigestRules.PreparationDue(now, sendAt, reviewHours, await WeeklyMadeAsync())
                    && (await mps.SittingDaysAsync(monday, monday.AddDays(6), cancellationToken)).Count > 0)
                {
                    candidates ??= await source.CandidatesAsync(cancellationToken);
                    var last = await LastParticipantsAsync(db, sendingOn, cancellationToken);
                    var notable = await settings.GetAsync(SettingKeys.DigestNotableChange, cancellationToken);
                    var items = DigestRules.WeeklyItems(candidates, monday, last, notable);
                    if (items.Count > 0 && await PrepareAsync(db, DigestRules.Weekly, monday, null, sendAt, items, now, null, async () => !await WeeklyMadeAsync(), cancellationToken) is { } weekly)
                    {
                        touched.Add(weekly.Id);
                    }
                }
            }

            // The before-vote summary: on the working day before a sitting, once per sitting.
            if (beforeVoteAt is { } beforeVoteTime)
            {
                foreach (var sitting in await mps.SittingDaysAsync(today.AddDays(1), today.AddDays(10), cancellationToken))
                {
                    var day = DigestRules.WorkingDayBefore(sitting);
                    var sendAt = DigestRules.At(day, beforeVoteTime);
                    Task<bool> ExistsAsync() => db.DigestRuns.AnyAsync(r => r.Kind == DigestRules.BeforeVote && r.SittingDate == sitting, cancellationToken);
                    if (!DigestRules.PreparationDue(now, sendAt, reviewHours, await ExistsAsync()))
                    {
                        continue;
                    }

                    candidates ??= await source.CandidatesAsync(cancellationToken);
                    var covered = await CoveredByMondayAsync(db, DigestRules.WeekStart(sitting), sitting, sendingOn, cancellationToken);
                    var items = DigestRules.BeforeVoteItems(candidates, sitting, covered);
                    if (items.Count > 0 && await PrepareAsync(db, DigestRules.BeforeVote, day, sitting, sendAt, items, now, null, async () => !await ExistsAsync(), cancellationToken) is { } beforeVote)
                    {
                        touched.Add(beforeVote.Id);
                    }
                }
            }

            // Every prepared summary whose time has come goes, as prepared: nobody approves it.
            var due = await db.DigestRuns.AsNoTracking()
                .Where(r => r.Status == RunStatuses.Prepared && (r.DueAt == null || r.DueAt <= now))
                .OrderBy(r => r.DueAt).Select(r => r.Id).ToListAsync(cancellationToken);
            foreach (var id in due)
            {
                await GoAsync(id, now, cancellationToken);
                touched.Add(id);
            }
        }

        await using var read = await contexts.CreateDbContextAsync(cancellationToken);
        var ids = touched.Distinct().ToList();
        var runs = await read.DigestRuns.AsNoTracking().Where(r => ids.Contains(r.Id)).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        return await InfoAsync(read, runs, cancellationToken);
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

        // Checked again under the lock, so two moderators approving at once make one urgent summary. Approving it is its
        // review (design 8.4): it is prepared and goes at once.
        var prepared = await PrepareAsync(
            db, DigestRules.Urgent, today, null, now, [candidate], now, (moderator, statement.Trim()),
            async () => !await (from r in db.DigestRuns
                                join i in db.DigestItems on r.Id equals i.RunId
                                where r.Kind == DigestRules.Urgent && i.RecordId == recordId && r.RunDate >= weekStart && r.RunDate < weekStart.AddDays(7)
                                select r.Id).AnyAsync(cancellationToken),
            cancellationToken);
        if (prepared is null)
        {
            return Error.Conflict("digest.urgent-already");
        }

        await GoAsync(prepared.Id, now, cancellationToken, moderator);
        var run = await db.DigestRuns.AsNoTracking().FirstAsync(r => r.Id == prepared.Id, cancellationToken);
        return (await InfoAsync(db, [run], cancellationToken))[0];
    }

    public async Task<DigestSending> SendingAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var state = await StateAsync(db, cancellationToken);
        return new DigestSending(state.Mode, state.Setting, state.Lock);
    }

    public async Task<Result<DigestRunInfo>> ApproveAsync(Guid runId, StaffActor moderator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moderator);
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
        var run = await db.DigestRuns.FirstOrDefaultAsync(r => r.Id == runId && r.Kind != SendingRules.Introduction, cancellationToken);
        if (run is null)
        {
            return Error.NotFound("digest.not-found");
        }

        // Read under the lock: a second moderator pressing at the same moment finds it approved already.
        if (run.Status != RunStatuses.Prepared || run.ApprovedAt is not null)
        {
            return Error.Conflict("digest.not-prepared");
        }

        run.ApprovedBy = moderator.UserId;
        run.ApprovedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("digest.approved", moderator.UserId, moderator.Role, "digest:" + run.Id,
                JsonSerializer.SerializeToElement(new { kind = run.Kind, dueAt = run.DueAt }, JsonSerializerOptions.Web), moderator.IpHash),
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
        var candidates = await source.CandidatesAsync(cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
        var run = await db.DigestRuns.FirstOrDefaultAsync(r => r.Id == runId && r.Kind != SendingRules.Introduction, cancellationToken);
        if (run is null)
        {
            return Error.NotFound("digest.not-found");
        }

        if (run.Status != RunStatuses.Prepared)
        {
            return Error.Conflict("digest.not-prepared");
        }

        // The public copy says it was not sent and shows what it held at that moment, as it would have gone then.
        await FreezeAsync(db, run, candidates, now, cancellationToken);
        run.Status = RunStatuses.Cancelled;
        run.Statement = statement.Trim();
        run.CancelledBy = moderator.UserId;
        run.CancelledAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("digest.cancelled", moderator.UserId, moderator.Role, "digest:" + run.Id,
                JsonSerializer.SerializeToElement(new { kind = run.Kind, statement = run.Statement }, JsonSerializerOptions.Web), moderator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await InfoAsync(db, [run], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<DigestRunInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => r.Kind != SendingRules.Introduction && PublicStatuses.Contains(r.Status))
            .OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken);
    }

    public async Task<IReadOnlyList<DigestRunInfo>> OnDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => r.RunDate == date && r.Kind != SendingRules.Introduction && PublicStatuses.Contains(r.Status))
            .OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken);
    }

    public async Task<IReadOnlyList<DigestRunInfo>> ForRecordAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runIds = db.DigestItems.Where(i => i.RecordId == recordId).Select(i => i.RunId);
        var runs = await db.DigestRuns.AsNoTracking().Where(r => runIds.Contains(r.Id) && PublicStatuses.Contains(r.Status))
            .OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        return await InfoAsync(db, runs, cancellationToken, recordId);
    }

    public async Task<IReadOnlyList<DigestReview>> ReviewAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var prepared = await db.DigestRuns.AsNoTracking().Where(r => r.Status == RunStatuses.Prepared)
            .OrderBy(r => r.DueAt).Select(r => r.Id).ToListAsync(cancellationToken);
        var result = new List<DigestReview>();
        if (prepared.Count > 0)
        {
            var candidates = (await source.CandidatesAsync(cancellationToken)).ToDictionary(c => c.RecordId);
            var state = await StateAsync(db, cancellationToken);
            var recipients = (await mps.SittingMpsAsync(cancellationToken)).Where(m => m.HasOfficialAddress).ToList();
            var maxPerWeek = await settings.GetAsync(SettingKeys.DigestMaxPerWeek, cancellationToken);
            foreach (var id in prepared)
            {
                // An argument hidden since the last look is replaced now and stays replaced, so the list shown is the one that goes.
                var pool = await ReconcileAsync(db, id, now, cancellationToken);
                if (pool is null)
                {
                    continue;
                }

                var run = await db.DigestRuns.AsNoTracking().FirstAsync(r => r.Id == id, cancellationToken);
                var items = await db.DigestItems.AsNoTracking().Where(i => i.RunId == id).OrderBy(i => i.Position).ToListAsync(cancellationToken);
                var choices = await db.DigestChoices.AsNoTracking().Where(c => c.RunId == id && c.LeftAt == null).ToListAsync(cancellationToken);
                var byId = pool.ToDictionary(r => r.Id);
                var counts = await CeilingCountsAsync(db, DigestRules.WeekStart(run.RunDate), state.On, exclude: run.Id, cancellationToken);
                var wouldGo = state.On
                    ? (await PlanAsync(db, run.Kind, recipients, counts, maxPerWeek, cancellationToken)).Count(p => p.Official is DeliveryStatuses.Queued or DeliveryStatuses.Introduced)
                    : recipients.Count(m => DigestRules.WithinCeiling(counts.GetValueOrDefault(m.Slug), maxPerWeek, run.Kind == DigestRules.Urgent));
                result.Add(new DigestReview(
                    run.Id, run.Kind, run.RunDate, run.SittingDate, run.CreatedAt, run.DueAt, run.Status, wouldGo, 0, null, run.ApprovedAt, null,
                    items.Select(i =>
                    {
                        var c = candidates.GetValueOrDefault(i.RecordId);
                        var reasons = Sides()
                            .SelectMany(side => choices.Where(x => x.RecordId == i.RecordId && x.Side == side && byId.ContainsKey(x.ReasonId))
                                .Select(x => (Choice: x, Reason: byId[x.ReasonId]))
                                .OrderByDescending(x => x.Reason.Endorsements).ThenBy(x => x.Choice.Place)
                                .Select(x => new DigestReviewReason(x.Reason.Id, side, x.Reason.Text, x.Reason.Endorsements, x.Choice.Replacement)))
                            .ToList();
                        return c is null
                            ? new DigestReviewItem(i.RecordId, i.BillNumber, i.BillTitle, i.Stage, i.PlannedDate, i.ForCount, i.ForOlderThan30Days, i.AgainstCount,
                                i.AgainstOlderThan30Days, i.SurgeSharePercent, i.SurgeWindowHours, Held: false, Gone: true, reasons)
                            : new DigestReviewItem(i.RecordId, c.BillNumber, c.BillTitle, c.Planned?.Stage ?? i.Stage, c.Planned?.Date ?? i.PlannedDate, c.For,
                                c.ForOlderThan30Days, c.Against, c.AgainstOlderThan30Days, c.SurgeSharePercent, c.SurgeWindowHours, c.Held, Gone: false, reasons);
                    }).ToList()));
            }
        }

        // Then the latest that went or were declined, as they went.
        var gone = await db.DigestRuns.AsNoTracking()
            .Where(r => r.Kind != SendingRules.Introduction && r.Status != RunStatuses.Prepared)
            .OrderByDescending(r => r.SentAt ?? r.CancelledAt ?? r.CreatedAt).Take(30).ToListAsync(cancellationToken);
        var goneItems = await db.DigestItems.AsNoTracking().Where(i => gone.Select(r => r.Id).Contains(i.RunId)).OrderBy(i => i.Position).ToListAsync(cancellationToken);
        foreach (var run in gone)
        {
            result.Add(new DigestReview(
                run.Id, run.Kind, run.RunDate, run.SittingDate, run.CreatedAt, run.DueAt, run.Status,
                run.Status == RunStatuses.Sent ? run.SentCount : run.MpCount, run.SentCount, run.SentAt, run.ApprovedAt, run.Statement,
                goneItems.Where(i => i.RunId == run.Id).Select(i => new DigestReviewItem(
                    i.RecordId, i.BillNumber, i.BillTitle, i.Stage, i.PlannedDate, i.ForCount, i.ForOlderThan30Days, i.AgainstCount,
                    i.AgainstOlderThan30Days, i.SurgeSharePercent, i.SurgeWindowHours, false, false,
                    Snapshot(i).Select(r => new DigestReviewReason(null, r.Side, r.Text, r.Endorsements, false)).ToList())).ToList()));
        }

        return result;
    }

    public async Task<DigestPending?> PendingAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var run = await db.DigestRuns.AsNoTracking()
            .Where(r => r.Status == RunStatuses.Prepared && r.ApprovedAt == null)
            .OrderBy(r => r.DueAt).FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        var bills = await db.DigestItems.CountAsync(i => i.RunId == run.Id, cancellationToken);
        var reasons = await db.DigestChoices.CountAsync(c => c.RunId == run.Id && c.LeftAt == null, cancellationToken);
        return new DigestPending(run.DueAt ?? run.CreatedAt, bills, reasons);
    }

    /// <summary>Sending as it is now (design 8.19.1): the setting, the letter, and the deployment's word on invented data.</summary>
    private async Task<SendingState> StateAsync(NotificationsDb db, CancellationToken ct) =>
        SendingRules.State(
            await settings.GetAsync(SettingKeys.DigestSending, ct),
            await db.IntroductionLetters.AnyAsync(l => l.SentAt != null, ct),
            await locks.InventedDataAsync(ct),
            locks.MailCaught);

    private static string[] Sides() => ["for", "against"];

    /// <summary>
    /// Prepares one summary under the summaries' lock (design 8.19.2): <paramref name="stillDue"/> is asked again under it
    /// (another worker or moderator may have made the same one meanwhile). Fixes the bills and, per bill and side, the most
    /// endorsed published arguments; tells every moderator and administrator, except for an urgent summary, which a moderator
    /// approves (<paramref name="approved"/>) and which goes at once. Null when it was made meanwhile.
    /// </summary>
    private async Task<DigestRun?> PrepareAsync(
        NotificationsDb db, string kind, DateOnly runDate, DateOnly? sitting, DateTimeOffset dueAt, IReadOnlyList<DigestCandidate> items, DateTimeOffset now,
        (StaffActor Moderator, string Statement)? approved, Func<Task<bool>> stillDue, CancellationToken ct)
    {
        var pool = await source.ReasonPoolAsync(items.Select(i => i.RecordId).ToList(), now, ct);
        var chosen = ChoiceRules.Choose(pool, DigestRules.ReasonsPerSide);
        var moderators = approved is null ? await staff.ModeratorAddressesAsync(ct) : [];

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(LockSql, ct);
        if (!await stillDue())
        {
            return null;
        }

        var run = new DigestRun
        {
            Id = Ids.New(clock),
            Kind = kind,
            RunDate = runDate,
            SittingDate = sitting,
            CreatedAt = now.ToUniversalTime(),
            DueAt = dueAt.ToUniversalTime(),
            Status = RunStatuses.Prepared,
            ApprovedBy = approved?.Moderator.UserId,
            ApprovedAt = approved is null ? null : now.ToUniversalTime(),
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
            });
        }

        foreach (var c in chosen)
        {
            db.DigestChoices.Add(new DigestChoice { RunId = run.Id, ReasonId = c.ReasonId, RecordId = c.RecordId, Side = c.Side, Place = c.Place });
        }

        await db.SaveChangesAsync(ct);
        var dbTransaction = transaction.GetDbTransaction();
        foreach (var address in moderators)
        {
            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.DigestReviewEmail, new DigestReviewEmail(address, kind, run.DueAt!.Value, items.Count, chosen.Count, ReviewPath)),
                dbTransaction,
                ct);
        }

        await audit.WriteAsync(
            approved is { } by
                ? new AuditEntry("digest.urgent.approved", by.Moderator.UserId, by.Moderator.Role, "digest:" + run.Id,
                    JsonSerializer.SerializeToElement(new { bills = items.Select(i => i.BillNumber), statement = by.Statement }, JsonSerializerOptions.Web), by.Moderator.IpHash)
                : new AuditEntry("digest.prepared", null, null, "digest:" + run.Id,
                    JsonSerializer.SerializeToElement(new { kind, dueAt = run.DueAt, bills = items.Count, reasons = chosen.Count }, JsonSerializerOptions.Web)),
            dbTransaction,
            ct);
        await transaction.CommitAsync(ct);
        return run;
    }

    /// <summary>
    /// Hiding brings the next one in (design 8.19.2): in its own transaction under the lock, every chosen argument of a prepared
    /// summary no longer published leaves and the next of its side by "Man svarbu" now comes in, from the arguments public when
    /// it was prepared. Returns that pool, or null when the run is no longer prepared.
    /// </summary>
    private async Task<IReadOnlyList<DigestPoolReason>?> ReconcileAsync(NotificationsDb db, Guid runId, DateTimeOffset now, CancellationToken ct)
    {
        var run = await db.DigestRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is not { Status: RunStatuses.Prepared })
        {
            return null;
        }

        var records = await db.DigestItems.AsNoTracking().Where(i => i.RunId == runId).Select(i => i.RecordId).ToListAsync(ct);
        var pool = await source.ReasonPoolAsync(records, run.CreatedAt, ct);
        var current = await db.DigestChoices.AsNoTracking().Where(c => c.RunId == runId).ToListAsync(ct);
        if (ChoiceRules.Replace(current.Select(ToChosen).ToList(), pool).IsEmpty)
        {
            return pool;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(LockSql, ct);
        if (!await db.DigestRuns.AnyAsync(r => r.Id == runId && r.Status == RunStatuses.Prepared, ct))
        {
            return null;
        }

        await ApplyReplacementsAsync(db, runId, pool, now, ct);
        await transaction.CommitAsync(ct);
        return pool;
    }

    /// <summary>Applies <see cref="ChoiceRules.Replace"/> to a run's chosen arguments, in the caller's transaction.</summary>
    private static async Task ApplyReplacementsAsync(NotificationsDb db, Guid runId, IReadOnlyList<DigestPoolReason> pool, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = await db.DigestChoices.Where(c => c.RunId == runId).ToListAsync(ct);
        var change = ChoiceRules.Replace(tracked.Select(ToChosen).ToList(), pool);
        foreach (var leaving in tracked.Where(c => change.Leaving.Contains(c.ReasonId)))
        {
            leaving.LeftAt = now.ToUniversalTime();
        }

        foreach (var c in change.Coming)
        {
            db.DigestChoices.Add(new DigestChoice { RunId = runId, ReasonId = c.ReasonId, RecordId = c.RecordId, Side = c.Side, Place = c.Place, Replacement = true });
        }

        await db.SaveChangesAsync(ct);
    }

    private static ChosenReason ToChosen(DigestChoice c) => new(c.ReasonId, c.RecordId, c.Side, c.Place, c.Replacement, c.LeftAt is not null);

    /// <summary>
    /// The snapshot of a prepared summary at <paramref name="now"/>, in the caller's transaction (design 8.19.2): an argument
    /// hidden since is replaced; a bill held or no longer open drops out; every number of the rest is read now, and the chosen
    /// arguments are written in their places with their "Man svarbu" now. Returns how many bills stay.
    /// </summary>
    private async Task<int> FreezeAsync(NotificationsDb db, DigestRun run, IReadOnlyList<DigestCandidate> candidates, DateTimeOffset now, CancellationToken ct)
    {
        var items = await db.DigestItems.Where(i => i.RunId == run.Id).OrderBy(i => i.Position).ToListAsync(ct);
        var pool = await source.ReasonPoolAsync(items.Select(i => i.RecordId).ToList(), run.CreatedAt, ct);
        await ApplyReplacementsAsync(db, run.Id, pool, now, ct);
        var choices = await db.DigestChoices.AsNoTracking().Where(c => c.RunId == run.Id && c.LeftAt == null).ToListAsync(ct);
        var byId = pool.ToDictionary(r => r.Id);
        var byRecord = candidates.ToDictionary(c => c.RecordId);
        var kept = 0;
        foreach (var item in items)
        {
            if (!byRecord.TryGetValue(item.RecordId, out var c) || c.Held)
            {
                db.DigestItems.Remove(item);
                continue;
            }

            kept++;
            item.BillNumber = c.BillNumber;
            item.BillTitle = c.BillTitle;
            item.Stage = c.Planned?.Stage ?? item.Stage;
            item.PlannedDate = c.Planned?.Date ?? item.PlannedDate;
            item.ForCount = c.For;
            item.ForOlderThan30Days = c.ForOlderThan30Days;
            item.AgainstCount = c.Against;
            item.AgainstOlderThan30Days = c.AgainstOlderThan30Days;
            item.SurgeSharePercent = c.SurgeSharePercent;
            item.SurgeWindowHours = c.SurgeWindowHours;

            // In the order chosen, not sorted again by marks (owner, 2026-11-17); the count beside each is read now.
            item.Reasons = JsonSerializer.Serialize(
                Sides().SelectMany(side => choices.Where(x => x.RecordId == item.RecordId && x.Side == side && byId.ContainsKey(x.ReasonId))
                        .OrderBy(x => x.Place)
                        .Select(x => new DigestReason(side, byId[x.ReasonId].Text, byId[x.ReasonId].Endorsements)))
                    .ToList(),
                JsonSerializerOptions.Web);
        }

        await db.SaveChangesAsync(ct);
        return kept;
    }

    /// <summary>
    /// Lets a prepared summary go at its time (design 8.19.2), under the summaries' lock: the snapshot is made now
    /// (<see cref="FreezeAsync"/>); with no bill left nothing goes (<c>dropped</c>); while sending is off a dry run, otherwise each
    /// MP's delivery as things are now, the party numbers read now.
    /// </summary>
    private async Task GoAsync(Guid runId, DateTimeOffset now, CancellationToken ct, StaffActor? approvedBy = null)
    {
        var candidates = await source.CandidatesAsync(ct);
        var recipients = (await mps.SittingMpsAsync(ct)).Where(m => m.HasOfficialAddress).ToList();
        var maxPerWeek = await settings.GetAsync(SettingKeys.DigestMaxPerWeek, ct);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(LockSql, ct);
        var run = await db.DigestRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is not { Status: RunStatuses.Prepared })
        {
            return;
        }

        run.SentAt = now.ToUniversalTime();
        if (await FreezeAsync(db, run, candidates, now, ct) == 0)
        {
            run.Status = RunStatuses.Dropped;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(
                new AuditEntry("digest.dropped", null, null, "digest:" + run.Id, JsonSerializer.SerializeToElement(new { kind = run.Kind }, JsonSerializerOptions.Web)),
                transaction.GetDbTransaction(),
                ct);
            await transaction.CommitAsync(ct);
            return;
        }

        var state = await StateAsync(db, ct);
        var counts = await CeilingCountsAsync(db, DigestRules.WeekStart(run.RunDate), state.On, exclude: run.Id, ct);
        var recordIds = await db.DigestItems.Where(i => i.RunId == run.Id).Select(i => i.RecordId).ToListAsync(ct);
        var numbers = await db.DigestItems.AsNoTracking().Where(i => i.RunId == run.Id).Select(i => new { i.RecordId, i.BillNumber }).ToListAsync(ct);
        var party = await source.PartyNumbersAsync(recordIds, ct);
        string PartyJson(MpRecipient mp) => JsonSerializer.Serialize(
            mp.Party is null
                ? []
                : numbers.Select(i => party.GetValueOrDefault(i.RecordId)?.FirstOrDefault(p => p.Party == mp.Party) is { } n
                        ? new DeliveryParty(i.BillNumber, n.Party, n.For, n.Against)
                        : null)
                    .OfType<DeliveryParty>()
                    .ToList(),
            JsonSerializerOptions.Web);

        run.Status = SendingRules.GoneStatus(state.Mode);
        if (run.Status == RunStatuses.DryRun)
        {
            // Sending off: every MP within the ceiling would have had it.
            foreach (var mp in recipients)
            {
                var within = DigestRules.WithinCeiling(counts.GetValueOrDefault(mp.Slug), maxPerWeek, run.Kind == DigestRules.Urgent);
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
            var plan = await PlanAsync(db, run.Kind, recipients, counts, maxPerWeek, ct);
            run.MpCount = plan.Count(p => p.Official is DeliveryStatuses.Queued or DeliveryStatuses.Introduced);
            await db.SaveChangesAsync(ct);
            var info = (await InfoAsync(db, [run], ct))[0];
            var bySlug = recipients.ToDictionary(m => m.Slug);
            await LetGoAsync(db, run, info, plan, slug => PartyJson(bySlug[slug]), now, ct);
        }

        await db.SaveChangesAsync(ct);
        if (run.Status == RunStatuses.Sent)
        {
            await audit.WriteAsync(
                new AuditEntry("digest.sent", approvedBy?.UserId, approvedBy?.Role, "digest:" + run.Id,
                    JsonSerializer.SerializeToElement(new { kind = run.Kind, mps = run.SentCount }, JsonSerializerOptions.Web), approvedBy?.IpHash),
                transaction.GetDbTransaction(),
                ct);
        }

        await transaction.CommitAsync(ct);
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

        await OfficeCopiesAsync(db, run, info, plan, partyJson, responses, ct);
        run.Status = RunStatuses.Sent;
        run.SentCount = plan.Count(p => p.Official == DeliveryStatuses.Queued);
        await source.MarkSummarisedAsync(
            await db.DigestItems.Where(i => i.RunId == run.Id).Select(i => i.RecordId).ToListAsync(ct), now, db.Database.CurrentTransaction!.GetDbTransaction(), ct);
    }

    /// <summary>
    /// The copies for the MPs' offices (design 8.19.3, owner 2026-11-17), in the caller's transaction: every office member linked
    /// now to an MP the summary went out for gets a copy at their own address, whatever the MP's own choice, by the member's own
    /// choice, weekly ceiling and the suppression list; one delivery row per member (never the address), the member's own
    /// one-click stop. Not counted as MPs anywhere public.
    /// </summary>
    private async Task OfficeCopiesAsync(
        NotificationsDb db, DigestRun run, DigestRunInfo info, IReadOnlyList<PlannedDelivery> plan, Func<string, string> partyJson,
        IReadOnlyList<MpStance> responses, CancellationToken ct)
    {
        var byPerson = plan.GroupBy(p => p.Mp.SeimasPersonId).ToDictionary(g => g.Key, g => g.First());
        var members = await links.OfficeMembersAsync(byPerson.Keys.ToList(), ct);
        if (members.Count == 0)
        {
            return;
        }

        var maxPerWeek = await settings.GetAsync(SettingKeys.DigestMaxPerWeek, ct);
        var users = members.Select(m => m.UserId).Distinct().ToList();
        var choices = await db.OfficeCopies.Where(c => users.Contains(c.UserId)).ToListAsync(ct);
        var suppressed = await sender.SuppressedAsync(db, members.Select(m => m.Email), ct);
        var counts = await OfficeCeilingCountsAsync(db, DigestRules.WeekStart(run.RunDate), run.Id, ct);
        foreach (var member in members)
        {
            var p = byPerson[member.SeimasPersonId];
            var choice = choices.FirstOrDefault(c => c.UserId == member.UserId && c.SeimasPersonId == member.SeimasPersonId);
            var status = SendingRules.OfficeStatus(
                sender.IsSuppressed(suppressed, member.Email), choice?.Frequency ?? Frequencies.Default, run.Kind, counts.GetValueOrDefault(member.UserId), maxPerWeek);
            var json = partyJson(p.Mp.Slug);
            db.DigestDeliveries.Add(new DigestDelivery
            {
                RunId = run.Id, MpSlug = p.Mp.Slug, SeimasPersonId = p.Mp.SeimasPersonId, Recipient = Recipients.Office, OfficeUserId = member.UserId,
                Status = status, PartyNumbers = json,
            });
            if (status != DeliveryStatuses.Queued)
            {
                continue;
            }

            if (choice is null)
            {
                choice = new OfficeCopy { UserId = member.UserId, SeimasPersonId = member.SeimasPersonId, ChangedAt = clock.GetUtcNow() };
                db.OfficeCopies.Add(choice);
                choices.Add(choice);
            }

            var token = keys.DerivedToken(member.SeimasPersonId, OfficeStopKind(member.UserId), choice.Generation);
            choice.StopTokenHash = MailKeys.TokenHash(token);
            var party = JsonSerializer.Deserialize<List<DeliveryParty>>(json, JsonSerializerOptions.Web) ?? [];
            await sender.EnqueueAsync(
                db,
                MpMailKinds.Digest,
                Email(info, p.Mp, member.Email, Recipients.Office, bill => party.FirstOrDefault(x => x.BillNumber == bill), responses,
                    null, MpMailPaths.OneClick(token), MpMailPaths.StopCopies(token), new DeliveryRef(run.Id, p.Mp.Slug, Recipients.Office, member.UserId)),
                ct);
        }
    }

    /// <summary>The kind an office member's stop token is derived under: per account, so each member has their own.</summary>
    internal static string OfficeStopKind(Guid userId) => "office-stop:" + userId.ToString("N");

    /// <summary>How many copies each office member has this week (design 8.19.3: the ceiling per recipient).</summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> OfficeCeilingCountsAsync(NotificationsDb db, DateOnly weekStart, Guid exclude, CancellationToken ct)
    {
        var runStatuses = SendingRules.CountingStatuses(sendingOn: true).ToList();
        var deliveryStatuses = SendingRules.CeilingDeliveryStatuses.ToList();
        var weekEnd = weekStart.AddDays(7);
        return await (from d in db.DigestDeliveries
                      join r in db.DigestRuns on d.RunId equals r.Id
                      where d.Recipient == Recipients.Office && runStatuses.Contains(r.Status) && deliveryStatuses.Contains(d.Status)
                            && r.RunDate >= weekStart && r.RunDate < weekEnd && r.Id != exclude
                      group d by d.OfficeUserId into g
                      select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
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
            delivery,
            info.Id);

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

    /// <summary>Records that week's Monday summary (a counting one, prepared or gone) already holds with the same sitting date.</summary>
    private static async Task<IReadOnlySet<Guid>> CoveredByMondayAsync(NotificationsDb db, DateOnly weekStart, DateOnly sitting, bool sendingOn, CancellationToken ct)
    {
        var statuses = SendingRules.CountingStatuses(sendingOn).ToList();
        return (await (from i in db.DigestItems
                       join r in db.DigestRuns on i.RunId equals r.Id
                       where r.Kind == DigestRules.Weekly && r.RunDate == weekStart && i.PlannedDate == sitting && statuses.Contains(r.Status)
                       select i.RecordId).ToListAsync(ct)).ToHashSet();
    }

    private static List<DigestReason> Snapshot(DigestItem i) =>
        JsonSerializer.Deserialize<List<DigestReason>>(i.Reasons, JsonSerializerOptions.Web) ?? [];

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
                    Snapshot(i))).ToList(),
                r.SentCount,
                r.SentAt,
                r.DueAt,
                r.Status == RunStatuses.Cancelled ? r.Statement : null,
                r.ApprovedAt))
            .ToList();
    }
}

/// <summary>The MP's party numbers for one bill in a delivery (groups of at least the party group minimum only).</summary>
internal sealed record DeliveryParty(string BillNumber, string Party, int? For, int? Against);
