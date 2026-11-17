using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IBillNews"/> (design 8.11.8): the texts a bill's page shows that were made public or changed after a member's last
/// visit, by the rule of <see cref="NewsRules"/>: published arguments on a record that is open or closed, organisations'
/// statements shown on the page, published MPs' responses, and published comments under a shown target; nothing on a bill
/// whose record is hidden; never the member's own. The same reads serve the marks on one bill and the counts in the member's
/// lists, in a fixed number of queries whatever the number of bills. Nothing here is stored or logged.
/// </summary>
internal sealed class BillNewsReads(IDbContextFactory<AppealsDb> contexts, IMpLinks links) : IBillNews
{
    public async Task<BillNews> OnBillAsync(Guid userId, string billNumber, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        var items = await ReadAsync(userId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { [number] = since }, cancellationToken);
        return new BillNews(
            items.Where(i => i.Kind == CommentKinds.Reason).Select(i => i.Id).ToList(),
            items.Where(i => i.Kind == Kinds.Comment).Select(i => new NewComment(i.Id, i.TargetKind!, i.TargetId!.Value)).ToList(),
            items.Where(i => i.Kind == CommentKinds.Statement).Select(i => i.Id).ToList(),
            items.Where(i => i.Kind == CommentKinds.Response).Select(i => i.Id).ToList());
    }

    public async Task<IReadOnlyDictionary<string, BillNewsCounts>> CountsAsync(Guid userId, IReadOnlyDictionary<string, DateTimeOffset> since, CancellationToken cancellationToken)
    {
        var moments = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var (number, at) in since)
        {
            moments[AppealReads.NormaliseBillNumber(number)] = at;
        }

