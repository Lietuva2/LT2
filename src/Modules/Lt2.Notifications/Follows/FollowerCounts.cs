using System.Data.Common;
using Lt2.Notifications.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Notifications.Follows;

/// <summary>
/// <see cref="IFollowerCounts"/> (design 8.11.2, owner 2026-11-16): how many follow one thing, counted from <c>notify.follow</c>
/// when asked; a number only, never who.
/// </summary>
internal sealed class FollowerCounts(IDbContextFactory<NotificationsDb> contexts) : IFollowerCounts
{
    public Task<int> BillAsync(string billNumber, CancellationToken cancellationToken) =>
        CountAsync(FollowKinds.Bill, billNumber.Trim().ToUpperInvariant(), cancellationToken);

    public Task<int> MpAsync(long seimasPersonId, CancellationToken cancellationToken) =>
        CountAsync(FollowKinds.Mp, FollowRules.MpKey(seimasPersonId), cancellationToken);

    public Task<int> OrganisationAsync(Guid organisationId, CancellationToken cancellationToken) =>
        CountAsync(FollowKinds.Organisation, FollowRules.IdKey(organisationId), cancellationToken);

    public Task<int> MemberAsync(string profileId, CancellationToken cancellationToken) =>
        CountAsync(FollowKinds.Member, profileId, cancellationToken);

    private async Task<int> CountAsync(string kind, string key, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Follows.AsNoTracking().CountAsync(f => f.Kind == kind && f.Key == key, ct);
    }
}

/// <summary>
/// <see cref="IMemberFollowEnd"/> (design 8.25.1): a member's profile switched off or deleted ends every follow of it, in the
/// caller's transaction; followers are not told why.
/// </summary>
internal sealed class MemberFollowEnd : IMemberFollowEnd
{
    public async Task<int> EndAsync(string profileId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Ending follows needs a PostgreSQL transaction.", nameof(transaction));
        }

        await using var command = new NpgsqlCommand("DELETE FROM notify.follow WHERE kind = @kind AND key = @key", connection, npgsql);
        command.Parameters.AddWithValue("kind", FollowKinds.Member);
        command.Parameters.AddWithValue("key", profileId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
