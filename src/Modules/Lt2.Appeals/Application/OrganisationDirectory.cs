using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IOrganisationDirectory"/> for following organisations (design 8.14.14, stage B): what a reader may know of an
/// organisation, and which statements became public lately. No representative, no writer, no statement text.
/// </summary>
internal sealed class OrganisationDirectory(IDbContextFactory<AppealsDb> contexts) : IOrganisationDirectory
{
    public async Task<OrganisationFacts?> FindAsync(string slug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await OrganisationReads.BySlugAsync(db, slug, tracking: false, cancellationToken) is { } o ? Facts(o) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, OrganisationFacts>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, OrganisationFacts>();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.Organisations.AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken)).ToDictionary(o => o.Id, Facts);
    }

    public async Task<IReadOnlyList<PublicStatementFacts>> PublicStatementsAsync(IReadOnlyCollection<Guid> organisationIds, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (organisationIds.Count == 0)
        {
            return [];
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (from s in db.OrganisationStatements.AsNoTracking()
                      join o in db.Organisations.AsNoTracking() on s.OrganisationId equals o.Id
                      where organisationIds.Contains(s.OrganisationId) && o.Status == OrganisationStatuses.Active
                          && s.Status == StatementStatuses.Published && s.FirstPublicAt != null && s.FirstPublicAt >= since
                      orderby s.FirstPublicAt
                      select new PublicStatementFacts(s.Id, s.OrganisationId, s.BillNumber, s.Stance, s.FirstPublicAt!.Value))
            .ToListAsync(cancellationToken);
    }

    private static OrganisationFacts Facts(Organisation o)
    {
        var label = OrganisationReads.Label(o);
        return new OrganisationFacts(o.Id, o.Slug, o.Name, label.ConfirmedBy, label.Domain, o.Status);
    }
}