        var items = await ReadAsync(userId, moments, cancellationToken);
        return items.GroupBy(i => i.BillNumber, StringComparer.Ordinal).ToDictionary(
            g => g.Key,
            g => new BillNewsCounts(
                g.Count(i => i.Kind == CommentKinds.Reason),
                g.Count(i => i.Kind == Kinds.Comment),
                g.Count(i => i.Kind == CommentKinds.Statement),
                g.Count(i => i.Kind == CommentKinds.Response)),
            StringComparer.Ordinal);
    }

    /// <summary>The new texts on the bills, each since its own moment, the member's own left out.</summary>
    private async Task<List<Item>> ReadAsync(Guid userId, IReadOnlyDictionary<string, DateTimeOffset> since, CancellationToken ct)
    {
        if (since.Count == 0)
        {
            return [];
        }

        var numbers = since.Keys.ToList();
        var earliest = since.Values.Min();
        await using var db = await contexts.CreateDbContextAsync(ct);
        var hiddenBills = (await db.BillRecords.AsNoTracking().Records()
                .Where(a => numbers.Contains(a.BillNumber) && a.Status == BillRecordStatuses.Rejected)
                .Select(a => a.BillNumber).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var represented = (await db.OrganisationRepresentatives.AsNoTracking()
                .Where(r => r.UserId == userId && r.EndedAt == null).Select(r => r.OrganisationId).ToListAsync(ct))
            .ToHashSet();
        long? speaksFor = (await links.SpeaksForAsync([userId], ct)).TryGetValue(userId, out var person) ? person : null;

        // Candidates after the earliest moment, then each bill's own moment and the member's own texts, in memory.
        var reasons = await (
                from r in db.Reasons.AsNoTracking()
                join a in db.BillRecords.AsNoTracking() on r.RecordId equals a.Id
                where r.Status == ReasonStatuses.Published && (a.Status == BillRecordStatuses.Open || a.Status == BillRecordStatuses.Closed)
                      && numbers.Contains(a.BillNumber) && ((r.FirstPublicAt ?? r.CreatedAt) > earliest || r.EditedAt > earliest)
                select new { r.Id, a.BillNumber, At = r.FirstPublicAt ?? r.CreatedAt, r.EditedAt, r.AuthorUserId })
            .ToListAsync(ct);
        var statements = await db.OrganisationStatements.AsNoTracking().Shown(db.Organisations)
            .Where(s => numbers.Contains(s.BillNumber) && ((s.FirstPublicAt ?? s.PublishedAt) > earliest || s.EditedAt > earliest))
            .Select(s => new { s.Id, s.BillNumber, At = s.FirstPublicAt ?? s.PublishedAt, s.EditedAt, s.OrganisationId, s.WrittenByUserId })
            .ToListAsync(ct);
        var responses = await db.MpResponses.AsNoTracking()
            .Where(r => r.Status == ResponseStatuses.Published && numbers.Contains(r.BillNumber) && (r.PublishedAt > earliest || r.EditedAt > earliest))
            .Select(r => new { r.Id, r.BillNumber, r.PublishedAt, r.EditedAt, r.SeimasPersonId, r.WrittenByUserId })
            .ToListAsync(ct);
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.Status == CommentStatuses.Published && numbers.Contains(c.BillNumber) && (c.CreatedAt > earliest || c.EditedAt > earliest))
            .Select(c => new { c.Id, c.BillNumber, c.CreatedAt, c.EditedAt, c.TargetKind, c.TargetId, c.AuthorUserId, c.OrganisationId, c.SeimasPersonId })
            .ToListAsync(ct);
        var shownTargets = await ShownTargetsAsync(db, comments.Select(c => (c.TargetKind, c.TargetId)).ToList(), ct);

        bool New(string bill, DateTimeOffset at, DateTimeOffset? changed, bool own) =>
            !hiddenBills.Contains(bill) && since.TryGetValue(bill, out var from) && NewsRules.IsNew(at, changed, from, own);

        var items = new List<Item>();
        items.AddRange(reasons.Where(r => New(r.BillNumber, r.At, r.EditedAt, r.AuthorUserId == userId))
            .Select(r => new Item(CommentKinds.Reason, r.Id, r.BillNumber, null, null)));
        items.AddRange(statements.Where(s => New(s.BillNumber, s.At, s.EditedAt, NewsRules.OwnStatement(s.WrittenByUserId, s.OrganisationId, userId, represented)))
            .Select(s => new Item(CommentKinds.Statement, s.Id, s.BillNumber, null, null)));
        items.AddRange(responses.Where(r => New(r.BillNumber, r.PublishedAt, r.EditedAt, NewsRules.OwnResponse(r.WrittenByUserId, r.SeimasPersonId, userId, speaksFor)))
            .Select(r => new Item(CommentKinds.Response, r.Id, r.BillNumber, null, null)));
        items.AddRange(comments
            .Where(c => shownTargets.Contains(c.TargetId)
                        && New(c.BillNumber, c.CreatedAt, c.EditedAt, NewsRules.OwnComment(c.AuthorUserId, c.OrganisationId, c.SeimasPersonId, userId, represented, speaksFor)))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new Item(Kinds.Comment, c.Id, c.BillNumber, c.TargetKind, c.TargetId)));
        return items;
    }

    /// <summary>
    /// Of the comments' targets, those the bill's page shows, so their comments are shown (design 8.23.7): an argument published
    /// on a record that is open or closed, a statement shown on the page, a response published or withdrawn.
    /// </summary>
    private static async Task<HashSet<Guid>> ShownTargetsAsync(AppealsDb db, IReadOnlyList<(string Kind, Guid Id)> targets, CancellationToken ct)
    {
        var shown = new HashSet<Guid>();
        var reasonIds = targets.Where(t => t.Kind == CommentKinds.Reason).Select(t => t.Id).Distinct().ToList();
        var statementIds = targets.Where(t => t.Kind == CommentKinds.Statement).Select(t => t.Id).Distinct().ToList();
        var responseIds = targets.Where(t => t.Kind == CommentKinds.Response).Select(t => t.Id).Distinct().ToList();
        if (reasonIds.Count > 0)
        {
            shown.UnionWith(await (
                    from r in db.Reasons.AsNoTracking()
                    join a in db.BillRecords.AsNoTracking() on r.RecordId equals a.Id
                    where reasonIds.Contains(r.Id) && r.Status == ReasonStatuses.Published && (a.Status == BillRecordStatuses.Open || a.Status == BillRecordStatuses.Closed)
                    select r.Id)
                .ToListAsync(ct));
        }

        if (statementIds.Count > 0)
        {
            shown.UnionWith(await db.OrganisationStatements.AsNoTracking().Shown(db.Organisations).Where(s => statementIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct));
        }

        if (responseIds.Count > 0)
        {
            shown.UnionWith(await db.MpResponses.AsNoTracking()
                .Where(r => responseIds.Contains(r.Id) && (r.Status == ResponseStatuses.Published || r.Status == ResponseStatuses.Withdrawn))
                .Select(r => r.Id).ToListAsync(ct));
        }

        return shown;
    }

    private static class Kinds
    {
        public const string Comment = "comment";
    }

    /// <summary>A new text: its kind (<c>reason</c>, <c>statement</c>, <c>response</c>, <c>comment</c>), and a comment's target.</summary>
    private sealed record Item(string Kind, Guid Id, string BillNumber, string? TargetKind, Guid? TargetId);
}
