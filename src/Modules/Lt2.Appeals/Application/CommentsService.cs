using System.Text.Json;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// Comments (design 8.23): one service for the three kinds. A comment is public at once; pre-screening refuses personal data
/// instead of holding it; nothing waits and nothing is listed for moderators unless reported. The discussion follows its target
/// (8.23.7): a comment is shown only while its target is shown and the bill's record is not hidden, and is read-only once the
/// bill closes. Nothing here reads or writes a position, a count or a summary, and no log or answer names the person behind an
/// organisation's or an MP's comment.
/// </summary>
internal sealed class CommentsService(
    IDbContextFactory<AppealsDb> contexts,
    OrganisationReads bills,
    AppealReads reads,
    ModerationService moderation,
    ModerationRecorder recorder,
    ReportNotices notices,
    IParticipants participants,
    IPreScreening screening,
    IMpLinks links,
    IMpDirectory mps,
    IOutbox outbox,
    IAuditLog audit,
    ISettings settings,
    TimeProvider clock) : IComments, ICommentModeration, ICommentFeed
{
    private const string Kind = ReportTargets.Comment;
    private const int ListLimit = 100;

    public async Task<Result<CommentThread>> ThreadAsync(string kind, Guid targetId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (!CommentKinds.All.Contains(kind))
        {
            return Error.NotFound("comment.target-not-found");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await TargetAsync(db, kind, targetId, cancellationToken) is not { } target
            || await bills.BillAsync(db, target.BillNumber, cancellationToken) is not { } bill
            || !CommentRules.IsShown(CommentStatuses.Published, target.Shown, bill.Hidden))
        {
            return Error.NotFound("comment.target-not-found");
        }

        // One page in the database's order (oldest first, then by id), so the next page starts exactly where this one ends.
        var shown = db.Comments.AsNoTracking()
            .Where(c => c.TargetKind == kind && c.TargetId == targetId && c.Status == CommentStatuses.Published);
        var count = await shown.CountAsync(cancellationToken);
        var offset = CommentRules.Offset(cursor);
        var taken = await shown.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip(offset).Take(CommentRules.Limit(limit, offset))
            .ToListAsync(cancellationToken);
        var (views, cards) = await ViewsAsync(db, taken, cancellationToken);
        var on = await SwitchAsync(kind, cancellationToken);
        // Read-only on a closed bill and under a withdrawn response (design 8.23.7): no new comment, no "Sutinku", no deleting.
        return new CommentThread(
            kind, targetId, bill.Bill.Number, count, bill.Open && on && target.Writable, bill.Open && target.Writable, views, cards,
            CommentRules.NextCursor(offset, taken.Count, count));
    }

    public async Task<Result<CommentPlace>> PlaceAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var notFound = Error.NotFound("comment.not-found");
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken) is not { Status: CommentStatuses.Published } comment
            || await TargetAsync(db, comment.TargetKind, comment.TargetId, cancellationToken) is not { } target
            || await bills.BillAsync(db, target.BillNumber, cancellationToken) is not { } bill
            || !CommentRules.IsShown(comment.Status, target.Shown, bill.Hidden))
        {
            return notFound;
        }

        // Its place among the target's shown comments, oldest first, ordered by the database exactly as the thread is.
        var order = await db.Comments.AsNoTracking()
            .Where(c => c.TargetKind == comment.TargetKind && c.TargetId == comment.TargetId && c.Status == CommentStatuses.Published)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        return new CommentPlace(comment.Id, comment.TargetKind, comment.TargetId, bill.Bill.Number, order.IndexOf(comment.Id) + 1);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountsAsync(string kind, IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await CommentCascade.CountsAsync(db, kind, targetIds, cancellationToken);
    }

    public async Task<Result<MyComments>> MineAsync(Guid userId, string billNumber, CommentSpeaker? speaker, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await bills.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        var participant = await participants.FindAsync(userId, cancellationToken);
        var number = bill.Bill.Number;
        var onBill = await db.Comments.AsNoTracking().Where(c => c.BillNumber == number).ToListAsync(cancellationToken);
        var ids = onBill.Select(c => c.Id).ToList();
        var represented = await db.OrganisationRepresentatives.AsNoTracking()
            .Where(r => r.UserId == userId && r.EndedAt == null).Select(r => r.OrganisationId).ToListAsync(cancellationToken);
        var deletable = onBill.Where(c => c.Status == CommentStatuses.Published && CommentRules.MayDelete(
                c.SpeaksAs,
                c.AuthorUserId == userId,
                c.OrganisationId is { } o && represented.Contains(o),
                speaker is not null && c.SeimasPersonId == speaker.SeimasPersonId,
                speaker?.PasskeySession == true))
            .Select(c => c.Id).ToList();
        var agreed = await db.CommentAgreements.AsNoTracking().Where(a => a.UserId == userId && ids.Contains(a.CommentId)).Select(a => a.CommentId).ToListAsync(cancellationToken);
        var agreedBefore = agreed.Count > 0 || await db.CommentAgreements.AsNoTracking().AnyAsync(a => a.UserId == userId, cancellationToken);

        // The voice where it is not a person's (design 8.23.2): the statements of organisations the caller represents, and the
        // responses of the MP the caller speaks for.
        var voices = new List<CommentTargetVoice>();
        if (represented.Count > 0)
        {
            var statements = await db.OrganisationStatements.AsNoTracking()
                .Where(s => s.BillNumber == number && represented.Contains(s.OrganisationId)).Select(s => s.Id).ToListAsync(cancellationToken);
            voices.AddRange(statements.Select(s => new CommentTargetVoice(CommentKinds.Statement, s, CommentVoices.Organisation, null)));
        }

        if (speaker is not null)
        {
            var responses = await db.MpResponses.AsNoTracking()
                .Where(r => r.BillNumber == number && r.SeimasPersonId == speaker.SeimasPersonId).Select(r => r.Id).ToListAsync(cancellationToken);
            var refusal = CommentRules.CheckMpVoice(speaker.PasskeySession, speaker.Sitting);
            voices.AddRange(responses.Select(r => new CommentTargetVoice(CommentKinds.Response, r, CommentVoices.Mp, refusal)));
        }

        var written = onBill.Where(c => c.AuthorUserId == userId).ToList();
        var restricted = await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), cancellationToken);
        return new MyComments(
            agreedBefore,
            restricted,
            participant?.IsMpAccount == true,
            deletable,
            written.Select(c => c.Id).ToList(),
            agreed,
            voices,
            await settings.GetAsync(SettingKeys.CommentsPerPersonPerTarget, cancellationToken),
            written.Where(c => c.Status == CommentStatuses.Published).GroupBy(c => c.TargetId).ToDictionary(g => g.Key, g => g.Count()))
        {
            Editable = restricted || participant is null || !bill.Open ? [] : await EditableAsync(db, written, participant, represented, speaker, cancellationToken),
            Own = onBill.Where(c => NewsRules.OwnComment(c.AuthorUserId, c.OrganisationId, c.SeimasPersonId, userId, represented.ToHashSet(), speaker?.SeimasPersonId))
                .Select(c => c.Id).ToList(),
            Hidden = await HiddenOfAsync(db, written, cancellationToken),
        };
    }

    /// <summary>
    /// The caller's own hidden comments with the moderator's statement (owner, 2026-11-17), for their own answer only. A comment
    /// shown nowhere because its target or the bill's record was hidden is not one of them: it follows its target (8.23.7).
    /// </summary>
    private static async Task<IReadOnlyList<MyHiddenComment>> HiddenOfAsync(AppealsDb db, List<Comment> written, CancellationToken ct)
    {
        var hidden = written.Where(c => c.Status == CommentStatuses.Hidden).OrderBy(c => c.CreatedAt).ToList();
        var statements = await AppealsService.HideStatementsAsync(db, hidden.Select(c => c.Id).ToList(), ct);
        return hidden.Select(c => new MyHiddenComment(c.Id, c.TargetKind, c.TargetId, c.Text, c.CreatedAt, statements.GetValueOrDefault(c.Id))).ToList();
    }

    /// <summary>
    /// The caller's own comments they may edit now (design 8.23.3, owner 2026-11-17), by the rules the edit is checked by: nobody
    /// said "Sutinku", the target open to comments and its kind switched on, the same voice as when written.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> EditableAsync(
        AppealsDb db, List<Comment> written, ParticipantFacts participant, List<Guid> represented, CommentSpeaker? speaker, CancellationToken ct)
    {
        var candidates = written.Where(c => c.Status == CommentStatuses.Published).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var ids = candidates.Select(c => c.Id).ToList();
        var agreements = await db.CommentAgreements.AsNoTracking().Where(a => ids.Contains(a.CommentId))
            .GroupBy(a => a.CommentId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var editable = new List<Guid>();
        var targets = new Dictionary<Guid, TargetState?>();
        var switches = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            if (CommentRules.CheckEdit(c.Status, isAuthor: true, agreements.GetValueOrDefault(c.Id)) is not null)
            {
                continue;
            }

            if (!targets.TryGetValue(c.TargetId, out var target))
            {
                targets[c.TargetId] = target = await TargetAsync(db, c.TargetKind, c.TargetId, ct);
            }

            if (!switches.TryGetValue(c.TargetKind, out var on))
            {
                switches[c.TargetKind] = on = await SwitchAsync(c.TargetKind, ct);
            }

            if (target is not { Shown: true, Writable: true } || !on)
            {
                continue;
            }

            var represents = target.OrganisationId is { } o && represented.Contains(o);
            var (voice, refusal) = CommentRules.Voice(c.TargetKind, represents, target.SeimasPersonId, speaker?.SeimasPersonId, participant.IsMpAccount);
            if (refusal is null && voice == c.SpeaksAs && (voice != CommentVoices.Mp || CommentRules.CheckMpVoice(speaker!.PasskeySession, speaker.Sitting) is null))
            {
                editable.Add(c.Id);
            }
        }

        return editable;
    }

    public async Task<Result<CommentCreated>> WriteAsync(Guid userId, string kind, Guid targetId, string text, CommentSpeaker? speaker, CancellationToken cancellationToken)
    {
        if (!CommentKinds.All.Contains(kind))
        {
            return Error.NotFound("comment.target-not-found");
        }

        var (normalised, textRefused) = await CheckTextAsync(text, cancellationToken);
        if (textRefused is not null)
        {
            return textRefused;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await participants.FindAsync(userId, cancellationToken) is not { } participant)
        {
            return Error.Forbidden("member.required");
        }

        if (await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), cancellationToken))
        {
            return Error.Forbidden("writing.restricted");
        }

        if (await TargetAsync(db, kind, targetId, cancellationToken) is not { Shown: true } target
            || await bills.BillAsync(db, target.BillNumber, cancellationToken) is not { Hidden: false } bill)
        {
            return Error.NotFound("comment.target-not-found");
        }

        if (CommentRules.CheckSwitch(await SwitchAsync(kind, cancellationToken)) is { } off)
        {
            return Error.Conflict(off);
        }

        if (CommentRules.CheckBillOpen(bill.Record?.Status, bill.Finished) is { } closed)
        {
            return Error.Conflict(closed);
        }

        if (!target.Writable)
        {
            return Error.Conflict("comment.target-closed");
        }

        var represents = target.OrganisationId is { } organisation && await OrganisationReads.IsRepresentativeAsync(db, organisation, userId, cancellationToken);
        var (voice, refusal) = CommentRules.Voice(kind, represents, target.SeimasPersonId, speaker?.SeimasPersonId, participant.IsMpAccount);
        if (refusal is not null)
        {
            return Error.Forbidden(refusal);
        }

        if (voice == CommentVoices.Mp && CommentRules.CheckMpVoice(speaker!.PasskeySession, speaker.Sitting) is { } mpRefusal)
        {
            return mpRefusal == "mp.passkey-required" ? Error.Forbidden(mpRefusal) : Error.Conflict(mpRefusal);
        }

        // Design 8.23.4: the ceiling per person per text, counted under the account's and the target's lock.
        await AdvisoryLocks.CommentsAsync(db, userId, targetId, cancellationToken);

        // Under an argument's lock, which its author's edit takes: a comment and an edit never cross (design 8.2).
        if (kind == CommentKinds.Reason)
        {
            await AdvisoryLocks.ReasonAsync(db, targetId, cancellationToken);
        }

        var limit = await settings.GetAsync(SettingKeys.CommentsPerPersonPerTarget, cancellationToken);
        if (limit > 0)
        {
            var shownByPerson = await db.Comments.CountAsync(c => c.TargetId == targetId && c.AuthorUserId == userId && c.Status == CommentStatuses.Published, cancellationToken);
            if (CommentRules.CheckCeiling(shownByPerson, limit) is { } full)
            {
                return Error.Conflict(full) with { Numbers = [limit] };
            }
        }

        var comment = new Comment
        {
            Id = Ids.New(clock),
            TargetKind = kind,
            TargetId = targetId,
            BillNumber = bill.Bill.Number,
            SpeaksAs = voice!,
            OrganisationId = voice == CommentVoices.Organisation ? target.OrganisationId : null,
            SeimasPersonId = voice == CommentVoices.Mp ? target.SeimasPersonId : null,
            AuthorUserId = userId,
            Text = normalised,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        // Design 8.12.5: a comment written is the bill's activity.
        await RecordActivity.TouchBillAsync(db, comment.BillNumber, comment.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CommentCreated(comment.Id);
    }

    /// <summary>
    /// A comment's text against the rules of writing it, checked here for every caller: the length is the members' texts setting
    /// (design 5.6, 6.8, owner 2026-11-16), and personal data refuses it with a message naming what was found, a link does not
    /// (8.23.3). The normalised text, or the refusal.
    /// </summary>
    private async Task<(string Text, Error? Refusal)> CheckTextAsync(string text, CancellationToken ct)
    {
        var maxLength = await settings.GetAsync(SettingKeys.TextsMaxLength, ct);
        if (CommentRules.CheckText(text, maxLength) is { } broken)
        {
            return ("", broken == "comment.too-long"
                ? Error.Validation(broken, "text") with { Numbers = [MemberTextLength.Allowed(maxLength)] }
                : Error.Validation(broken, "text"));
        }

        var normalised = AppealRules.NormaliseText(text);
        var refused = CommentRules.RefusedBy(await screening.ScreenAsync(normalised, ct));
        return refused.Count > 0
            ? ("", Error.Validation("comment.screened", "text") with { MessageKey = "comment.screened." + (refused.Count == 1 ? refused[0] : "several") })
            : (normalised, null);
    }

    /// <summary>
    /// The author edits their comment (design 8.23.3, owner 2026-11-17) while nobody has said "Sutinku" to it
    /// (<see cref="CommentRules.CheckEdit"/>), under the comment's lock, which "Sutinku" takes too, and by the rules of writing
    /// it: the bill open, the target open to comments, the kind switched on, no writing restriction, the same voice as when it was
    /// written (a representative still of the organisation, the MP's account or office in a passkey session, the mandate not
    /// ended), the length and pre-screening. No earlier text is kept; the audit says that an edit happened, never the text.
    /// </summary>
    public async Task<Result<Unit>> EditAsync(Guid userId, Guid commentId, string text, CommentSpeaker? speaker, CancellationToken cancellationToken)
    {
        var (normalised, textRefused) = await CheckTextAsync(text, cancellationToken);
        if (textRefused is not null)
        {
            return textRefused;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.CommentAsync(db, commentId, cancellationToken);
        if (await participants.FindAsync(userId, cancellationToken) is not { } participant)
        {
            return Error.Forbidden("member.required");
        }

        if (await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken) is not { } comment)
        {
            return Error.NotFound("comment.not-found");
        }

        switch (CommentRules.CheckEdit(comment.Status, comment.AuthorUserId == userId, agreements: 0))
        {
            case "comment.hidden":
                return Error.Conflict("comment.hidden") with { MessageKey = "comment.hidden.edit" };
            case { } notAuthor:
                return Error.Forbidden(notAuthor);
        }

        if (await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), cancellationToken))
        {
            return Error.Forbidden("writing.restricted");
        }

        var bill = await bills.BillAsync(db, comment.BillNumber, cancellationToken);
        if (CommentRules.CheckBillOpen(bill?.Record?.Status, bill?.Finished ?? true) is { } closed)
        {
            return Error.Conflict(closed);
        }

        if (await TargetAsync(db, comment.TargetKind, comment.TargetId, cancellationToken) is not { Shown: true, Writable: true } target)
        {
            return Error.Conflict("comment.read-only");
        }

        if (CommentRules.CheckSwitch(await SwitchAsync(comment.TargetKind, cancellationToken)) is { } off)
        {
            return Error.Conflict(off);
        }

        if (await VoiceRefusalAsync(db, comment, target, participant, speaker, cancellationToken) is { } voiceRefused)
        {
            return voiceRefused;
        }

        var agreements = await db.CommentAgreements.CountAsync(a => a.CommentId == commentId, cancellationToken);
        if (CommentRules.CheckEdit(comment.Status, isAuthor: true, agreements) is { } reacted)
        {
            return Error.Conflict(reacted);
        }

        var now = clock.GetUtcNow();
        comment.Text = normalised;
        comment.EditedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        // Design 8.12.5: a comment changed in public is the bill's activity.
        await RecordActivity.TouchBillAsync(db, comment.BillNumber, now, cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(
                "comment.edited", userId, "member", "comment:" + commentId,
                JsonSerializer.SerializeToElement(new { bill = comment.BillNumber, kind = comment.TargetKind, voice = comment.SpeaksAs }, JsonSerializerOptions.Web),
                null),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The author still writes in the comment's voice (design 8.23.2), as writing it under its target now would decide: otherwise
    /// the refusal of writing (a former representative, an MP's account in a code session, a mandate ended).
    /// </summary>
    private static async Task<Error?> VoiceRefusalAsync(AppealsDb db, Comment comment, TargetState target, ParticipantFacts participant, CommentSpeaker? speaker, CancellationToken ct)
    {
        var represents = target.OrganisationId is { } organisation && await OrganisationReads.IsRepresentativeAsync(db, organisation, comment.AuthorUserId!.Value, ct);
        var (voice, refusal) = CommentRules.Voice(comment.TargetKind, represents, target.SeimasPersonId, speaker?.SeimasPersonId, participant.IsMpAccount);
        if (refusal is not null)
        {
            return Error.Forbidden(refusal);
        }

        if (voice != comment.SpeaksAs)
        {
            return Error.Forbidden("comment.edit-author-only");
        }

        return voice == CommentVoices.Mp && CommentRules.CheckMpVoice(speaker!.PasskeySession, speaker.Sitting) is { } mpRefusal
            ? mpRefusal == "mp.passkey-required" ? Error.Forbidden(mpRefusal) : Error.Conflict(mpRefusal)
            : null;
    }

    public async Task<Result<Unit>> DeleteAsync(Guid userId, Guid commentId, CommentSpeaker? speaker, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.CommentAsync(db, commentId, cancellationToken);
        if (await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken) is not { } comment)
        {
            return Error.NotFound("comment.not-found");
        }

        var represents = comment.OrganisationId is { } organisation && await OrganisationReads.IsRepresentativeAsync(db, organisation, userId, cancellationToken);
        if (!CommentRules.MayDelete(comment.SpeaksAs, comment.AuthorUserId == userId, represents, speaker?.SeimasPersonId == comment.SeimasPersonId && speaker is not null, speaker?.PasskeySession == true))
        {
            return comment.SpeaksAs == CommentVoices.Mp && speaker is { PasskeySession: false } && speaker.SeimasPersonId == comment.SeimasPersonId
                ? Error.Forbidden("mp.passkey-required")
                : Error.Forbidden("comment.author-only");
        }

        // A hidden comment is the moderators' to restore, as a hidden argument is (design 8.3): its author cannot delete it.
        if (comment.Status != CommentStatuses.Published)
        {
            return Error.Conflict("comment.hidden");
        }

        // On a closed bill, and under a withdrawn response, nobody deletes (design 8.23.7): the discussion is read-only.
        var bill = await bills.BillAsync(db, comment.BillNumber, cancellationToken);
        if (CommentRules.CheckBillOpen(bill?.Record?.Status, bill?.Finished ?? true) is { } closed)
        {
            return Error.Conflict(closed);
        }

        if (await TargetAsync(db, comment.TargetKind, comment.TargetId, cancellationToken) is { Writable: false })
        {
            return Error.Conflict("comment.read-only");
        }

        var now = clock.GetUtcNow();
        // Owner, 2026-11-17: whoever reported it is told it was deleted by its writer.
        await notices.TellAsync(db, transaction, comment.Id, ReportOutcomes.Withdrawn, null, cancellationToken);
        await ModerationRecorder.CloseReportsAsync(db, comment.Id, now, cancellationToken);
        db.Comments.Remove(comment);
        await db.SaveChangesAsync(cancellationToken);

        // Audited as an argument's withdrawal (design 8.23.3): the bill and the kind, never the text or the voice's person.
        await audit.WriteAsync(
            new AuditEntry(
                "comment.deleted", userId, "member", "comment:" + commentId,
                JsonSerializer.SerializeToElement(new { bill = comment.BillNumber, kind = comment.TargetKind, voice = comment.SpeaksAs }, JsonSerializerOptions.Web),
                null),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> AgreeAsync(Guid userId, Guid commentId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Under the comment's lock, which hiding, restoring and deleting take: a mark never outlives a hide or a deletion.
        await AdvisoryLocks.CommentAsync(db, commentId, cancellationToken);
        if (await ShownCommentAsync(db, commentId, cancellationToken) is not { } shown)
        {
            return Error.NotFound("comment.not-found");
        }

        var (comment, bill, target) = shown;
        if (!target.Writable)
        {
            return Error.Conflict("comment.read-only");
        }

        var participant = await participants.FindAsync(userId, cancellationToken);
        if (participant is null)
        {
            return Error.Forbidden("member.required");
        }

        // Design 8.23.10: a public mark under the person's name; no positions consent, not blocked by a writing restriction.
        switch (CommentRules.CheckAgreement(comment.AuthorUserId == userId, participant.IsMpAccount, bill.Open))
        {
            case "mp.takes-no-part":
                return MpAccounts.TakesNoPart;
            case { } refused:
                return Error.Conflict(refused);
        }

        if (await db.CommentAgreements.AnyAsync(a => a.CommentId == commentId && a.UserId == userId, cancellationToken))
        {
            return Result.Ok();
        }

        db.CommentAgreements.Add(new CommentAgreement { CommentId = commentId, UserId = userId, CreatedAt = clock.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Pressed twice at once: one mark stands.
            return Result.Ok();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation })
        {
            // The comment went meanwhile (its target withdrawn, the account deleted): nothing to agree with.
            return Error.NotFound("comment.not-found");
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> RemoveAgreementAsync(Guid userId, Guid commentId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.CommentAsync(db, commentId, cancellationToken);
        if (await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken) is not { } comment)
        {
            return Error.NotFound("comment.not-found");
        }

        // Frozen when the bill closes, and under a withdrawn response (design 8.23.7, 8.23.10): nothing given or taken back.
        var bill = await bills.BillAsync(db, comment.BillNumber, cancellationToken);
        if (CommentRules.CheckBillOpen(bill?.Record?.Status, bill?.Finished ?? true) is { } closed)
        {
            return Error.Conflict(closed);
        }

        if (await TargetAsync(db, comment.TargetKind, comment.TargetId, cancellationToken) is { Writable: false })
        {
            return Error.Conflict("comment.read-only");
        }

        await db.CommentAgreements.Where(a => a.CommentId == commentId && a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<CommentModerationCounts> CountsAsync(CancellationToken cancellationToken)
    {
        // Design 8.3: each tab's count by its own query, with the filter its list uses (the same builders).
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return new CommentModerationCounts(await ReportedComments(db).CountAsync(cancellationToken), await HiddenComments(db).CountAsync(cancellationToken));
    }

    private static IQueryable<Report> OpenCommentReports(AppealsDb db) => db.Reports.AsNoTracking().Where(r => r.Status == "open" && r.TargetKind == Kind);

    /// <summary>The reported comments of the moderators' list (design 8.23.8): published, with an open report. The one filter of the list and of its count.</summary>
    private static IQueryable<Comment> ReportedComments(AppealsDb db)
    {
        var reported = OpenCommentReports(db).Select(r => r.TargetId);
        return db.Comments.AsNoTracking().Where(c => reported.Contains(c.Id) && c.Status == CommentStatuses.Published);
    }

    private static IQueryable<Comment> HiddenComments(AppealsDb db) => db.Comments.AsNoTracking().Where(c => c.Status == CommentStatuses.Hidden);

    public async Task<CommentModerationItems> ItemsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var reports = await OpenCommentReports(db).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var dueReported = ReportedComments(db);
        var reported = await dueReported.OrderBy(c => c.CreatedAt).Take(ListLimit).ToListAsync(cancellationToken);
        var allHidden = HiddenComments(db);
        var hidden = await allHidden.OrderByDescending(c => c.HiddenAt).Take(ListLimit).ToListAsync(cancellationToken);

        // Owner, 2026-11-18: the lists still show the first hundred, and say so when there are more.
        return new CommentModerationItems(await QueueAsync(db, reported, reports, cancellationToken), await QueueAsync(db, hidden, reports, cancellationToken))
        {
            ReportedTotal = reported.Count < ListLimit ? reported.Count : await dueReported.CountAsync(cancellationToken),
            HiddenTotal = hidden.Count < ListLimit ? hidden.Count : await allHidden.CountAsync(cancellationToken),
        };
    }

    public async Task<Result<IReadOnlyList<QueueComment>>> OnBillAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await bills.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        var rows = await db.Comments.AsNoTracking().Where(c => c.BillNumber == bill.Bill.Number && c.Status == CommentStatuses.Published)
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var ids = rows.Select(c => c.Id).ToList();
        var reports = await db.Reports.AsNoTracking().Where(r => r.Status == "open" && r.TargetKind == Kind && ids.Contains(r.TargetId)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<QueueComment>>.Success(await QueueAsync(db, rows, reports, cancellationToken));
    }

    public Task<Result<Unit>> HideAsync(Guid commentId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? ActAsync(commentId, moderator, "hide", statement.Trim(), contact, async (db, c, now) =>
        {
            if (!CommentRules.MayHide(c.Status))
            {
                return (false, null);
            }

            // Design 8.23.8: its "Sutinku" with the names goes, as hiding an argument deletes its "Man svarbu".
            await db.CommentAgreements.Where(a => a.CommentId == c.Id).ExecuteDeleteAsync(cancellationToken);
            c.Status = CommentStatuses.Hidden;
            c.HiddenAt = now;
            return (true, "comment-hidden");
        }, cancellationToken);

    public Task<Result<Unit>> RestoreAsync(Guid commentId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? ActAsync(commentId, moderator, "restore", statement.Trim(), contact, (_, c, _) =>
        {
            if (!CommentRules.MayRestore(c.Status))
            {
                return Task.FromResult<(bool, string?)>((false, null));
            }

            c.Status = CommentStatuses.Published;
            c.HiddenAt = null;
            return Task.FromResult<(bool, string?)>((true, "comment-restored"));
        }, cancellationToken);

    public Task<Result<Unit>> KeepAsync(Guid commentId, string? note, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(note, required: false) ?? ActAsync(commentId, moderator, "keep", StatementRules.Normalise(note), null, (_, c, _) =>
            Task.FromResult<(bool, string?)>((CommentRules.MayKeep(c.Status), null)), cancellationToken);

    public Task<Result<AuthorView>> WriterAsync(Guid commentId, CancellationToken cancellationToken) =>
        moderation.ViewAsync(db => db.Comments.Where(c => c.Id == commentId).Select(c => c.AuthorUserId), "comment.not-found", cancellationToken);

    public Task<Result<Guid>> RestrictWriterAsync(Guid commentId, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        moderation.RestrictAsync(db => db.Comments.Where(c => c.Id == commentId).Select(c => c.AuthorUserId), "comment.not-found", until, statement, moderator, contact, cancellationToken);

    public async Task<IReadOnlyList<CommentedArgument>> CommentedArgumentsAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from c in db.Comments.AsNoTracking()
                          join r in db.Reasons.AsNoTracking() on c.TargetId equals r.Id
                          join b in db.BillRecords.AsNoTracking() on r.RecordId equals b.Id
                          where c.TargetKind == CommentKinds.Reason && c.Status == CommentStatuses.Published && c.CreatedAt >= since && c.CreatedAt < until
                              && r.Status == ReasonStatuses.Published && (b.Status == BillRecordStatuses.Open || b.Status == BillRecordStatuses.Closed)
                              && (c.AuthorUserId == null || c.AuthorUserId != r.AuthorUserId)
                          select new { r.AuthorUserId, ReasonId = r.Id, b.BillNumber })
            .Distinct()
            .ToListAsync(cancellationToken);
        return rows.Select(r => new CommentedArgument(r.AuthorUserId, r.ReasonId, r.BillNumber)).ToList();
    }

    public async Task<IReadOnlyList<CommentedArgumentAt>> CommentTimesAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from c in db.Comments.AsNoTracking()
                          join r in db.Reasons.AsNoTracking() on c.TargetId equals r.Id
                          join b in db.BillRecords.AsNoTracking() on r.RecordId equals b.Id
                          where c.TargetKind == CommentKinds.Reason && c.Status == CommentStatuses.Published && c.CreatedAt >= since && c.CreatedAt < until
                              && r.Status == ReasonStatuses.Published && (b.Status == BillRecordStatuses.Open || b.Status == BillRecordStatuses.Closed)
                              && (c.AuthorUserId == null || c.AuthorUserId != r.AuthorUserId)
                          select new { r.AuthorUserId, ReasonId = r.Id, b.BillNumber, c.CreatedAt, CommentId = c.Id })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new CommentedArgumentAt(r.AuthorUserId, r.ReasonId, r.BillNumber, r.CreatedAt, r.CommentId)).ToList();
    }

    /// <summary>A comment that is published with its target shown, and its bill (not hidden); null otherwise.</summary>
    private async Task<(Comment Comment, BillForStatements Bill, TargetState Target)?> ShownCommentAsync(AppealsDb db, Guid commentId, CancellationToken ct)
    {
        if (await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commentId, ct) is not { } comment
            || await TargetAsync(db, comment.TargetKind, comment.TargetId, ct) is not { } target
            || await bills.BillAsync(db, comment.BillNumber, ct) is not { } bill
            || !CommentRules.IsShown(comment.Status, target.Shown, bill.Hidden))
        {
            return null;
        }

        return (comment, bill, target);
    }

    /// <summary>
    /// Whether a comment is shown on its bill's page (design 8.23.7, <see cref="CommentRules.IsShown"/>), for a report (design
    /// 8.23.8): published, its target shown, the bill's record not hidden.
    /// </summary>
    internal static async Task<bool> IsShownAsync(AppealsDb db, Guid commentId, CancellationToken ct)
    {
        if (await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commentId, ct) is not { } comment)
        {
            return false;
        }

        var target = await TargetAsync(db, comment.TargetKind, comment.TargetId, ct);
        var record = await AppealReads.FindRecordAsync(db, comment.BillNumber, tracking: false, ct);
        return CommentRules.IsShown(comment.Status, target?.Shown == true, record?.Status == BillRecordStatuses.Rejected);
    }

    /// <summary>
    /// Where a target stands for its comments (design 8.23.3, 8.23.7): shown (its comments are), open to new ones, the bill it is
    /// on, and for a statement or a response whose it is. Null for an unknown target.
    /// </summary>
    internal static async Task<TargetState?> TargetAsync(AppealsDb db, string kind, Guid targetId, CancellationToken ct)
    {
        switch (kind)
        {
            case CommentKinds.Reason:
                var reason = await db.Reasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == targetId, ct);
                if (reason is null)
                {
                    return null;
                }

                var record = await db.BillRecords.AsNoTracking().FirstAsync(a => a.Id == reason.RecordId, ct);
                var reasonShown = reason.Status == ReasonStatuses.Published && record.Status is BillRecordStatuses.Open or BillRecordStatuses.Closed;
                return new TargetState(record.BillNumber, reasonShown, reasonShown, null, null, reason.Text, reason.AuthorUserId);
            case CommentKinds.Statement:
                var statement = await db.OrganisationStatements.AsNoTracking().FirstOrDefaultAsync(s => s.Id == targetId, ct);
                if (statement is null)
                {
                    return null;
                }

                var statementShown = await db.OrganisationStatements.AsNoTracking().Shown(db.Organisations).AnyAsync(s => s.Id == targetId, ct);
                return new TargetState(statement.BillNumber, statementShown, statementShown, statement.OrganisationId, null, statement.Text, null);
            case CommentKinds.Response:
                var response = await db.MpResponses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == targetId, ct);
                if (response is null)
                {
                    return null;
                }

                // A withdrawn response stays shown with its discussion, read-only (design 8.23.7).
                var responseShown = response.Status is ResponseStatuses.Published or ResponseStatuses.Withdrawn;
                return new TargetState(response.BillNumber, responseShown, response.Status == ResponseStatuses.Published, null, response.SeimasPersonId, response.Status == ResponseStatuses.Published ? response.Text : null, null);
            default:
                return null;
        }
    }

    private Task<bool> SwitchAsync(string kind, CancellationToken ct) => kind switch
    {
        CommentKinds.Reason => settings.GetAsync(SettingKeys.CommentsOnReasons, ct),
        CommentKinds.Statement => settings.GetAsync(SettingKeys.CommentsOnStatements, ct),
        _ => settings.GetAsync(SettingKeys.CommentsOnResponses, ct),
    };

    /// <summary>
    /// The public views of the given comments, in their order, with who agreed (oldest agreement first), and one card per person
    /// with a public profile among the names (design 8.25.4): the authors in their own voice and those who agreed.
    /// </summary>
    private async Task<(IReadOnlyList<CommentView> Views, IReadOnlyList<AuthorCard> Cards)> ViewsAsync(AppealsDb db, IReadOnlyList<Comment> rows, CancellationToken ct)
    {
        var ids = rows.Select(c => c.Id).ToList();
        var agreements = ids.Count == 0
            ? []
            : await db.CommentAgreements.AsNoTracking().Where(a => ids.Contains(a.CommentId)).OrderBy(a => a.CreatedAt).ThenBy(a => a.UserId).ToListAsync(ct);
        var authors = await AuthorsAsync(db, rows, ct);
        var names = await reads.NamesAsync(agreements.Select(a => a.UserId).Distinct(), ct);
        var people = rows.Where(c => c.SpeaksAs == CommentVoices.Person && c.AuthorUserId is not null).Select(c => c.AuthorUserId!.Value).Concat(agreements.Select(a => a.UserId)).Distinct().ToList();
        var profiles = await reads.ProfilesAsync(people, ct);
        var byComment = agreements.ToLookup(a => a.CommentId);
        var views = rows.Select(c => new CommentView(
                c.Id,
                c.SpeaksAs == CommentVoices.Person && c.AuthorUserId is { } a ? authors[c.Id] with { ProfileId = profiles.GetValueOrDefault(a)?.ProfileId } : authors[c.Id],
                c.Text,
                c.CreatedAt,
                byComment[c.Id].Select(x => new CommentAgreer(names.GetValueOrDefault(x.UserId), profiles.GetValueOrDefault(x.UserId)?.ProfileId)).ToList()))
            .ToList();
        var cards = profiles.Values.OrderBy(p => p.ProfileId, StringComparer.Ordinal).Select(p => new AuthorCard(p.ProfileId, p.DisplayName, p.About)).ToList();
        return (views, cards);
    }

    /// <summary>The name each comment is shown under (design 8.23.2): never the person behind an organisation's or an MP's comment.</summary>
    private async Task<Dictionary<Guid, CommentAuthor>> AuthorsAsync(AppealsDb db, IReadOnlyCollection<Comment> rows, CancellationToken ct)
    {
        var people = await reads.NamesAsync(rows.Where(c => c.SpeaksAs == CommentVoices.Person && c.AuthorUserId is not null).Select(c => c.AuthorUserId!.Value).Distinct(), ct);
        var organisationIds = rows.Where(c => c.OrganisationId is not null).Select(c => c.OrganisationId!.Value).Distinct().ToList();
        var organisations = organisationIds.Count == 0
            ? []
            : await db.Organisations.AsNoTracking().Where(o => organisationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        return rows.ToDictionary(c => c.Id, c => c.SpeaksAs switch
        {
            CommentVoices.Organisation when c.OrganisationId is { } o && organisations.TryGetValue(o, out var org) =>
                new CommentAuthor(CommentVoices.Organisation, null, org.Name, org.Slug, OrganisationReads.Label(org), null),
            CommentVoices.Mp => new CommentAuthor(CommentVoices.Mp, null, null, null, null, c.SeimasPersonId),
            _ => new CommentAuthor(CommentVoices.Person, c.AuthorUserId is { } a ? people.GetValueOrDefault(a) : null, null, null, null, null),
        });
    }

    private async Task<IReadOnlyList<QueueComment>> QueueAsync(AppealsDb db, List<Comment> rows, List<Report> reports, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var authors = await AuthorsAsync(db, rows, ct);
        var billsOf = await bills.BillsAsync(db, rows.Select(c => c.BillNumber).Distinct().ToList(), ct);
        var ids = rows.Select(c => c.Id).ToList();
        var hides = (await db.ModerationActions.AsNoTracking().Where(m => m.TargetKind == Kind && ids.Contains(m.TargetId) && m.Action == "hide").ToListAsync(ct))
            .GroupBy(m => m.TargetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());
        var moderators = await reads.NamesAsync(hides.Values.Select(m => m.ModeratorUserId).Distinct(), ct);
        var result = new List<QueueComment>(rows.Count);
        foreach (var c in rows)
        {
            var target = await TargetAsync(db, c.TargetKind, c.TargetId, ct);
            var targetAuthor = target?.ReasonAuthorUserId is { } ra ? (await reads.NamesAsync([ra], ct)).GetValueOrDefault(ra) : null;
            var organisationName = target?.OrganisationId is { } o ? await db.Organisations.AsNoTracking().Where(x => x.Id == o).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
            var hide = c.Status == CommentStatuses.Hidden ? hides.GetValueOrDefault(c.Id) : null;
            result.Add(new QueueComment(
                c.Id,
                billsOf.GetValueOrDefault(c.BillNumber)?.Bill ?? new BillFacts(c.BillNumber, c.BillNumber),
                authors[c.Id],
                c.Text,
                c.CreatedAt,
                c.Status,
                reports.Where(r => r.TargetId == c.Id).Select(r => new QueueReport(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList(),
                new CommentContext(c.TargetKind, c.TargetId, target?.Text, targetAuthor, organisationName, target?.SeimasPersonId, target?.Shown == true),
                c.HiddenAt,
                hide is null ? null : moderators.GetValueOrDefault(hide.ModeratorUserId),
                hide?.Statement));
        }

        return result;
    }

    /// <summary>A moderator's statement checked where it is stored (design 6.8): the refusal, or null when it passes.</summary>
    private static Task<Result<Unit>>? Refused(string? statement, bool required) =>
        StatementRules.Check(statement, required) is { } broken ? Task.FromResult<Result<Unit>>(RuleErrors.Of(broken)) : null;

    private delegate Task<(bool Applied, string? Notice)> CommentAction(AppealsDb db, Comment comment, DateTimeOffset now);

    /// <summary>
    /// A moderator's action on a comment (design 8.23.8): applied, recorded (target <c>comment</c>, answering its open reports)
    /// and audited without the text, the author told in the same transaction. Works whatever the target's state (8.23.14).
    /// </summary>
    private async Task<Result<Unit>> ActAsync(Guid commentId, StaffActor moderator, string action, string? statement, string? contact, CommentAction apply, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLocks.CommentAsync(db, commentId, ct);
        if (await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct) is not { } comment)
        {
            return Error.NotFound("comment.not-found");
        }

        var now = clock.GetUtcNow();
        var (applied, notice) = await apply(db, comment, now);
        if (!applied)
        {
            return Error.Conflict("moderation.wrong-state");
        }

        await recorder.RecordAsync(db, transaction, Kind, comment.Id, action, statement, moderator, now, comment.BillNumber, new { kind = comment.TargetKind, voice = comment.SpeaksAs }, answersReports: true, ct);
        if (notice is not null)
        {
            await NotifyAsync(db, transaction, comment, notice, statement, contact, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// The statement of reasons for a hidden or restored comment (design 8.23.8): to the person; for a comment in an
    /// organisation's name to every current representative; in the MP's name to the MP's account's own address.
    /// </summary>
    private async Task NotifyAsync(AppealsDb db, IDbContextTransaction transaction, Comment comment, string notice, string? statement, string? contact, CancellationToken ct)
    {
        var bill = await reads.BillAsync(comment.BillNumber, ct);
        var path = "/seimas/" + Uri.EscapeDataString(bill.Number);
        var messages = new List<OutboxMessage>();
        switch (comment.SpeaksAs)
        {
            case CommentVoices.Organisation when comment.OrganisationId is { } organisationId:
                var name = await db.Organisations.AsNoTracking().Where(o => o.Id == organisationId).Select(o => o.Name).FirstAsync(ct);
                foreach (var address in await bills.RepresentativeAddressesAsync(db, organisationId, ct))
                {
                    messages.Add(OutboxMessage.Create(MessageKinds.OrganisationNoticeEmail, new OrganisationNoticeEmail(address, notice, name, path, statement, contact, bill.Number, bill.Title)));
                }

                break;
            case CommentVoices.Mp when comment.SeimasPersonId is { } person:
                if (await links.MpAccountAddressAsync(person, ct) is { } mpAddress)
                {
                    var mpName = (await mps.MpsAsync([person], ct)).TryGetValue(person, out var mp) ? mp.FirstName + " " + mp.LastName : "";
                    messages.Add(OutboxMessage.Create(MessageKinds.MpResponseNoticeEmail, new MpResponseNoticeEmail(mpAddress, notice, mpName, bill.Number, bill.Title, path, statement, contact)));
                }

                break;
            default:
                if (comment.AuthorUserId is { } author && await participants.FindAsync(author, ct) is { } person2)
                {
                    var excerpt = comment.Text.Length > 120 ? comment.Text[..120] + "…" : comment.Text;
                    messages.Add(OutboxMessage.Create(MessageKinds.ModerationNoticeEmail, new ModerationNoticeEmail(person2.Email, notice, bill.Number, bill.Title, statement, excerpt, path, contact)));
                }

                break;
        }

        foreach (var message in messages)
        {
            await outbox.EnqueueAsync(message, transaction.GetDbTransaction(), ct);
        }
    }

    /// <param name="Shown">The target is shown, so its comments are.</param>
    /// <param name="Writable">The target takes new comments (a withdrawn response does not).</param>
    internal sealed record TargetState(string BillNumber, bool Shown, bool Writable, Guid? OrganisationId, long? SeimasPersonId, string? Text, Guid? ReasonAuthorUserId);
}

/// <summary>
/// The discussion follows its target (design 8.23.7): when an argument is withdrawn, merged or removed with its author's account,
/// or a statement is withdrawn (also by closing its organisation), its comments are deleted with their "Sutinku", and their open
/// reports closed. Runs in the caller's transaction.
/// </summary>
internal static class CommentCascade
{
    /// <summary>How many comments each target shows (design 8.23.6, 8.23.14): published ones; a target without any is missing.</summary>
    public static async Task<Dictionary<Guid, int>> CountsAsync(AppealsDb db, string kind, IReadOnlyCollection<Guid> targetIds, CancellationToken ct)
    {
        var ids = targetIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Comments.AsNoTracking()
                .Where(c => c.TargetKind == kind && ids.Contains(c.TargetId) && c.Status == CommentStatuses.Published)
                .GroupBy(c => c.TargetId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public static async Task DeleteForTargetsAsync(AppealsDb db, string kind, IReadOnlyCollection<Guid> targetIds, DateTimeOffset now, CancellationToken ct)
    {
        if (targetIds.Count == 0)
        {
            return;
        }

        var ids = targetIds.ToList();
        var comments = db.Comments.Where(c => c.TargetKind == kind && ids.Contains(c.TargetId));
        await db.Reports.Where(r => r.Status == "open" && comments.Any(c => c.Id == r.TargetId))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "closed").SetProperty(r => r.ClosedAt, now), ct);
        await comments.ExecuteDeleteAsync(ct);
    }
}
