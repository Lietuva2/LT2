using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IMemberTexts"/> (design 8.25.2, 8.25.5): a member's published arguments on bills whose record is open or closed
/// (never hidden), for their public page and the follow emails. Never a side of the person, a mark, or a count.
/// </summary>
internal sealed class MemberTexts(IDbContextFactory<AppealsDb> contexts, IBillCatalogue bills) : IMemberTexts
{
    public async Task<MemberArgumentsPage> PublishedAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await Shown(db)
            .Where(x => x.Reason.AuthorUserId == userId)
            .OrderByDescending(x => x.Reason.FirstPublicAt ?? x.Reason.CreatedAt)
            .ThenByDescending(x => x.Reason.Id)
            .Take(take + 1)
            .Select(x => new { x.Reason.Id, x.BillNumber, x.Reason.Side, x.Reason.Text, PublishedAt = x.Reason.FirstPublicAt ?? x.Reason.CreatedAt })
            .ToListAsync(cancellationToken);
        var numbers = rows.Select(r => r.BillNumber).Distinct().ToList();
        var titles = numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, cancellationToken);
        return new MemberArgumentsPage(
            rows.Take(take)
                .Select(r => new MemberArgumentFacts(r.Id, r.BillNumber, titles.GetValueOrDefault(r.BillNumber)?.Title ?? r.BillNumber, r.Side, r.Text, r.PublishedAt))
                .ToList(),
            rows.Count > take);
    }

    public async Task<IReadOnlyList<PublishedArgumentFacts>> PublishedSinceAsync(IReadOnlyCollection<Guid> authors, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (authors.Count == 0)
        {
            return [];
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await Shown(db)
            .Where(x => authors.Contains(x.Reason.AuthorUserId) && x.Reason.FirstPublicAt != null && x.Reason.FirstPublicAt >= since)
            .OrderBy(x => x.Reason.FirstPublicAt)
            .Select(x => new PublishedArgumentFacts(x.Reason.Id, x.Reason.AuthorUserId, x.BillNumber, x.Reason.FirstPublicAt!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsPublicAsync(Guid reasonId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await Shown(db).AnyAsync(x => x.Reason.Id == reasonId, cancellationToken);
    }

    /// <summary>Published arguments on a record that is open or closed: what the bill's page shows.</summary>
    private static IQueryable<ShownReason> Shown(AppealsDb db) =>
        from r in db.Reasons.AsNoTracking()
        join a in db.BillRecords.AsNoTracking() on r.RecordId equals a.Id
        where r.Status == ReasonStatuses.Published && (a.Status == BillRecordStatuses.Open || a.Status == BillRecordStatuses.Closed)
        select new ShownReason { Reason = r, BillNumber = a.BillNumber };

    private sealed class ShownReason
    {
        public Reason Reason { get; init; } = default!;

        public string BillNumber { get; init; } = "";
    }
}
