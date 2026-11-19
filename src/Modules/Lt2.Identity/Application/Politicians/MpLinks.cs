using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Politicians;

/// <summary>
/// <see cref="IMpLinks"/> (design 8.5.5): the address of the account linked as the MP, for a moderator's notice about the MP's
/// response; the same address step one's notices go to; and, for the follow email about MPs' responses (design 8.20.1), which
/// MP an account speaks for, so the MP's own account and office are not told about their own MP's responses. Nothing else
/// of a link leaves the module. Other modules ask while a page's other components read the scoped context, so each call
/// reads through a context of its own.
/// </summary>
internal sealed class MpLinks(IDbContextFactory<IdentityDb> contexts) : IMpLinks
{
    public async Task<string?> MpAccountAddressAsync(long seimasPersonId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (from l in PoliticianAccess.OpenLinks(db).AsNoTracking()
                      join u in db.Users.AsNoTracking() on l.UserId equals u.Id
                      where l.SeimasPersonId == seimasPersonId && l.Part == PoliticianParts.Mp
                      select u.Email)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, long>> SpeaksForAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        var ids = userIds.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // One MP per account (design 8.5.3: an office member of at most one MP; the MP's own account is one MP's).
        return (await PoliticianAccess.OpenLinks(db).AsNoTracking()
                .Where(l => ids.Contains(l.UserId))
                .Select(l => new { l.UserId, l.SeimasPersonId })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.First().SeimasPersonId);
    }

    public async Task<IReadOnlyList<OfficeMemberAddress>> OfficeMembersAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken)
    {
        if (seimasPersonIds.Count == 0)
        {
            return [];
        }

        var people = seimasPersonIds.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await (from l in PoliticianAccess.OpenLinks(db).AsNoTracking()
                       join u in db.Users.AsNoTracking() on l.UserId equals u.Id
                       where people.Contains(l.SeimasPersonId) && l.Part == PoliticianParts.Office && u.Email != null
                       select new { l.SeimasPersonId, l.UserId, u.Email })
                .ToListAsync(cancellationToken))
            .Select(m => new OfficeMemberAddress(m.SeimasPersonId, m.UserId, m.Email!))
            .ToList();
    }
}
