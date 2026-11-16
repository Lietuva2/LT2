using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// Moderation after the fact (design 8.12.3): nothing waits for approval before it is shown, except arguments that
/// pre-screening or a report flags. Every action is recorded in <c>appeals.moderation_action</c> and in the audit log, in
/// the same transaction (<see cref="ModerationRecorder"/>); an argument's author is told by email through the outbox, with
/// the statement of reasons. Nothing here reads or writes a position.
/// </summary>
internal sealed class ModerationService(
    IDbContextFactory<AppealsDb> contexts,
    AppealReads reads,
    BillRecords records,
    ModerationRecorder recorder,
    IParticipants participants,
    IAuditLog audit,
    IOutbox outbox,
    IPlannedAgenda agenda,
    IBillCatalogue bills,
    IAiAssistant assistant,
    TimeProvider clock) : IAppealsModeration
{
    private const int QueueLimit = 100;

    public async Task<ModerationItems> QueueAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var openReports = await db.Reports.AsNoTracking().Where(r => r.Status == "open").OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var reportedIds = openReports.Select(r => r.TargetId).ToHashSet();

        // Open records come into the queue when reported, flagged about their date, surging (design 6.6), held, or under
        // their threshold on the agenda with at least one participant (design 8.12.2). A closed record comes in only when
        // its description is reported (design 6.9, "Reports after the final vote").
        var records = await db.BillRecords.AsNoTracking()
            .Where(a => a.Status == BillRecordStatuses.Open || (a.Status == BillRecordStatuses.Closed && reportedIds.Contains(a.Id)))
            .ToListAsync(cancellationToken);
        var openRecords = records.Where(a => a.Status == BillRecordStatuses.Open).ToList();
        var surges = await reads.SurgesAsync(db, openRecords.Select(a => a.Id).ToList(), cancellationToken);
        var openFlags = await db.Flags.AsNoTracking().Where(f => f.Status == "open").OrderBy(f => f.CreatedAt).ToListAsync(cancellationToken);
        var planned = await agenda.ForBillsAsync(openRecords.Select(a => a.BillNumber).ToList(), cancellationToken);
        var counts = await reads.ShownCountsAsync(db, records, cancellationToken);
        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        var thresholds = records.ToDictionary(a => a.Id, a => AppealReads.Threshold(a, counts[a.Id], threshold));
        var underThresholdOnAgenda = openRecords
            .Where(a => RecordRules.UnderThresholdOnAgenda(counts[a.Id].Total, planned.ContainsKey(a.BillNumber), thresholds[a.Id].Reached))
            .Select(a => a.Id)
            .ToHashSet();
        var listed = records
            .Where(a => reportedIds.Contains(a.Id) || surges.ContainsKey(a.Id) || openFlags.Any(f => f.RecordId == a.Id)
                || underThresholdOnAgenda.Contains(a.Id) || a.Sending == "held")
            .OrderBy(a => a.ChangedAt)
            .Take(QueueLimit)
            .ToList();

        // Arguments of open records that wait, are reported or not yet seen; of closed records only reported ones (6.9).
        var reasons = await db.Reasons.AsNoTracking()
            .Where(r => (db.BillRecords.Any(a => a.Id == r.RecordId && a.Status == BillRecordStatuses.Open)
                    && (r.Status == ReasonStatuses.Pending
                        || (r.Status == ReasonStatuses.Published && (r.SeenAt == null || reportedIds.Contains(r.Id)))))
                || (db.BillRecords.Any(a => a.Id == r.RecordId && a.Status == BillRecordStatuses.Closed)
                    && r.Status == ReasonStatuses.Published && reportedIds.Contains(r.Id)))
            .OrderBy(r => r.CreatedAt)
            .Take(QueueLimit)
            .ToListAsync(cancellationToken);
        var billFacts = await reads.BillsAsync(listed.Select(a => a.BillNumber).Distinct().ToList(), cancellationToken);
        BillFacts Bill(string number) => billFacts.GetValueOrDefault(number) ?? new BillFacts(number, number);
        IReadOnlyList<QueueReport> ReportsOn(Guid id) => openReports.Where(r => r.TargetId == id).Select(r => new QueueReport(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList();

        var queueRecords = listed.Select(a => new QueueRecord(
            a.Id,
            Bill(a.BillNumber),
            a.Description,
            a.Description.Length == 0 ? null : a.DescriptionSource,
            thresholds[a.Id].Value,
            ReportsOn(a.Id),
            surges.GetValueOrDefault(a.Id),
            planned.GetValueOrDefault(a.BillNumber),
            openFlags.Where(f => f.RecordId == a.Id).Select(f => new QueueFlag(f.Id, f.Kind, f.Link, f.Note, f.CreatedAt)).ToList(),
            underThresholdOnAgenda.Contains(a.Id),
            a.Sending == "held",
            counts[a.Id].Total,
            a.Description.Length == 0 ? null : a.DescriptionUrl,
            a.Status)).ToList();

        return new ModerationItems(queueRecords, await QueueReasonsAsync(db, reasons, openReports, cancellationToken));
    }

    /// <summary>
    /// Arguments as a moderator sees them, in the queue and on the bill's page alike (design 8.3, 8.12.3): text, author's
    /// name, status, whether seen, pre-screening flags, open reports, and the published arguments on the same side they could
    /// be merged into. Never a side of a person.
    /// </summary>
    private async Task<IReadOnlyList<QueueReason>> QueueReasonsAsync(AppealsDb db, IReadOnlyList<Reason> reasons, IReadOnlyList<Report> openReports, CancellationToken ct)
    {
        var recordIds = reasons.Select(r => r.RecordId).Distinct().ToList();
        var recordOf = await db.BillRecords.AsNoTracking().Where(a => recordIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var candidates = await db.Reasons.AsNoTracking()
            .Where(r => recordIds.Contains(r.RecordId) && r.Status == ReasonStatuses.Published)
            .ToListAsync(ct);
        var allReasons = reasons.Concat(candidates).ToList();
        var names = await reads.NamesAsync(allReasons.Select(r => r.AuthorUserId), ct);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, allReasons.Select(r => r.Id).Distinct().ToList(), ct);
        var billFacts = await reads.BillsAsync(recordOf.Values.Select(a => a.BillNumber).Distinct().ToList(), ct);
        return reasons.Select(r => new QueueReason(
            r.Id,
            r.RecordId,
            billFacts.GetValueOrDefault(recordOf[r.RecordId].BillNumber) ?? new BillFacts(recordOf[r.RecordId].BillNumber, recordOf[r.RecordId].BillNumber),
            r.Side,
            r.Text,
            names.GetValueOrDefault(r.AuthorUserId),
            r.Status,
            r.SeenAt is not null,
            r.ScreeningFlags,
            openReports.Where(o => o.TargetId == r.Id).Select(o => new QueueReport(o.Id, o.Ground, o.Text, o.CreatedAt)).ToList(),
            // Merging is refused on a closed record (design 6.9), so nothing is offered to merge into there.
            recordOf[r.RecordId].Status != BillRecordStatuses.Open
                ? []
                : ReasonSimilarity.MostSimilar(
                        r.Text,
                        candidates.Where(c => c.RecordId == r.RecordId && c.Side == r.Side && c.Id != r.Id),
                        c => c.Text)
                    .Select(c => AppealReads.ToInfo(c, names, endorsements))
                    .ToList(),
            r.CreatedAt,
            recordOf[r.RecordId].Status)).ToList();
    }

    public Task<Result<Unit>> KeepRecordAsync(Guid recordId, string? statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: false) ?? OnRecordAsync(recordId, moderator, "keep", statement, answersReports: true, async (db, record, now) =>
        {
            // On an open record, or on a closed one to answer a report (design 6.9).
            if (RecordRules.CheckRecordAction(record.Status, "keep") is { } refused)
            {
                return RuleErrors.Of(refused);
            }

            // Kept as it is: its open date flags close (the reports close with the record of the action).
            await CloseFlagsAsync(db, record.Id, now, cancellationToken);
            return Result.Ok();
        }, cancellationToken);

    public Task<Result<Unit>> HideRecordAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnRecordAsync(recordId, moderator, "hide", statement, answersReports: true, (_, record, _) =>
        {
            if (record.Status is not (BillRecordStatuses.Open or BillRecordStatuses.Closed))
            {
                return Task.FromResult<Result<Unit>>(Error.Conflict("moderation.wrong-state"));
            }

            // Taking part on the bill closes; sides, arguments and marks are kept unseen (design 8.12.3).
            record.Status = BillRecordStatuses.Rejected;
            return Task.FromResult(Result.Ok());
        }, cancellationToken);

    public async Task<HiddenItems> HiddenAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeals = await db.BillRecords.AsNoTracking().Where(a => a.Status == BillRecordStatuses.Rejected).ToListAsync(cancellationToken);
        var reasons = await db.Reasons.AsNoTracking().Where(r => r.Status == ReasonStatuses.Hidden).ToListAsync(cancellationToken);
        var targets = appeals.Select(a => a.Id).Concat(reasons.Select(r => r.Id)).ToList();
        var hides = (await db.ModerationActions.AsNoTracking()
                .Where(m => targets.Contains(m.TargetId) && m.Action == "hide")
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.TargetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());
        var reasonRecords = reasons.Select(r => r.RecordId).Distinct().ToList();
        var recordOf = await db.BillRecords.AsNoTracking().Where(a => reasonRecords.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);

        var names = await reads.NamesAsync(reasons.Select(r => r.AuthorUserId).Concat(hides.Values.Select(m => m.ModeratorUserId)), cancellationToken);
        var billFacts = await reads.BillsAsync(appeals.Select(a => a.BillNumber).Concat(recordOf.Values.Select(a => a.BillNumber)).Distinct().ToList(), cancellationToken);
        BillFacts Bill(string number) => billFacts.GetValueOrDefault(number) ?? new BillFacts(number, number);
        ModerationAction? Hide(Guid id) => hides.GetValueOrDefault(id);

        var hiddenRecords = appeals
            .Select(a =>
            {
                var hide = Hide(a.Id);
                return new HiddenRecord(
                    a.Id, Bill(a.BillNumber), a.Description, hide?.CreatedAt, hide is null ? null : names.GetValueOrDefault(hide.ModeratorUserId), hide?.Statement,
                    RecordRules.RestoredStatus(a.ClosedAt));
            })
            .OrderByDescending(a => a.HiddenAt ?? DateTimeOffset.MinValue)
            .Take(QueueLimit * 2)
            .ToList();
        var hiddenReasons = reasons
            .Select(r =>
            {
                var hide = Hide(r.Id);
                var record = recordOf[r.RecordId];
                return new HiddenReason(
                    r.Id, Bill(record.BillNumber), r.Side, r.Text, names.GetValueOrDefault(r.AuthorUserId), hide?.CreatedAt,
                    hide is null ? null : names.GetValueOrDefault(hide.ModeratorUserId), hide?.Statement, record.Status is BillRecordStatuses.Open or BillRecordStatuses.Closed);
            })
            .OrderByDescending(r => r.HiddenAt ?? DateTimeOffset.MinValue)
            .Take(QueueLimit * 2)
            .ToList();
        return new HiddenItems(hiddenRecords, hiddenReasons);
    }

    public Task<Result<Unit>> RestoreRecordAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnRecordAsync(recordId, moderator, "restore", statement, answersReports: true, (_, record, _) =>
        {
            if (RecordRules.CheckRestore(record.Status) is { } refused)
            {
                return Task.FromResult<Result<Unit>>(RuleErrors.Of(refused));
            }

            // Sides, arguments and marks were never touched by the hide, so they come back as they were.
            record.Status = RecordRules.RestoredStatus(record.ClosedAt);
            return Task.FromResult(Result.Ok());
        }, cancellationToken);

    public Task<Result<Unit>> RestoreReasonAsync(Guid reasonId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnReasonAsync(reasonId, moderator, "restore", statement, (_, reason, now) =>
        {
            if (reason.Status != ReasonStatuses.Hidden)
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            // Its "Man svarbu" marks were deleted by the hide and do not come back (design 8.3).
            reason.Status = ReasonStatuses.Published;
            reason.SeenAt = now;
            return Task.FromResult<Result<string?>>(ModerationNotices.ReasonRestored);
        }, cancellationToken);

    public async Task<Result<Unit>> SetThresholdAsync(Guid recordId, int threshold, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        if (ThresholdRules.CheckRequested(threshold, await reads.MinimumAsync(cancellationToken)) is { } below)
        {
            return RuleErrors.Of(below);
        }

        // A threshold does not answer a report on the description (design 6.8).
        return await (Refused(statement, required: true) ?? OnRecordAsync(recordId, moderator, "threshold", statement, answersReports: false, (db, record, now) =>
        {
            if (record.Status is not BillRecordStatuses.Open)
            {
                return Task.FromResult<Result<Unit>>(Error.Conflict("moderation.wrong-state"));
            }

            db.Revisions.Add(AppealsService.Revise("record", record.Id, "threshold", record.Threshold is { } old ? AppealsService.Number(old) : null, AppealsService.Number(threshold), moderator.UserId, now));
            record.Threshold = threshold;
            record.ThresholdSetBy = ThresholdSetters.Moderator;
            record.ThresholdNote = statement.Trim();
            return Task.FromResult(Result.Ok());
        }, cancellationToken, details: new { threshold }));
    }

    public async Task<Result<Unit>> WriteDescriptionAsync(string billNumber, ModeratorDescription request, StaffActor moderator, CancellationToken cancellationToken)
    {
        if ((AppealRules.CheckDescription(request.Description) ?? RecordRules.CheckDescriptionUrl(request.DescriptionUrl) ?? StatementRules.Check(request.Statement, required: true)) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        var text = AppealRules.NormaliseText(request.Description);
        var address = RecordRules.NormaliseDescriptionUrl(request.DescriptionUrl);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // On any active bill (design 8.12.3): the bill's record is created if it has none. Under the bill's lock, so the
        // summary job or another moderator cannot write over it meanwhile (design 6.8).
        var ensured = await records.EnsureAsync(db, billNumber, cancellationToken, forWrite: true);
        if (!ensured.IsSuccess)
        {
            return ensured.Error!;
        }

        var record = ensured.Value;
        if (RecordRules.CheckDescriptionWrite(record.Status, record.Description, text, request.Statement, record.DescriptionUrl, address) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        var now = clock.GetUtcNow();
        var lastDraft = await db.AiDraftRequests.AsNoTracking()
            .Where(d => d.ModeratorUserId == moderator.UserId && d.BillNumber == record.BillNumber)
            .MaxAsync(d => (DateTimeOffset?)d.RequestedAt, cancellationToken);
        if (AiDraftRules.CheckFromDraft(request.FromDraft, lastDraft, now) is { } noDraft)
        {
            return RuleErrors.Of(noDraft);
        }

        // Shown at once and in the public history; the same moderator writes and saves it, no second one (decided 2026-11-14).
        // A change of the address alone leaves the text, its source (the label), its history and its day as they are.
        if (record.Description != text)
        {
            db.Revisions.Add(AppealsService.Revise("record", record.Id, "description", record.Description.Length == 0 ? null : record.Description, text, moderator.UserId, now));
            record.Description = text;
            record.DescriptionAt = now;
            record.DescriptionSource = RecordRules.SourceOfModeratorText(request.FromDraft, record.DescriptionSource);
        }

        // The form's address decides (decided by the owner on 2026-11-14): it is filled in advance with the record's, so
        // leaving it alone keeps it, and an empty field clears it. Not personal data, so it may be in the audit details.
        record.DescriptionUrl = address;
        record.ChangedAt = now;
        await recorder.RecordAsync(
            db, transaction, "record", record.Id, "describe", request.Statement.Trim(), moderator, now, record.BillNumber,
            new { descriptionSource = record.DescriptionSource, descriptionUrl = address }, answersReports: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ConfirmDescriptionAsync(string billNumber, string? note, StaffActor moderator, CancellationToken cancellationToken)
    {
        if (StatementRules.Check(note, required: false) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken) is not { } record)
        {
            return await records.ActiveBillAsync(billNumber, cancellationToken) is { IsSuccess: false } unknown ? unknown.Error! : Error.Conflict("moderation.wrong-state");
        }

        // The text stays as it is; a person has now read it, so it is "Sukurta DI · taisė žmonės" for its whole life (design 8.13).
        return await OnRecordAsync(record.Id, moderator, "confirm", note, answersReports: true, (_, record, _) =>
        {
            if (RecordRules.CheckConfirm(record.Status, record.Description.Length == 0 ? null : record.DescriptionSource) is { } refused)
            {
                return Task.FromResult<Result<Unit>>(RuleErrors.Of(refused));
            }

            record.DescriptionSource = DescriptionSources.AiDraft;
            return Task.FromResult(Result.Ok());
        }, cancellationToken);
    }

    public async Task<Result<Unit>> RemoveDescriptionAsync(string billNumber, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        if (StatementRules.Check(statement, required: true) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken) is not { } record)
        {
            return await records.ActiveBillAsync(billNumber, cancellationToken) is { IsSuccess: false } unknown ? unknown.Error! : Error.Conflict("moderation.wrong-state");
        }

        // The removal is in the public history (old text, no new one) and by a person, so the summary job never writes the
        // description of this bill again (design 8.13).
        string? removedSource = null;
        return await OnRecordAsync(record.Id, moderator, "undescribe", statement, answersReports: true, (inner, record, now) =>
        {
            if (RecordRules.CheckRemove(record.Status, record.Description, statement) is { } refused)
            {
                return Task.FromResult<Result<Unit>>(RuleErrors.Of(refused));
            }

            removedSource = record.DescriptionSource;
            inner.Revisions.Add(AppealsService.Revise("record", record.Id, "description", record.Description, null, moderator.UserId, now));
            record.Description = "";
            record.DescriptionSource = null;
            record.DescriptionUrl = null;
            record.DescriptionAt = null;
            return Task.FromResult(Result.Ok());
        }, () => new { removedSource }, cancellationToken);
    }

    public async Task<Result<ModerationBillState>> BillStateAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            var bill = await records.ActiveBillAsync(billNumber, cancellationToken);
            if (!bill.IsSuccess)
            {
                return bill.Error!;
            }

            var plannedOnly = await agenda.ForBillsAsync([bill.Value.Number], cancellationToken);
            return new ModerationBillState(
                null, bill.Value.Number, BillRecordStatuses.Open, "", null, ThresholdRules.Effective(null, threshold.Default, threshold.Minimum), ThresholdSetters.Default,
                false, plannedOnly.GetValueOrDefault(bill.Value.Number), false, [], null, []);
        }

        var planned = await agenda.ForBillsAsync([record.BillNumber], cancellationToken);

        // The open reports about the description (a report on the record is one, 8.12.3), with what the queue shows for each:
        // the ground, the reporter's words and when; never who (decided 2026-11-14).
        var descriptionReports = await db.Reports.AsNoTracking()
            .Where(r => r.Status == "open" && r.TargetId == record.Id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new QueueReport(r.Id, r.Ground, r.Text, r.CreatedAt))
            .ToListAsync(cancellationToken);
        var hasOpenItems = descriptionReports.Count > 0
            || await db.Flags.AsNoTracking().AnyAsync(f => f.RecordId == record.Id && f.Status == "open", cancellationToken);

        // The bill's waiting and published arguments, for the controls on each and the panel's list (decided 2026-11-14).
        var reasons = await db.Reasons.AsNoTracking()
            .Where(r => r.RecordId == record.Id && (r.Status == ReasonStatuses.Pending || r.Status == ReasonStatuses.Published))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        var reasonIds = reasons.Select(r => r.Id).ToList();
        var reports = await db.Reports.AsNoTracking().Where(r => r.Status == "open" && reasonIds.Contains(r.TargetId)).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var described = record.Description.Length > 0;
        return new ModerationBillState(
            record.Id, record.BillNumber, record.Status, record.Description, described ? record.DescriptionSource : null,
            ThresholdRules.Effective(record.Threshold, threshold.Default, threshold.Minimum), record.ThresholdSetBy, record.Sending == "held",
            planned.GetValueOrDefault(record.BillNumber), hasOpenItems, await QueueReasonsAsync(db, reasons, reports, cancellationToken),
            described ? record.DescriptionUrl : null, descriptionReports);
    }

    public async Task<Result<Guid>> EnsureRecordAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var record = await records.EnsureAsync(db, billNumber, cancellationToken);
        if (!record.IsSuccess)
        {
            return record.Error!;
        }

        await transaction.CommitAsync(cancellationToken);
        return record.Value.Id;
    }

    public Task<Result<Unit>> PublishReasonAsync(Guid reasonId, string? statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: false) ?? OnReasonAsync(reasonId, moderator, "publish", statement, (_, reason, now) =>
        {
            if (reason.Status is not (ReasonStatuses.Pending or ReasonStatuses.Published))
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            var wasPending = reason.Status == ReasonStatuses.Pending;
            reason.Status = ReasonStatuses.Published;
            reason.SeenAt = now;
            return Task.FromResult<Result<string?>>(wasPending ? ModerationNotices.ReasonPublished : null);
        }, cancellationToken);

    public Task<Result<Unit>> HideReasonAsync(Guid reasonId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnReasonAsync(reasonId, moderator, "hide", statement, async (db, reason, now) =>
        {
            if (reason.Status is not (ReasonStatuses.Pending or ReasonStatuses.Published))
            {
                return Error.Conflict("moderation.wrong-state");
            }

            reason.Status = ReasonStatuses.Hidden;
            reason.SeenAt = now;

            // A hidden argument cannot be endorsed; the marks on it go, so they no longer count towards anyone's limit.
            await db.Endorsements.Where(e => e.ReasonId == reason.Id).ExecuteDeleteAsync(cancellationToken);
            return ModerationNotices.ReasonHidden;
        }, cancellationToken);

    public Task<Result<Unit>> MergeReasonAsync(Guid reasonId, Guid intoReasonId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnReasonAsync(reasonId, moderator, "merge", statement, async (db, reason, now) =>
        {
            if (reasonId == intoReasonId || reason.Status is not (ReasonStatuses.Pending or ReasonStatuses.Published))
            {
                return Error.Conflict("moderation.wrong-state");
            }

            var into = await db.Reasons.FirstOrDefaultAsync(r => r.Id == intoReasonId, cancellationToken);
            if (into is null || into.RecordId != reason.RecordId || into.Side != reason.Side || into.Status != ReasonStatuses.Published)
            {
                return Error.Conflict("merge.target-invalid");
            }

            // Endorsements move with it (design 8.3); someone who marked both keeps one mark.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO appeals.endorsement (reason_id, user_id, record_id, created_at)
                SELECT {1}, e.user_id, e.record_id, e.created_at FROM appeals.endorsement e WHERE e.reason_id = {0}
                ON CONFLICT (reason_id, user_id) DO NOTHING
                """,
                [reasonId, intoReasonId],
                cancellationToken);
            await db.Endorsements.Where(e => e.ReasonId == reasonId).ExecuteDeleteAsync(cancellationToken);

            // Design 8.23.7: its comments remarked on a text no longer shown, so they go (not moved under the other).
            await CommentCascade.DeleteForTargetsAsync(db, CommentKinds.Reason, [reasonId], now, cancellationToken);
            reason.Status = ReasonStatuses.Merged;
            reason.MergedIntoId = intoReasonId;
            reason.SeenAt = now;
            return ModerationNotices.ReasonMerged;
        }, cancellationToken, details: new { into = intoReasonId });

    public Task<Result<Unit>> SetPlannedAsync(Guid recordId, DateOnly date, string? stage, string statement, StaffActor moderator, CancellationToken cancellationToken)
    {
        if (stage is not (null or "presentation" or "consideration" or "adoption"))
        {
            return Task.FromResult<Result<Unit>>(Error.Validation("planned.stage", "stage"));
        }

        // A planned date answers the date flags, not a report on the description (design 6.8).
        return Refused(statement, required: true) ?? OnRecordAsync(recordId, moderator, "planned", statement, answersReports: false, async (db, record, now) =>
        {
            if (record.Status != BillRecordStatuses.Open)
            {
                return Error.Conflict("moderation.wrong-state");
            }

            await agenda.CorrectAsync(record.BillNumber, date, stage, moderator.UserId, statement.Trim(), db.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
            await CloseFlagsAsync(db, record.Id, now, cancellationToken);
            return Result.Ok();
        }, cancellationToken, details: new { date, stage });
    }

    public Task<Result<Unit>> HoldAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        SendingAsync(recordId, "held", "hold", statement, moderator, cancellationToken);

    public Task<Result<Unit>> ReleaseAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken) =>
        SendingAsync(recordId, "automatic", "release", statement, moderator, cancellationToken);

    /// <summary>Holding and releasing the sending do not answer a report on the description (design 6.8).</summary>
    private Task<Result<Unit>> SendingAsync(Guid recordId, string sending, string action, string statement, StaffActor moderator, CancellationToken ct) =>
        Refused(statement, required: true) ?? OnRecordAsync(recordId, moderator, action, statement, answersReports: false, (_, record, _) =>
        {
            if (record.Status != BillRecordStatuses.Open || record.Sending == sending)
            {
                return Task.FromResult<Result<Unit>>(Error.Conflict("moderation.wrong-state"));
            }

            record.Sending = sending;
            return Task.FromResult(Result.Ok());
        }, ct);

    public bool DraftAvailable => assistant.IsConfigured;

    public async Task<Result<string>> DraftDescriptionAsync(string billNumber, string sourceText, StaffActor moderator, CancellationToken cancellationToken)
    {
        if (!assistant.IsConfigured)
        {
            return Error.Conflict("draft.not-configured");
        }

        var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken);
        if (bill is null)
        {
            return Error.NotFound("bill.not-found");
        }

        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Serialised per moderator so the hourly limit holds under parallel requests.
            await AdvisoryLocks.AiDraftsAsync(db, moderator.UserId, cancellationToken);
            var lastHour = await db.AiDraftRequests.CountAsync(d => d.ModeratorUserId == moderator.UserId && d.RequestedAt > now.AddHours(-1), cancellationToken);
            if (AiDraftRules.CheckRequest(sourceText, lastHour) is { } refused)
            {
                return RuleErrors.Of(refused);
            }

            // Audited: who, which bill, when; never the source text or the draft (design 8.11.4).
            db.AiDraftRequests.Add(new AiDraftRequest { Id = Ids.New(clock), ModeratorUserId = moderator.UserId, BillNumber = bill.Number, RequestedAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(
                new AuditEntry("moderation.draft.requested", moderator.UserId, moderator.Role, "bill:" + bill.Number, null, moderator.IpHash),
                transaction.GetDbTransaction(),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await assistant.DraftDescriptionAsync(new DraftSource(bill.Number, bill.Title, sourceText.Trim()), cancellationToken);
    }

    /// <summary>The bill's page, where an argument's author is pointed to (design 8.12.1).</summary>
    private static string BillPath(string billNumber) => $"/seimas/{Uri.EscapeDataString(billNumber)}";

    private static Task<int> CloseFlagsAsync(AppealsDb db, Guid recordId, DateTimeOffset now, CancellationToken ct) =>
        db.Flags.Where(f => f.RecordId == recordId && f.Status == "open")
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, "closed").SetProperty(f => f.ClosedAt, now), ct);

    /// <summary>A moderator's statement checked where it is stored (design 6.8): the refusal, or null when it passes.</summary>
    private static Task<Result<Unit>>? Refused(string? statement, bool required) =>
        StatementRules.Check(statement, required) is { } broken ? Task.FromResult<Result<Unit>>(RuleErrors.Of(broken)) : null;

    private delegate Task<Result<Unit>> RecordAction(AppealsDb db, BillRecord record, DateTimeOffset now);

    private delegate Task<Result<string?>> ReasonAction(AppealsDb db, Reason reason, DateTimeOffset now);

    private Task<Result<Unit>> OnRecordAsync(
        Guid recordId, StaffActor moderator, string action, string? statement, bool answersReports, RecordAction apply, CancellationToken ct, object? details = null) =>
        OnRecordAsync(recordId, moderator, action, statement, answersReports, apply, () => details, ct);

    /// <summary>
    /// An action on a bill's record: applied, recorded and audited in one transaction, under the bill's lock (design 6.8).
    /// Nobody is emailed: a record has no author since 2026-11-14 (design 8.12.4).
    /// </summary>
    private async Task<Result<Unit>> OnRecordAsync(
        Guid recordId, StaffActor moderator, string action, string? statement, bool answersReports, RecordAction apply, Func<object?> details, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var number = await db.BillRecords.Where(a => a.Id == recordId).Select(a => a.BillNumber).FirstOrDefaultAsync(ct);
        if (number is null)
        {
            return Error.NotFound("record.not-found");
        }

        await AdvisoryLocks.BillAsync(db, number, ct);
        var record = await db.BillRecords.FirstAsync(a => a.Id == recordId, ct);
        if (!RecordRules.IsRecord(record.Status))
        {
            return Error.NotFound("record.not-found");
        }

        var now = clock.GetUtcNow();
        var outcome = await apply(db, record, now);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        record.ChangedAt = now;
        await recorder.RecordAsync(db, transaction, "record", recordId, action, StatementRules.Normalise(statement), moderator, now, record.BillNumber, details(), answersReports, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    /// <summary>An action on an argument: each of them answers the reports on it.</summary>
    private async Task<Result<Unit>> OnReasonAsync(
        Guid reasonId, StaffActor moderator, string action, string? statement, ReasonAction apply, CancellationToken ct, object? details = null)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var reason = await db.Reasons.FirstOrDefaultAsync(r => r.Id == reasonId, ct);
        var record = reason is null ? null : await db.BillRecords.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reason.RecordId, ct);
        if (reason is null || record is null)
        {
            return Error.NotFound("reason.not-found");
        }

        // On a closed record only hiding, restoring and "Palikti" answer a report (design 6.9); on a hidden one nothing.
        if (RecordRules.CheckReasonAction(record.Status, action, reason.Status) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        var now = clock.GetUtcNow();
        var outcome = await apply(db, reason, now);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        var stored = StatementRules.Normalise(statement);
        await recorder.RecordAsync(db, transaction, "reason", reasonId, action, stored, moderator, now, record.BillNumber, details, answersReports: true, ct);
        if (outcome.Value is { } notice)
        {
            var excerpt = reason.Text.Length > 120 ? reason.Text[..120] + "…" : reason.Text;
            await NotifyAsync(transaction, reason.AuthorUserId, notice, record.BillNumber, stored, excerpt, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    public Task<Result<AuthorView>> AuthorOfReasonAsync(Guid reasonId, CancellationToken cancellationToken) =>
        ViewAsync(db => db.Reasons.Where(r => r.Id == reasonId).Select(r => (Guid?)r.AuthorUserId), "reason.not-found", cancellationToken);

    public Task<Result<Guid>> RestrictAuthorOfReasonAsync(Guid reasonId, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        RestrictAsync(db => db.Reasons.Where(r => r.Id == reasonId).Select(r => (Guid?)r.AuthorUserId), "reason.not-found", until, statement, moderator, contact, cancellationToken);

    public async Task<IReadOnlyList<RestrictionInfo>> RestrictionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var holding = await WritingRestrictionReads.HoldingAsync(db, null, now, cancellationToken);
        var names = await reads.NamesAsync(holding.Select(w => w.UserId), cancellationToken);
        return holding.Select(w => Info(w, names.GetValueOrDefault(w.UserId), now)).ToList();
    }

    public async Task<Result<Unit>> LiftRestrictionAsync(Guid restrictionId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken)
    {
        if (StatementRules.Check(statement, required: true) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var restriction = await db.WritingRestrictions.FirstOrDefaultAsync(w => w.Id == restrictionId, cancellationToken);
        if (restriction is null)
        {
            return Error.NotFound("restriction.not-found");
        }

        var now = clock.GetUtcNow();
        if (!WritingRules.Holds(restriction.EndsAt, restriction.LiftedAt, now))
        {
            return Error.Conflict("restriction.not-holding");
        }

        restriction.LiftedAt = now;
        restriction.LiftedByUserId = moderator.UserId;
        restriction.LiftStatement = statement.Trim();
        await recorder.RecordAsync(db, transaction, "restriction", restriction.Id, "lift", restriction.LiftStatement, moderator, now, null, null, answersReports: false, cancellationToken);
        await NotifyRestrictionAsync(transaction, restriction.UserId, WritingRestrictionNotices.Lifted, null, restriction.LiftStatement, contact, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    internal async Task<Result<AuthorView>> ViewAsync(Func<AppealsDb, IQueryable<Guid?>> who, string notFound, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (await who(db).FirstOrDefaultAsync(ct) is not { } userId)
        {
            return Error.NotFound(notFound);
        }

        var now = clock.GetUtcNow();
        var reasons = await db.Reasons.AsNoTracking().Where(r => r.AuthorUserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var reasonRecords = reasons.Select(r => r.RecordId).Distinct().ToList();
        var billsOf = await db.BillRecords.AsNoTracking().Where(a => reasonRecords.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.BillNumber, ct);
        var restrictions = await db.WritingRestrictions.AsNoTracking().Where(w => w.UserId == userId).OrderByDescending(w => w.SetAt).ToListAsync(ct);
        var targets = reasons.Select(r => r.Id).Concat(restrictions.Select(w => w.Id)).ToList();
        var actions = await db.ModerationActions.AsNoTracking().Where(m => targets.Contains(m.TargetId)).OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
        var name = (await reads.NamesAsync([userId], ct)).GetValueOrDefault(userId);

        // The person's arguments and what moderators did about them. Never the address, a side or a mark.
        return new AuthorView(
            name,
            reasons.Select(r => new AuthorReason(r.Id, billsOf.GetValueOrDefault(r.RecordId) ?? "", r.Side, r.Text, r.Status, r.CreatedAt)).ToList(),
            actions.Select(m => new ModerationEntry(m.TargetKind, m.Action, m.Statement, m.CreatedAt)).ToList(),
            restrictions.Select(w => Info(w, name, now)).ToList());
    }

    internal async Task<Result<Guid>> RestrictAsync(
        Func<AppealsDb, IQueryable<Guid?>> who, string notFound, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (StatementRules.Check(statement, required: true) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        if (WritingRules.CheckUntil(until, now) is { } wrong)
        {
            return Error.Validation(wrong, "until");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await who(db).FirstOrDefaultAsync(ct) is not { } userId)
        {
            return Error.NotFound(notFound);
        }

        if (await WritingRestrictionReads.IsRestrictedAsync(db, userId, now, ct))
        {
            // One at a time: to change its length, a moderator lifts it and sets a new one.
            return Error.Conflict("restriction.exists");
        }

        var restriction = new WritingRestriction
        {
            Id = Ids.New(clock),
            UserId = userId,
            EndsAt = until is { } day ? WritingRules.EndsAt(day) : null,
            Statement = statement.Trim(),
            SetByUserId = moderator.UserId,
            SetAt = now,
        };
        db.WritingRestrictions.Add(restriction);
        await recorder.RecordAsync(db, transaction, "restriction", restriction.Id, "restrict", restriction.Statement, moderator, now, null, new { until }, answersReports: false, ct);
        await NotifyRestrictionAsync(transaction, userId, WritingRestrictionNotices.Restricted, until, restriction.Statement, contact, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return restriction.Id;
    }

    private static RestrictionInfo Info(WritingRestriction w, string? name, DateTimeOffset now) =>
        new(w.Id, name, WritingRules.Until(w.EndsAt), w.Statement, w.SetAt, WritingRules.Holds(w.EndsAt, w.LiftedAt, now), w.LiftedAt, w.LiftStatement);

    private async Task NotifyRestrictionAsync(IDbContextTransaction transaction, Guid userId, string notice, DateOnly? until, string statement, string? contact, CancellationToken ct)
    {
        if (await participants.FindAsync(userId, ct) is not { } person)
        {
            return;
        }

        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.WritingRestrictionEmail, new WritingRestrictionEmail(person.Email, notice, until, statement, contact)),
            transaction.GetDbTransaction(),
            ct);
    }

    /// <summary>Tells an argument's author what a moderator did and why, with the link to the bill's page.</summary>
    private async Task NotifyAsync(
        IDbContextTransaction transaction, Guid authorId, string notice, string billNumber, string? statement, string? excerpt, CancellationToken ct)
    {
        if (await participants.FindAsync(authorId, ct) is not { } author)
        {
            return;
        }

        var bill = await reads.BillAsync(billNumber, ct);
        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.ModerationNoticeEmail, new ModerationNoticeEmail(author.Email, notice, bill.Number, bill.Title, statement, excerpt, BillPath(bill.Number))),
            transaction.GetDbTransaction(),
            ct);
    }
}
