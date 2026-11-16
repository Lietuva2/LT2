using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IMemberTexts"/> (design 8.25.2, 8.25.5): a member's published arguments on bills whose record is open or closed
/// (never hidden), and the comments they wrote in their own voice that the bill's page shows (8.23.6, 8.23.7: published, under
/// a shown target, on a bill whose record is not hidden), for their public page and the follow emails. Never a side of the
/// person, a mark, a "Sutinku", a comment written as an organisation or an MP, or a count.
/// </summary>
internal sealed class MemberTexts(IDbContextFactory<AppealsDb> contexts, IBillCatalogue bills) : IMemberTexts
{
    public async Task<MemberTextsPage> PublishedAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var arguments = await Shown(db)
            .Where(x => x.Reason.AuthorUserId == userId)
            .OrderByDescending(x => x.Reason.FirstPublicAt ?? x.Reason.CreatedAt)
            .ThenByDescending(x => x.Reason.Id)
            .Take(take + 1)
            .Select(x => new Row(x.Reason.Id, MemberTextKinds.Argument, x.BillNumber, x.Reason.Side, null, x.Reason.Text, x.Reason.FirstPublicAt ?? x.Reason.CreatedAt))
            .ToListAsync(cancellationToken);
        var comments = await ShownCommentsAsync(db, [userId], null, cancellationToken);
        var rows = arguments
            .Concat(comments.Select(c => new Row(c.Id, MemberTextKinds.Comment, c.BillNumber, null, c.TargetKind, c.Text, c.CreatedAt)))
            .OrderByDescending(r => r.PublishedAt)
            .ThenByDescending(r => r.Id)
            .Take(take + 1)
            .ToList();
        var numbers = rows.Select(r => r.BillNumber).Distinct().ToList();
        var titles = numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, cancellationToken);
        return new MemberTextsPage(
            rows.Take(take)
                .Select(r => new MemberTextFacts(r.Id, r.Kind, r.BillNumber, titles.GetValueOrDefault(r.BillNumber)?.Title ?? r.BillNumber, r.Side, r.On, r.Text, r.PublishedAt))
                .ToList(),
            rows.Count > take);
    }

    public async Task<IReadOnlyList<PublishedTextFacts>> PublishedSinceAsync(IReadOnlyCollection<Guid> authors, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (authors.Count == 0)
        {
            return [];
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var arguments = await Shown(db)
            .Where(x => authors.Contains(x.Reason.AuthorUserId) && x.Reason.FirstPublicAt != null && x.Reason.FirstPublicAt >= since)
            .Select(x => new PublishedTextFacts(x.Reason.Id, MemberTextKinds.Argument, x.Reason.AuthorUserId, x.BillNumber, x.Reason.FirstPublicAt!.Value))
            .ToListAsync(cancellationToken);
        var comments = await ShownCommentsAsync(db, authors, since, cancellationToken);
        return arguments
            .Concat(comments.Select(c => new PublishedTextFacts(c.Id, MemberTextKinds.Comment, c.AuthorUserId!.Value, c.BillNumber, c.CreatedAt)))
            .OrderBy(t => t.PublishedAt)
            .ToList();
    }

    /// <summary>Published arguments on a record that is open or closed: what the bill's page shows.</summary>
    private static IQueryable<ShownReason> Shown(AppealsDb db) =>
        from r in db.Reasons.AsNoTracking()
        join a in db.BillRecords.AsNoTracking() on r.RecordId equals a.Id
        where r.Status == ReasonStatuses.Published && (a.Status == BillRecordStatuses.Open || a.Status == BillRecordStatuses.Closed)
        select new ShownReason { Reason = r, BillNumber = a.BillNumber };

    /// <summary>
    /// The authors' published comments in their own voice (written at or after <paramref name="since"/> when given) that the bill's
    /// page shows: under an argument that is shown, a statement shown on the page or a response published or withdrawn (design
    /// 8.23.7), on a bill whose record is not hidden. Newest first.
    /// </summary>
    private static async Task<List<Comment>> ShownCommentsAsync(AppealsDb db, IReadOnlyCollection<Guid> authors, DateTimeOffset? since, CancellationToken ct)
    {
        var query = db.Comments.AsNoTracking()
            .Where(c => c.SpeaksAs == CommentVoices.Person && c.AuthorUserId != null && authors.Contains(c.AuthorUserId.Value) && c.Status == CommentStatuses.Published);
        if (since is { } from)
        {
            query = query.Where(c => c.CreatedAt >= from);
        }

        var rows = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return rows;
        }

        var reasonIds = rows.Where(c => c.TargetKind == CommentKinds.Reason).Select(c => c.TargetId).Distinct().ToList();
        var statementIds = rows.Where(c => c.TargetKind == CommentKinds.Statement).Select(c => c.TargetId).Distinct().ToList();
        var responseIds = rows.Where(c => c.TargetKind == CommentKinds.Response).Select(c => c.TargetId).Distinct().ToList();
        var shownTargets = new HashSet<Guid>();
        if (reasonIds.Count > 0)
        {
            shownTargets.UnionWith(await Shown(db).Where(x => reasonIds.Contains(x.Reason.Id)).Select(x => x.Reason.Id).ToListAsync(ct));
        }

        if (statementIds.Count > 0)
        {
            shownTargets.UnionWith(await db.OrganisationStatements.AsNoTracking().Shown(db.Organisations).Where(s => statementIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct));
        }

        if (responseIds.Count > 0)
        {
            shownTargets.UnionWith(await db.MpResponses.AsNoTracking()
                .Where(r => responseIds.Contains(r.Id) && (r.Status == ResponseStatuses.Published || r.Status == ResponseStatuses.Withdrawn))
                .Select(r => r.Id).ToListAsync(ct));
        }

        var numbers = rows.Select(c => c.BillNumber).Distinct().ToList();
        var hiddenBills = (await db.BillRecords.AsNoTracking()
                .Where(a => numbers.Contains(a.BillNumber) && a.Status == BillRecordStatuses.Rejected)
                .Select(a => a.BillNumber).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        return rows.Where(c => CommentRules.IsShown(c.Status, shownTargets.Contains(c.TargetId), hiddenBills.Contains(c.BillNumber))).ToList();
    }

    private sealed record Row(Guid Id, string Kind, string BillNumber, string? Side, string? On, string Text, DateTimeOffset PublishedAt);

    private sealed class ShownReason
    {
        public Reason Reason { get; init; } = default!;

        public string BillNumber { get; init; } = "";
    }
}
