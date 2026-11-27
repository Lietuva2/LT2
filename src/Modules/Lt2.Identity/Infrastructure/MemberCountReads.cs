using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Infrastructure;

/// <summary><see cref="IMemberCounts"/>: completed registrations (<see cref="Domain.Profile.RegisteredAt"/> set), counted in the database.</summary>
internal sealed class MemberCountReads(IDbContextFactory<IdentityDb> contexts) : IMemberCounts
{
    public async Task<MemberCounts> CountAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var members = db.Profiles.AsNoTracking().Where(p => p.RegisteredAt != null);
        var all = await members.CountAsync(cancellationToken);
        var joined = await members.CountAsync(p => p.RegisteredAt >= since, cancellationToken);
        return new MemberCounts(all, joined);
    }
}
