using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// The decision on a report, to the member who made it (owner, 2026-11-17; Digital Services Act art. 16(5)): when a moderator's
/// action answers the open reports on a target, or its author withdraws it, or it goes with the text it stood under, every member
/// whose report on it was open gets one email through the outbox, in the action's transaction. It names the text by its kind and
/// bill (or an organisation's public name), what became of it, the moderator's explanation when there is one and the link while
/// the text still stands; never the moderator, the other reporters or more of the author than is public. Reports closed because
/// the reporter's or the author's account was deleted send nothing (those paths close them without this).
/// </summary>
internal sealed class ReportNotices(IParticipants participants, IOutbox outbox, AppealReads reads)
{
    /// <summary>Tells the reporters of every open report on <paramref name="targetId"/>; call it before the reports are closed.</summary>
    public async Task TellAsync(AppealsDb db, IDbContextTransaction transaction, Guid targetId, string outcome, string? statement, CancellationToken ct)
    {
        var reports = await db.Reports.AsNoTracking().Where(r => r.TargetId == targetId && r.Status == "open").ToListAsync(ct);
        if (reports.Count == 0)
        {
            return;
        }

        var kind = reports[0].TargetKind;
        var place = await PlaceAsync(db, kind, targetId, ct);
        var bill = place.BillNumber is { } number ? await reads.BillAsync(number, ct) : null;
        var path = ReportOutcomes.StillShown(outcome) ? place.Path : null;
        var explanation = string.IsNullOrWhiteSpace(statement) ? null : statement.Trim();
        foreach (var reporter in reports.Select(r => r.ReporterUserId).Distinct())
        {
            if (await participants.FindAsync(reporter, ct) is not { } person)
            {
                continue;
            }

            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.ReportDecidedEmail, new ReportDecidedEmail(person.Email, kind, outcome, bill?.Number, bill?.Title, place.Name, path, explanation)),
                transaction.GetDbTransaction(),
                ct);
        }
    }

    /// <summary>The comments under the given texts went with them (withdrawn or merged): their reporters are told.</summary>
    public async Task TellForCommentsUnderAsync(AppealsDb db, IDbContextTransaction transaction, string kind, IReadOnlyCollection<Guid> targetIds, CancellationToken ct)
    {
        var ids = targetIds.ToList();
        var reported = await db.Comments.AsNoTracking()
            .Where(c => c.TargetKind == kind && ids.Contains(c.TargetId) && db.Reports.Any(r => r.TargetId == c.Id && r.Status == "open"))
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var comment in reported)
        {
            await TellAsync(db, transaction, comment, ReportOutcomes.Gone, null, ct);
        }
    }

    /// <summary>Where the reported text is: its bill, an organisation's name, and the path to it.</summary>
    private static async Task<(string? BillNumber, string? Name, string? Path)> PlaceAsync(AppealsDb db, string kind, Guid id, CancellationToken ct)
    {
        switch (kind)
        {
            case ReportTargets.Reason:
                var reason = await (from r in db.Reasons.AsNoTracking()
                                    join a in db.BillRecords.AsNoTracking() on r.RecordId equals a.Id
                                    where r.Id == id
                                    select a.BillNumber).FirstOrDefaultAsync(ct);
                return (reason, null, reason is null ? null : BillTextLinks.ToReason(reason, id));
            case ReportTargets.Comment:
                var comment = await db.Comments.AsNoTracking().Where(c => c.Id == id).Select(c => c.BillNumber).FirstOrDefaultAsync(ct);
                return (comment, null, comment is null ? null : BillTextLinks.ToComment(comment, id));
            case ReportTargets.Statement:
                var statement = await db.OrganisationStatements.AsNoTracking().Where(s => s.Id == id).Select(s => s.BillNumber).FirstOrDefaultAsync(ct);
                return (statement, null, statement is null ? null : BillPath(statement));
            case ReportTargets.Response:
                var response = await db.MpResponses.AsNoTracking().Where(r => r.Id == id).Select(r => r.BillNumber).FirstOrDefaultAsync(ct);
                return (response, null, response is null ? null : BillPath(response));
            case ReportTargets.Record:
                var record = await db.BillRecords.AsNoTracking().Where(r => r.Id == id).Select(r => r.BillNumber).FirstOrDefaultAsync(ct);
                return (record, null, record is null ? null : BillPath(record));
            case ReportTargets.Organisation:
                var organisation = await db.Organisations.AsNoTracking().Where(o => o.Id == id).Select(o => new { o.Name, o.Slug }).FirstOrDefaultAsync(ct);
                return (null, organisation?.Name, organisation is null ? null : "/o/" + Uri.EscapeDataString(organisation.Slug));
            default:
                return (null, null, null);
        }
    }

    private static string BillPath(string number) => "/seimas/" + Uri.EscapeDataString(number);
}
