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
/// caller's transaction; followers are not told why. Under the profile's follow lock (<see cref="MemberFollowLock"/>), which
/// following takes too, so a follow added at the same moment is either refused or ended here. Emails about the member still
/// waiting in the outbox go too: a <c>email.follow-member</c> message is deleted, and the member's lines are taken out of a
/// gathered email (deleted when nothing is left), so nothing about a switched-off or deleted profile is sent.
/// </summary>
internal sealed class MemberFollowEnd : IMemberFollowEnd
{
    public async Task<int> EndAsync(string profileId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Ending follows needs a PostgreSQL transaction.", nameof(transaction));
        }

        await using (var locked = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, npgsql))
        {
            locked.Parameters.AddWithValue("key", MemberFollowLock.Key(profileId));
            await locked.ExecuteNonQueryAsync(cancellationToken);
        }

        int ended;
        await using (var command = new NpgsqlCommand("DELETE FROM notify.follow WHERE kind = @kind AND key = @key", connection, npgsql))
        {
            command.Parameters.AddWithValue("kind", FollowKinds.Member);
            command.Parameters.AddWithValue("key", profileId);
            ended = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // The member's page address in a payload: /nariai/{id} or /nariai/{name-slug}-{id}.
        const string About = "(({path}) = @alone OR ({path}) LIKE @slugged)";
        var alone = MemberAddress.Prefix + profileId;
        var slugged = "%-" + profileId;
        var sql = string.Join(
            ";\n",
            "DELETE FROM notify.outbox WHERE kind = @member AND " + About.Replace("{path}", "payload->>'profilePath'", StringComparison.Ordinal),
            "UPDATE notify.outbox SET payload = jsonb_set(payload, '{notices}', (SELECT coalesce(jsonb_agg(n), '[]'::jsonb) FROM jsonb_array_elements(payload->'notices') n WHERE NOT (n->>'kind' = @member AND "
                + About.Replace("{path}", "n->'payload'->>'profilePath'", StringComparison.Ordinal) + "))) "
                + "WHERE kind = @gathered AND EXISTS (SELECT 1 FROM jsonb_array_elements(payload->'notices') n WHERE n->>'kind' = @member AND "
                + About.Replace("{path}", "n->'payload'->>'profilePath'", StringComparison.Ordinal) + ")",
            "DELETE FROM notify.outbox WHERE kind = @gathered AND jsonb_array_length(payload->'notices') = 0");
#pragma warning disable CA2100 // Constant SQL; the values are parameters.
        await using (var outbox = new NpgsqlCommand(sql, connection, npgsql))
#pragma warning restore CA2100
        {
            outbox.Parameters.AddWithValue("member", MessageKinds.FollowMemberEmail);
            outbox.Parameters.AddWithValue("gathered", MessageKinds.GatheredNoticesEmail);
            outbox.Parameters.AddWithValue("alone", alone);
            outbox.Parameters.AddWithValue("slugged", slugged);
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }

        return ended;
    }
}

/// <summary>The advisory lock under which a member's follows are added and ended (design 8.25.1, 8.25.5).</summary>
internal static class MemberFollowLock
{
    public static string Key(string profileId) => "lt2.member-follows:" + profileId;
}