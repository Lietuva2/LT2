using System.Globalization;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// What members do on a bill (design 8.12.1, 8.2): take or change a side, write and endorse arguments, report. Every active
/// bill is open: the first side or argument creates the bill's record (<see cref="BillRecords"/>). Every action of one
/// person runs under that person's lock (<see cref="AdvisoryLocks.PersonAsync"/>), so the limits per person and the consent
/// check cannot be raced. Taking part ends for everyone when the bill's final vote is recorded (design 6.8).
/// </summary>
internal sealed class AppealsService(
    IDbContextFactory<AppealsDb> contexts,
    AppealReads reads,
    BillRecords records,
    IParticipants participants,
    IBillCatalogue bills,
    IPreScreening screening,
    IPlannedAgenda agenda,
    ISettings settings,
    IAuditLog audit,
    IMpLinks links,
    TimeProvider clock) : IAppealsModule
{
    private const int PageSize = 20;

    /// <summary>"Planuojami artimiausiu metu" (design 8.12.5): a planned date within this many days from today.</summary>
    private const int SoonDays = 14;

    public Task<AppealRulesInfo> RulesAsync(CancellationToken cancellationToken) => reads.RulesAsync(cancellationToken);

    public async Task<BillListPage> ListAsync(BillListQuery query, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        var offset = int.TryParse(query.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : 0;

        if (query.Closed)
        {
            // The closed tab (design 8.12.5): records the Seimas finished, newest first, with their frozen counts.
            var (closedPage, total) = await ClosedPageAsync(db, query.Search, offset, cancellationToken);
            var closedFacts = await reads.BillsAsync(closedPage.Select(a => a.BillNumber).ToList(), cancellationToken);
            var shownCounts = await reads.ShownCountsAsync(db, closedPage, cancellationToken);
            var closedArguments = await ArgumentCountsAsync(db, closedPage.Select(a => a.Id).ToList(), cancellationToken);
            var closedItems = closedPage.Select(a =>
            {
                var shown = shownCounts[a.Id];
                return new BillListItem(a.Id, closedFacts.GetValueOrDefault(a.BillNumber) ?? new BillFacts(a.BillNumber, a.BillNumber), a.Status, shown,
                    AppealReads.Threshold(a, shown, threshold), null, closedArguments.GetValueOrDefault(a.Id));
            }).ToList();
            return new BillListPage(closedItems, total, Next(offset, closedPage.Count, total));
        }

        // Active bills (design 8.12.1): in the catalogue and not finished; a bill whose record is hidden or closed is not
        // offered here.
        var active = (await bills.ActiveAsync(cancellationToken)).Where(b => BillListRules.Matches(query.Search, b.Number, b.Title)).ToList();
        var numbers = active.Select(b => b.Number).ToList();
        var recordsOnBills = await db.BillRecords.AsNoTracking().Records()
            .Where(a => numbers.Contains(a.BillNumber))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, cancellationToken);
        var openIds = recordsOnBills.Values.Where(a => a.Status == BillRecordStatuses.Open).Select(a => a.Id).ToList();
        var counts = await reads.CountsAsync(db, openIds, cancellationToken);
        var arguments = await ArgumentCountsAsync(db, openIds, cancellationToken);
        var planned = await agenda.ForBillsAsync(numbers, cancellationToken);
        var soon = VilniusTime.Today(clock).AddDays(SoonDays);
        var candidates = active
            .Where(b => !recordsOnBills.TryGetValue(b.Number, out var r) || r.Status == BillRecordStatuses.Open)
            .Where(b => !query.PlannedOnly || (planned.TryGetValue(b.Number, out var p) && p.Date <= soon))
            .Select(b =>
            {
                var record = recordsOnBills.GetValueOrDefault(b.Number);
                var c = record is null ? SideCounts.None : counts[record.Id];
                return new BillListItem(
                    record?.Id, b, BillRecordStatuses.Open, c, AppealReads.Threshold(record, c, threshold), planned.GetValueOrDefault(b.Number),
                    record is null ? 0 : arguments.GetValueOrDefault(record.Id), record?.LastActivityAt);
            });
        var ordered = BillListRules.Order(
            candidates, BillSorts.Of(query.Sort), i => i.Counts.Total, i => i.Arguments, i => i.Planned?.Date, i => i.Bill.Number, i => i.LastActivityAt);
        var page = ordered.Skip(Math.Min(offset, ordered.Count)).Take(PageSize).ToList();
        return new BillListPage(page, ordered.Count, Next(offset, page.Count, ordered.Count));
    }

    /// <summary>
    /// One page of the closed tab, newest closing first, and how many there are. Paged in the database; a search, which
    /// matches the titles the Parliament module holds, first narrows the closed bills' numbers.
    /// </summary>
    private async Task<(List<BillRecord> Page, int Total)> ClosedPageAsync(AppealsDb db, string? search, int offset, CancellationToken ct)
    {
        var closed = db.BillRecords.AsNoTracking().Where(a => a.Status == BillRecordStatuses.Closed);
        if (string.IsNullOrWhiteSpace(search))
        {
            return (await closed.OrderByDescending(a => a.ClosedAt).ThenBy(a => a.Id).Skip(offset).Take(PageSize).ToListAsync(ct), await closed.CountAsync(ct));
        }

        var numbers = await closed.OrderByDescending(a => a.ClosedAt).ThenBy(a => a.Id).Select(a => a.BillNumber).ToListAsync(ct);
        var titles = await reads.BillsAsync(numbers, ct);
        var matching = numbers.Where(n => BillListRules.Matches(search, n, titles.GetValueOrDefault(n)?.Title ?? "")).ToList();
        var pageNumbers = matching.Skip(offset).Take(PageSize).ToList();
        var page = await closed.Where(a => pageNumbers.Contains(a.BillNumber)).OrderByDescending(a => a.ClosedAt).ThenBy(a => a.Id).ToListAsync(ct);
        return (page, matching.Count);
    }

    /// <summary>How many published arguments each given record has.</summary>
    private static async Task<Dictionary<Guid, int>> ArgumentCountsAsync(AppealsDb db, IReadOnlyCollection<Guid> recordIds, CancellationToken ct) =>
        await db.Reasons.AsNoTracking()
            .Where(r => recordIds.Contains(r.RecordId) && r.Status == ReasonStatuses.Published)
            .GroupBy(r => r.RecordId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static string? Next(int offset, int taken, int total) =>
        offset + taken < total ? (offset + taken).ToString(CultureInfo.InvariantCulture) : null;

    public async Task<Result<BillRecordInfo>> GetAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken) is { } record)
        {
            return await InfoAsync(db, record, cancellationToken);
        }

        // No record: an active bill is open with zero counts (design 8.12.1). A finished bill without one has nothing to
        // show here (not found); its page shows the bill from the Parliament module.
        var bill = await records.ActiveBillAsync(billNumber, cancellationToken);
        if (!bill.IsSuccess)
        {
            return bill.Error!.Code == "record.not-open" ? Error.NotFound("record.not-found") : bill.Error!;
        }

        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        return new BillRecordInfo(
            null, bill.Value, BillRecordStatuses.Open, "", null, null, null, null, SideCounts.None, AppealReads.Threshold(null, SideCounts.None, threshold), [], null, null, null,
            (await agenda.ForBillsAsync([bill.Value.Number], cancellationToken)).GetValueOrDefault(bill.Value.Number), null);
    }

    public async Task<Result<IReadOnlyList<(DateTimeOffset ChangedAt, string? OldText, string? NewText)>>> HistoryAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken);
            return bill is null
                ? Error.NotFound("bill.not-found")
                : Result<IReadOnlyList<(DateTimeOffset, string?, string?)>>.Success([]);
        }

        if (record.Status == BillRecordStatuses.Rejected)
        {
            return Error.NotFound("record.not-found");
        }

        var rows = await db.Revisions.AsNoTracking()
            .Where(r => r.EntityKind == "record" && r.EntityId == record.Id && r.Field == "description")
            .OrderBy(r => r.ChangedAt)
            .Select(r => new { r.ChangedAt, r.OldValue, r.NewValue })
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<(DateTimeOffset, string?, string?)>>.Success(rows.Select(r => (r.ChangedAt, r.OldValue, r.NewValue)).ToList());
    }

    public async Task<Result<MyBillRecord>> MineAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        var facts = await participants.FindAsync(userId, cancellationToken);
        var reasonLimit = await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, cancellationToken);
        var endorsementLimit = await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, cancellationToken);
        if (record is null || record.Status == BillRecordStatuses.Rejected)
        {
            // Nothing of the person's on a bill without a record; on a hidden one nothing is shown (design 8.12.3).
            var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken);
            return bill is null
                ? Error.NotFound("bill.not-found")
                : new MyBillRecord(null, [], AppealRules.Left(0, endorsementLimit), [], AppealRules.Left(0, reasonLimit), facts?.HasPositionConsent == true);
        }

        var side = await db.Positions.AsNoTracking()
            .Where(p => p.RecordId == record.Id && p.UserId == userId)
            .Select(p => p.Side)
            .FirstOrDefaultAsync(cancellationToken);
        var endorsed = await db.Endorsements.AsNoTracking()
            .Where(e => e.RecordId == record.Id && e.UserId == userId)
            .Select(e => e.ReasonId)
            .ToListAsync(cancellationToken);
        var mine = await db.Reasons.AsNoTracking()
            .Where(r => r.RecordId == record.Id && r.AuthorUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await reads.NamesAsync([userId], cancellationToken);
        var mineIds = mine.Select(r => r.Id).ToList();
        var endorsements = await AppealReads.EndorsementCountsAsync(db, mineIds, cancellationToken);
        return new MyBillRecord(
            side,
            endorsed,
            AppealRules.Left(endorsed.Count, endorsementLimit),
            mine.Select(r => AppealReads.ToInfo(r, names, endorsements)).ToList(),
            AppealRules.Left(mine.Count(r => AppealRules.CountedReasonStatuses.Contains(r.Status)), reasonLimit),
            facts?.HasPositionConsent == true)
        {
            Editable = await EditableAsync(db, record, userId, facts, mine, endorsements, cancellationToken),
        };
    }

    /// <summary>
    /// The person's own arguments they may edit now (design 8.2, owner 2026-11-17), by the rules the edit is checked by: the
    /// record open, no writing restriction, and nobody reacted (comments counted whatever their state).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> EditableAsync(
        AppealsDb db, BillRecord record, Guid userId, ParticipantFacts? facts, List<Reason> mine, Dictionary<Guid, int> endorsements, CancellationToken ct)
    {
        if (mine.Count == 0 || facts is null || MpAccounts.Refusal(facts) is not null || await NotOpenAsync(record, ct) is not null
            || await RestrictedAsync(db, userId, ct) is not null)
        {
            return [];
        }

        var ids = mine.Select(r => r.Id).ToList();
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.TargetKind == CommentKinds.Reason && ids.Contains(c.TargetId))
            .GroupBy(c => c.TargetId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return mine.Where(r => AppealRules.CheckEdit(r.Status, isAuthor: true, endorsements.GetValueOrDefault(r.Id), comments.GetValueOrDefault(r.Id)) is null)
            .Select(r => r.Id).ToList();
    }

    public async Task<Result<bool>> TakePositionAsync(string billNumber, Guid userId, string side, CancellationToken cancellationToken)
    {
        if (AppealRules.CheckSide(side) is { } invalid)
        {
            return RuleErrors.Of(invalid);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);

        // Read after the lock: a consent withdrawn a moment ago is seen here (IPositionErasure takes the same lock).
        var participant = await participants.FindAsync(userId, cancellationToken);
        if (participant is null)
        {
            return Error.Forbidden("member.required");
        }

        // Design 8.5.4: the MP's own account takes no side.
        if (MpAccounts.Refusal(participant) is { } mpAccount)
        {
            return mpAccount;
        }

        if (!participant.HasPositionConsent)
        {
            return Error.Conflict("consent.required");
        }

        var record = await OpenRecordAsync(db, billNumber, cancellationToken);
        if (!record.IsSuccess)
        {
            return record.Error!;
        }

        var now = clock.GetUtcNow();
        var position = await db.Positions.FirstOrDefaultAsync(p => p.RecordId == record.Value.Id && p.UserId == userId, cancellationToken);
        if (position is null)
        {
            db.Positions.Add(new Position
            {
                RecordId = record.Value.Id,
                UserId = userId,
                Side = side,
                CreatedAt = now,
                ChangedAt = now,
                MemberSince = participant.MemberSince,
            });
        }
        else if (position.Side != side)
        {
            if (AppealRules.ChangingSideDropsEndorsementsOfTheOldSide(position.Side, side))
            {
                var oldSide = position.Side;
                await db.Endorsements
                    .Where(e => e.RecordId == record.Value.Id && e.UserId == userId
                        && db.Reasons.Any(r => r.Id == e.ReasonId && r.Side == oldSide))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // Overwritten in place: no earlier side is kept anywhere (design 5.3).
            position.Side = side;
            position.ChangedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (position is null || position.ChangedAt == now)
        {
            // Design 8.12.5: a side taken or changed is the bill's activity (the time only, never who).
            await RecordActivity.TouchAsync(db, record.Value.Id, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return position is null;
    }

    public async Task<Result<Unit>> ClearPositionAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);

        // Clearing creates nothing: on a bill without a record there is no side to clear.
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            return await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken) is null ? Error.NotFound("bill.not-found") : Result.Ok();
        }

        if (await NotOpenAsync(record, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        await db.Endorsements.Where(e => e.RecordId == record.Id && e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        if (await db.Positions.Where(p => p.RecordId == record.Id && p.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0)
        {
            // Design 8.12.5: a side withdrawn is the bill's activity too.
            await RecordActivity.TouchAsync(db, record.Id, clock.GetUtcNow(), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<IReadOnlyList<ReasonInfo>>> FindSimilarAsync(string billNumber, NewReason request, CancellationToken cancellationToken)
    {
        if (await ReasonTextErrorAsync(request, cancellationToken) is { } broken)
        {
            return broken;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            // An active bill nobody wrote on has nothing similar yet.
            var bill = await records.ActiveBillAsync(billNumber, cancellationToken);
            return bill.IsSuccess ? Result<IReadOnlyList<ReasonInfo>>.Success([]) : bill.Error!;
        }

        if (record.Status != BillRecordStatuses.Open)
        {
            return Error.Conflict("record.not-open");
        }

        var existing = await db.Reasons.AsNoTracking()
            .Where(r => r.RecordId == record.Id && r.Side == request.Side && r.Status == ReasonStatuses.Published)
            .ToListAsync(cancellationToken);
        var similar = ReasonSimilarity.MostSimilar(AppealRules.NormaliseText(request.Text), existing, r => r.Text);
        var names = await reads.NamesAsync(similar.Select(r => r.AuthorUserId), cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, similar.Select(r => r.Id).ToList(), cancellationToken);
        return Result<IReadOnlyList<ReasonInfo>>.Success(similar.Select(r => AppealReads.ToInfo(r, names, endorsements)).ToList());
    }

    /// <summary>
    /// What pre-screening finds in an argument (design 8.3): the rules every screened text has, and an argument with fewer words
    /// than <c>Appeals:MinReasonWords</c> is too short (owner, 2026-11-17: arguments only). Any flag makes it wait for a moderator.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReasonFlagsAsync(string text, CancellationToken ct)
    {
        var flags = (await screening.ScreenAsync(text, ct)).ToList();
        if (ReasonWords.TooShort(text, await settings.GetAsync(SettingKeys.AppealsMinReasonWords, ct)))
        {
            flags.Add(ScreeningFlags.TooShort);
        }

        return flags;
    }

    /// <summary>
    /// The side and the text of an argument against the rules, with the length the setting allows now (design 8.2, 6.8:
    /// checked here for every caller); a too-long text's error names that number.
    /// </summary>
    private async Task<Error?> ReasonTextErrorAsync(NewReason request, CancellationToken cancellationToken)
    {
        var maxLength = await settings.GetAsync(SettingKeys.TextsMaxLength, cancellationToken);
        return FirstBroken(AppealRules.CheckSide(request.Side), AppealRules.CheckReasonText(request.Text, maxLength)) switch
        {
            null => null,
            "reason.too-long" => RuleErrors.Of("reason.too-long") with { Numbers = [MemberTextLength.Allowed(maxLength)] },
            var broken => RuleErrors.Of(broken),
        };
    }

    public async Task<Result<ReasonCreated>> WriteReasonAsync(string billNumber, Guid userId, NewReason request, CancellationToken cancellationToken)
    {
        if (await ReasonTextErrorAsync(request, cancellationToken) is { } broken)
        {
            return broken;
        }

        var text = AppealRules.NormaliseText(request.Text);
        var flags = await ReasonFlagsAsync(text, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);
        var author = await participants.FindAsync(userId, cancellationToken);
        if (author is null)
        {
            return Error.Forbidden("member.required");
        }

        // Design 8.5.4: the MP's own account writes no argument; the MP speaks through the response.
        if (MpAccounts.Refusal(author) is { } mpAccount)
        {
            return mpAccount;
        }

        if (await RestrictedAsync(db, userId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var record = await OpenRecordAsync(db, billNumber, cancellationToken);
        if (!record.IsSuccess)
        {
            return record.Error!;
        }

        // Either side, whatever the author's own (design 8.2): the position is not read, and writing never stores one.
        var counted = AppealRules.CountedReasonStatuses.ToList();
        var written = await db.Reasons.CountAsync(r => r.RecordId == record.Value.Id && r.AuthorUserId == userId && counted.Contains(r.Status), cancellationToken);
        var limit = await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, cancellationToken);
        if (AppealRules.CheckNewReason(request.Side, written, limit) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        var reason = new Reason
        {
            Id = Ids.New(clock),
            RecordId = record.Value.Id,
            Side = request.Side,
            AuthorUserId = userId,
            Text = text,
            Status = flags.Count > 0 ? ReasonStatuses.Pending : ReasonStatuses.Published,
            ScreeningFlags = [.. flags],
            CreatedAt = clock.GetUtcNow(),
        };
        reason.FirstPublicAt = reason.Status == ReasonStatuses.Published ? reason.CreatedAt : null;
        db.Reasons.Add(reason);
        await db.SaveChangesAsync(cancellationToken);
        if (reason.FirstPublicAt is { } published)
        {
            // Design 8.12.5: an argument published is the bill's activity; one waiting for a moderator is not yet.
            await RecordActivity.TouchAsync(db, record.Value.Id, published, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ReasonCreated(reason.Id, reason.Status);
    }

    /// <summary>
    /// The author edits their argument's text and side (design 8.2, owner 2026-11-17) while the record is open and nobody has
    /// reacted to it (<see cref="AppealRules.CheckEdit"/>), checked under the argument's lock, which "Man svarbu" and a comment
    /// under it take too. The new text keeps the rules of writing (the length, pre-screening: a flagged edit waits for a
    /// moderator again); the moderator's "seen" goes, since nobody has seen these words; the similar arguments are not asked
    /// again. No earlier text is kept; the audit says that an edit happened, never the text or the side.
    /// </summary>
    public async Task<Result<ReasonCreated>> EditReasonAsync(Guid reasonId, Guid userId, NewReason request, CancellationToken cancellationToken)
    {
        if (await ReasonTextErrorAsync(request, cancellationToken) is { } broken)
        {
            return broken;
        }

        var text = AppealRules.NormaliseText(request.Text);
        var flags = await ReasonFlagsAsync(text, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);
        await AdvisoryLocks.ReasonAsync(db, reasonId, cancellationToken);
        var author = await participants.FindAsync(userId, cancellationToken);
        if (author is null)
        {
            return Error.Forbidden("member.required");
        }

        if (MpAccounts.Refusal(author) is { } mpAccount)
        {
            return mpAccount;
        }

        var reason = await db.Reasons.FirstOrDefaultAsync(r => r.Id == reasonId, cancellationToken);
        if (reason is null)
        {
            return RuleErrors.Of("reason.not-found");
        }

        // Whose and in what state first (the reactions are counted once the record is known to be open).
        if (AppealRules.CheckEdit(reason.Status, reason.AuthorUserId == userId, endorsements: 0, comments: 0) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        if (await RestrictedAsync(db, userId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var record = await db.BillRecords.AsNoTracking().FirstAsync(a => a.Id == reason.RecordId, cancellationToken);
        if (await NotOpenAsync(record, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        var endorsements = await db.Endorsements.CountAsync(e => e.ReasonId == reasonId, cancellationToken);
        var comments = await db.Comments.CountAsync(c => c.TargetKind == CommentKinds.Reason && c.TargetId == reasonId, cancellationToken);
        if (AppealRules.CheckEdit(reason.Status, isAuthor: true, endorsements, comments) is { } reacted)
        {
            return RuleErrors.Of(reacted);
        }

        var now = clock.GetUtcNow();
        reason.Side = request.Side;
        reason.Text = text;
        reason.ScreeningFlags = [.. flags];
        reason.Status = AppealRules.StatusAfterEdit(reason.Status, flags.Count > 0);
        reason.SeenAt = null;
        reason.EditedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        if (reason.Status == ReasonStatuses.Published)
        {
            // Design 8.12.5: an argument changed in public is the bill's activity; one waiting for a moderator is not.
            await RecordActivity.TouchAsync(db, record.Id, now, cancellationToken);
        }

        await audit.WriteAsync(
            new AuditEntry(
                "reason.edited", userId, "member", "reason:" + reasonId,
                System.Text.Json.JsonSerializer.SerializeToElement(new { bill = record.BillNumber }, System.Text.Json.JsonSerializerOptions.Web),
                null),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReasonCreated(reason.Id, reason.Status);
    }

    public async Task<Result<Unit>> EndorseAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);

        // Under the argument's lock, which its author's edit takes: a mark never crosses an edit (design 8.2).
        await AdvisoryLocks.ReasonAsync(db, reasonId, cancellationToken);

        // Design 8.5.4: the MP's own account gives no "Man svarbu".
        if (MpAccounts.Refusal(await participants.FindAsync(userId, cancellationToken)) is { } mpAccount)
        {
            return mpAccount;
        }

        var reason = await db.Reasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reasonId && r.Status == ReasonStatuses.Published, cancellationToken);
        var record = reason is null ? null : await db.BillRecords.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reason.RecordId, cancellationToken);
        if (reason is null || record is null || record.Status != BillRecordStatuses.Open)
        {
            return Error.NotFound("reason.not-found");
        }

        if (await NotOpenAsync(record, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        if (await db.Endorsements.AnyAsync(e => e.ReasonId == reasonId && e.UserId == userId, cancellationToken))
        {
            return Result.Ok();
        }

        var side = await db.Positions.Where(p => p.RecordId == record.Id && p.UserId == userId).Select(p => p.Side).FirstOrDefaultAsync(cancellationToken);
        var already = await db.Endorsements.CountAsync(e => e.RecordId == record.Id && e.UserId == userId, cancellationToken);
        var limit = await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, cancellationToken);
        if (AppealRules.CheckEndorsement(reason.AuthorUserId == userId, side, reason.Side, already, limit) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        db.Endorsements.Add(new Endorsement { ReasonId = reasonId, UserId = userId, RecordId = record.Id, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> RemoveEndorsementAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);
        var recordId = await db.Reasons.Where(r => r.Id == reasonId).Select(r => (Guid?)r.RecordId).FirstOrDefaultAsync(cancellationToken);
        if (recordId is null)
        {
            return Error.NotFound("reason.not-found");
        }

        var record = await db.BillRecords.AsNoTracking().FirstAsync(a => a.Id == recordId, cancellationToken);
        if (await NotOpenAsync(record, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        await db.Endorsements.Where(e => e.ReasonId == reasonId && e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The author withdraws their own argument (design 8.2, decided 2026-11-13), while the bill is open: the row is
    /// deleted with its "Man svarbu" marks, arguments once merged into it stay merged but point nowhere, and open reports
    /// on it are closed. A summary already made keeps its frozen text. The audit entry names the bill, not the text or
    /// the side. Someone else's argument: <c>reason.author-only</c>; a hidden, merged or unknown one: not found; on a
    /// record that is not open (closed or hidden) or a finished bill: <c>record.not-open</c>.
    /// </summary>
    public async Task<Result<Unit>> WithdrawReasonAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);
        var counted = AppealRules.CountedReasonStatuses.ToList();
        var reason = await db.Reasons.FirstOrDefaultAsync(r => r.Id == reasonId && counted.Contains(r.Status), cancellationToken);
        if (reason is null)
        {
            return Error.NotFound("reason.not-found");
        }

        if (reason.AuthorUserId != userId)
        {
            return Error.Forbidden("reason.author-only");
        }

        var record = await db.BillRecords.AsNoTracking().FirstAsync(a => a.Id == reason.RecordId, cancellationToken);
        if (await NotOpenAsync(record, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        var now = clock.GetUtcNow();
        await db.Endorsements.Where(e => e.ReasonId == reasonId).ExecuteDeleteAsync(cancellationToken);
        await db.Reasons.Where(r => r.MergedIntoId == reasonId).ExecuteUpdateAsync(s => s.SetProperty(r => r.MergedIntoId, (Guid?)null), cancellationToken);
        await ModerationRecorder.CloseReportsAsync(db, reasonId, now, cancellationToken);

        // Design 8.23.7: its discussion goes with it, as its "Man svarbu" marks do.
        await CommentCascade.DeleteForTargetsAsync(db, CommentKinds.Reason, [reasonId], now, cancellationToken);
        db.Reasons.Remove(reason);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(
                "reason.withdrawn", userId, "member", "reason:" + reasonId,
                System.Text.Json.JsonSerializer.SerializeToElement(new { bill = record.BillNumber }, System.Text.Json.JsonSerializerOptions.Web),
                null),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<MyRestriction?> MyRestrictionAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var holding = (await WritingRestrictionReads.HoldingAsync(db, userId, clock.GetUtcNow(), cancellationToken))
            .OrderBy(w => w.EndsAt is null ? 0 : 1)
            .ThenByDescending(w => w.EndsAt)
            .FirstOrDefault();
        return holding is null ? null : new MyRestriction(WritingRules.Until(holding.EndsAt), holding.Statement);
    }

    /// <summary>The MP the account speaks for (the MP's own account or an office member), if any.</summary>
    private async Task<long?> SpeaksForAsync(Guid userId, CancellationToken ct) =>
        (await links.SpeaksForAsync([userId], ct)).TryGetValue(userId, out var person) ? person : null;

    /// <summary>
    /// A moderator's restriction on writing (design 8.3) blocks what the person writes for others to read: arguments and
    /// date flags. Never sides, marks, reports or anything about the account.
    /// </summary>
    private async Task<Error?> RestrictedAsync(AppealsDb db, Guid userId, CancellationToken ct) =>
        await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), ct) ? Error.Forbidden("writing.restricted") : null;

    public async Task<Result<Unit>> ReportAsync(Guid userId, NewReport request, CancellationToken cancellationToken)
    {
        if (!ReportGrounds.For(request.TargetKind).Contains(request.Ground))
        {
            return Error.Validation("report.ground", "ground");
        }

        var text = string.IsNullOrWhiteSpace(request.Text) ? null : AppealRules.NormaliseText(request.Text);
        if (text is { Length: > ReportGrounds.TextMaxLength })
        {
            return Error.Validation("report.too-long", "text");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        Guid? recordId;

        // Owner, 2026-11-17: nobody reports their own text: an argument or a comment they wrote, the statement of an organisation
        // they represent, the response of the MP they speak for, a comment in either voice. (An organisation is not a text.)
        var own = false;
        var represented = await db.OrganisationRepresentatives.AsNoTracking()
            .Where(r => r.UserId == userId && r.EndedAt == null).Select(r => r.OrganisationId).ToListAsync(cancellationToken);
        switch (request.TargetKind)
        {
            case "record":
                // What is reported on a bill's record is its description (design 8.13): a record without one has nothing to report.
                var record = await db.BillRecords.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.TargetId, cancellationToken);
                if (record is null || record.Status is not (BillRecordStatuses.Open or BillRecordStatuses.Closed) || record.Description.Length == 0)
                {
                    return Error.NotFound("report.target-not-found");
                }

                recordId = record.Id;
                break;
            case "reason":
                var reason = await db.Reasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.TargetId && r.Status == ReasonStatuses.Published, cancellationToken);
                if (reason is null)
                {
                    return Error.NotFound("report.target-not-found");
                }

                recordId = reason.RecordId;
                own = reason.AuthorUserId == userId;
                break;
            case "organisation":
                // Design 8.14.14: an organisation shown on the site; a blocked or closed one is shown nowhere.
                if (!await db.Organisations.AnyAsync(o => o.Id == request.TargetId && o.Status == OrganisationStatuses.Active, cancellationToken))
                {
                    return Error.NotFound("report.target-not-found");
                }

                recordId = null;
                break;
            case "statement":
                if (await db.OrganisationStatements.Shown(db.Organisations).FirstOrDefaultAsync(s => s.Id == request.TargetId, cancellationToken) is not { } statement)
                {
                    return Error.NotFound("report.target-not-found");
                }

                recordId = null;
                own = statement.WrittenByUserId == userId || represented.Contains(statement.OrganisationId);
                break;
            case "comment":
                // Design 8.23.8: a comment that is shown (published, its target shown, its bill's record not hidden).
                if (!await CommentsService.IsShownAsync(db, request.TargetId, cancellationToken))
                {
                    return Error.NotFound("report.target-not-found");
                }

                recordId = null;
                var comment = await db.Comments.AsNoTracking().FirstAsync(c => c.Id == request.TargetId, cancellationToken);
                own = NewsRules.OwnComment(comment.AuthorUserId, comment.OrganisationId, comment.SeimasPersonId, userId, represented.ToHashSet(), await SpeaksForAsync(userId, cancellationToken));
                break;
            case "response":
                // Design 8.5.5: an MP's response that is shown (published, or withdrawn with its versions).
                if (await db.MpResponses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.TargetId && (r.Status == ResponseStatuses.Published || r.Status == ResponseStatuses.Withdrawn), cancellationToken) is not { } response)
                {
                    return Error.NotFound("report.target-not-found");
                }

                recordId = null;
                own = NewsRules.OwnResponse(response.WrittenByUserId, response.SeimasPersonId, userId, await SpeaksForAsync(userId, cancellationToken));
                break;
            default:
                return Error.Validation("report.target", "targetKind");
        }

        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        if (AppealRules.CheckReport(own) is { } ownText)
        {
            return RuleErrors.Of(ownText);
        }

        // One open report per person and target; reporting again changes nothing.
        if (await db.Reports.AnyAsync(r => r.TargetId == request.TargetId && r.ReporterUserId == userId && r.Status == "open", cancellationToken))
        {
            return Result.Ok();
        }

        db.Reports.Add(new Report
        {
            Id = Ids.New(clock),
            TargetKind = request.TargetKind,
            TargetId = request.TargetId,
            RecordId = recordId,
            ReporterUserId = userId,
            Ground = request.Ground,
            Text = text,
            CreatedAt = clock.GetUtcNow(),
        });

        // The same person reported the same thing twice at once: one report stands.
        await SaveIgnoringDuplicateAsync(db, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> FlagDateAsync(string billNumber, Guid userId, NewFlag request, CancellationToken cancellationToken)
    {
        if (!FlagKinds.All.Contains(request.Kind))
        {
            return Error.Validation("flag.kind", "kind");
        }

        if (!FlagKinds.IsLink(request.Link))
        {
            return Error.Validation("flag.link", "link");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : AppealRules.NormaliseText(request.Note);
        if (note is { Length: > 1000 })
        {
            return Error.Validation("flag.note", "note");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // Participants only (design 8.4): someone with a side on the bill, so a bill without a record has none.
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            return await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken) is null
                ? Error.NotFound("bill.not-found")
                : Error.Conflict("flag.participants-only");
        }

        if (record.Status != BillRecordStatuses.Open)
        {
            return Error.Conflict("record.not-open");
        }

        if (!await db.Positions.AnyAsync(p => p.RecordId == record.Id && p.UserId == userId, cancellationToken))
        {
            return Error.Conflict("flag.participants-only");
        }

        if (await RestrictedAsync(db, userId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        if (await db.Flags.AnyAsync(f => f.RecordId == record.Id && f.ReporterUserId == userId && f.Status == "open", cancellationToken))
        {
            return Result.Ok();
        }

        db.Flags.Add(new Flag
        {
            Id = Ids.New(clock),
            RecordId = record.Id,
            Kind = request.Kind,
            Link = request.Link.Trim(),
            Note = note,
            ProposedDate = request.Date,
            ReporterUserId = userId,
            CreatedAt = clock.GetUtcNow(),
        });

        // One open flag per person and bill (a unique index): a second sent at the same moment changes nothing.
        await SaveIgnoringDuplicateAsync(db, cancellationToken);
        return Result.Ok();
    }

    /// <summary>Saves; a row refused by a unique index (the same thing sent twice at once) is dropped, as if sent once.</summary>
    private static async Task SaveIgnoringDuplicateAsync(AppealsDb db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The bill's open record for a member taking part, created if the bill is active and has none (design 8.12.1). A
    /// closed or hidden record and a finished bill refuse with <c>record.not-open</c>.
    /// </summary>
    private async Task<Result<BillRecord>> OpenRecordAsync(AppealsDb db, string billNumber, CancellationToken ct)
    {
        var record = await records.EnsureAsync(db, billNumber, ct);
        if (!record.IsSuccess)
        {
            return record.Error!;
        }

        return await NotOpenAsync(record.Value, ct) is { } notOpen ? notOpen : record.Value;
    }

    /// <summary>
    /// Taking part on an existing record (design 8.12.1, 6.8): refused with <c>record.not-open</c> unless the record is
    /// open and the bill's final vote is not recorded, whether or not the hourly job has closed the record yet.
    /// </summary>
    private async Task<Error?> NotOpenAsync(BillRecord record, CancellationToken ct) =>
        RecordRules.CheckParticipation(record.Status, await records.IsFinishedAsync(record.Status, record.BillNumber, ct)) is { } refused
            ? RuleErrors.Of(refused)
            : null;

    private async Task<BillRecordInfo> InfoAsync(AppealsDb db, BillRecord record, CancellationToken ct)
    {
        var bill = await reads.BillAsync(record.BillNumber, ct);
        var threshold = await reads.ThresholdSettingsAsync(ct);
        if (record.Status == BillRecordStatuses.Rejected)
        {
            // Taking part is closed by the moderators (design 8.12.3): the bill and the statement, nothing of the record.
            var statement = await db.ModerationActions.AsNoTracking()
                .Where(m => m.TargetId == record.Id && m.Action == "hide")
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Statement)
                .FirstOrDefaultAsync(ct);
            return new BillRecordInfo(
                record.Id, bill, record.Status, "", null, null, null, null, SideCounts.None, AppealReads.Threshold(null, SideCounts.None, threshold), [], null, null, null, null,
                statement ?? "");
        }

        var counts = (await reads.ShownCountsAsync(db, [record], ct))[record.Id];
        var reasons = await db.Reasons.AsNoTracking()
            .Where(r => r.RecordId == record.Id && r.Status == ReasonStatuses.Published)
            .ToListAsync(ct);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, reasons.Select(r => r.Id).ToList(), ct);

        // An initiator's name only beside a description they wrote, before 2026-11-14 (design 8.12.4).
        var author = record.DescriptionSource == DescriptionSources.Initiator && record.InitiatorUserId is { } initiator ? initiator : (Guid?)null;
        var names = await reads.NamesAsync(reasons.Select(r => r.AuthorUserId).Concat(author is { } a ? [a] : []), ct);
        var profiles = await reads.ProfilesAsync(reasons.Select(r => r.AuthorUserId), ct);
        var comments = await CommentCascade.CountsAsync(db, CommentKinds.Reason, reasons.Select(r => r.Id).ToList(), ct);
        var ordered = reasons
            .OrderByDescending(r => endorsements.GetValueOrDefault(r.Id))
            .ThenBy(r => r.CreatedAt)
            .Select(r => AppealReads.ToInfo(r, names, endorsements, profiles) with { Comments = comments.GetValueOrDefault(r.Id) })
            .ToList();
        return new BillRecordInfo(
            record.Id,
            bill,
            record.Status,
            record.Description,
            record.Description.Length == 0 ? null : record.DescriptionSource,
            author is { } by ? names.GetValueOrDefault(by) : null,
            record.Description.Length == 0 ? null : record.DescriptionUrl,
            record.Description.Length == 0 ? null : record.DescriptionAt,
            counts,
            AppealReads.Threshold(record, counts, threshold),
            ordered,
            record.Status == BillRecordStatuses.Open ? await reads.SurgeAsync(db, record.Id, ct) : null,
            record.ClosingVoteId,
            record.ClosedAt,
            record.Status == BillRecordStatuses.Closed ? null : (await agenda.ForBillsAsync([record.BillNumber], ct)).GetValueOrDefault(record.BillNumber),
            null,
            profiles.Values.OrderBy(p => p.ProfileId, StringComparer.Ordinal).Select(p => new AuthorCard(p.ProfileId, p.DisplayName, p.About)).ToList());
    }

    public async Task<IReadOnlyDictionary<string, BillRecordState>> BillStatesAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken)
    {
        var numbers = billNumbers.Select(AppealReads.NormaliseBillNumber).Distinct().ToList();
        if (numbers.Count == 0)
        {
            return new Dictionary<string, BillRecordState>(StringComparer.Ordinal);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var known = await reads.BillsAsync(numbers, cancellationToken);
        var recordsOnBills = await db.BillRecords.AsNoTracking().Records()
            .Where(a => numbers.Contains(a.BillNumber))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, cancellationToken);
        var shownCounts = await reads.ShownCountsAsync(db, recordsOnBills.Values.Where(a => a.Status != BillRecordStatuses.Rejected).ToList(), cancellationToken);
        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        return numbers.Where(known.ContainsKey).ToDictionary(
            n => n,
            n =>
            {
                if (recordsOnBills.GetValueOrDefault(n) is not { } record)
                {
                    return new BillRecordState(n, null, BillRecordStatuses.Open, SideCounts.None, AppealReads.Threshold(null, SideCounts.None, threshold));
                }

                // A hidden record shows no numbers (design 8.12.3).
                var shown = shownCounts.GetValueOrDefault(record.Id) ?? SideCounts.None;
                return new BillRecordState(n, record.Id, record.Status, shown, AppealReads.Threshold(record, shown, threshold));
            },
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, string>> MySidesAsync(Guid userId, IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken)
    {
        var numbers = billNumbers.Select(AppealReads.NormaliseBillNumber).Distinct().ToList();
        if (numbers.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (from p in db.Positions.AsNoTracking()
                      join r in db.BillRecords.AsNoTracking().Records() on p.RecordId equals r.Id
                      where p.UserId == userId && numbers.Contains(r.BillNumber) && r.Status != BillRecordStatuses.Rejected
                      select new { r.BillNumber, p.Side })
            .ToDictionaryAsync(x => x.BillNumber, x => x.Side, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<IReadOnlyList<ClosedRecordFacts>> ClosedRecordsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var closed = await db.BillRecords.AsNoTracking().Records()
            .Where(r => r.Status == BillRecordStatuses.Closed && r.ClosingVoteId != null && r.ClosedAt != null)
            .OrderByDescending(r => r.ClosedAt).ThenBy(r => r.BillNumber)
            .ToListAsync(cancellationToken);
        var counts = await reads.ShownCountsAsync(db, closed, cancellationToken);
        return closed.Select(r => new ClosedRecordFacts(r.BillNumber, r.ClosingVoteId!.Value, r.ClosedAt!.Value, counts[r.Id])).ToList();
    }

    public async Task<string> ClosedRecordsVersionAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var closed = db.BillRecords.AsNoTracking().Records().Where(r => r.Status == BillRecordStatuses.Closed);
        var count = await closed.CountAsync(cancellationToken);
        var last = await closed.MaxAsync(r => (DateTimeOffset?)r.ChangedAt, cancellationToken);
        return count.ToString(CultureInfo.InvariantCulture) + "/" + (last?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-");
    }

    public async Task<IReadOnlyList<MyClosedSide>> MyClosedSidesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (from p in db.Positions.AsNoTracking()
                      join r in db.BillRecords.AsNoTracking().Records() on p.RecordId equals r.Id
                      where p.UserId == userId && r.Status == BillRecordStatuses.Closed && r.ClosingVoteId != null
                      orderby r.BillNumber
                      select new MyClosedSide(r.BillNumber, r.ClosingVoteId!.Value, p.Side))
            .ToListAsync(cancellationToken);
    }

    internal static Revision Revise(string kind, Guid id, string field, string? oldValue, string? newValue, Guid? by, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        EntityKind = kind,
        EntityId = id,
        Field = field,
        OldValue = oldValue,
        NewValue = newValue,
        ChangedByUserId = by,
        ChangedAt = at,
    };

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? FirstBroken(params string?[] codes) => codes.FirstOrDefault(c => c is not null);
}

/// <summary>
/// Grounds for a report (design 8.3, Digital Services Act notice and action). A bill's description may also be reported as
/// inaccurate ("Pranešti apie klaidą", design 8.13); an argument may not: it is its author's own opinion.
/// </summary>
internal static class ReportGrounds
{
    public const int TextMaxLength = 1000;

    public const string Inaccurate = "inaccurate";

    public static readonly IReadOnlyList<string> All = ["illegal", "personal-data", "insult", "spam", "other"];

    public static readonly IReadOnlyList<string> Description = [Inaccurate, .. All];

    /// <summary>"Apsimeta kita organizacija" (design 8.14.9): an organisation or its statement may be reported as impersonation.</summary>
    public const string Impersonation = "impersonation";

    public static readonly IReadOnlyList<string> Organisation = [Impersonation, .. All];

    /// <summary>The grounds a report on this target may give.</summary>
    public static IReadOnlyList<string> For(string targetKind) => targetKind switch
    {
        "record" => Description,
        "organisation" or "statement" => Organisation,
        _ => All,
    };
}
