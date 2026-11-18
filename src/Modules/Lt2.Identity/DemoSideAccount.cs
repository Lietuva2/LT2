using Lt2.Identity.Domain;
using Npgsql;

namespace Lt2.Identity;

/// <summary>What looking up the account named to <c>--demo-sides-for</c> found.</summary>
public enum DemoSideAccountStatus
{
    Found,

    /// <summary>No completed registration with that address (or the address is not usable).</summary>
    Unknown,

    /// <summary>The consent to store positions (design 6.5) is not given, or withdrawn.</summary>
    NoPositionConsent,

    /// <summary>An MP's own linked account, which takes no side (design 8.5.4).</summary>
    MpAccount,
}

/// <summary>The account named to <c>--demo-sides-for</c>; the address itself is never part of it.</summary>
public sealed record DemoSideAccountLookup(DemoSideAccountStatus Status, Guid UserId, DateTimeOffset MemberSince);

/// <summary>
/// Design 8.26.5, demonstration data only (the migrator's <c>--demo-sides-for</c>, which refuses unless <c>Demo:Allowed</c>
/// and the parliament data is invented): finds one existing account by its address, matched as sign-in matches it (the
/// normalised address, <see cref="EmailAddress.Normalize"/>), so its owner can see their own comparison on the invented
/// closed bills. Nothing here runs in the application.
/// </summary>
public static class DemoSideAccount
{
    public static async Task<DemoSideAccountLookup> FindAsync(string connectionString, string email, CancellationToken cancellationToken)
    {
        var normalized = EmailAddress.Normalize(email);
        if (normalized is null)
        {
            return new DemoSideAccountLookup(DemoSideAccountStatus.Unknown, Guid.Empty, default);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var query = new NpgsqlCommand(
            $"""
            SELECT p.user_id, p.registered_at, {CurrentPositionConsent.ExistsSql},
                   EXISTS (SELECT 1 FROM identity.politician_link l WHERE l.user_id = p.user_id AND l.part = 'mp' AND l.ended_at IS NULL)
            FROM identity.profile p
            WHERE p.normalized_email = @email AND p.registered_at IS NOT NULL
            """,
            connection);
        query.Parameters.AddWithValue("email", normalized);
        CurrentPositionConsent.AddParameters(query);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DemoSideAccountLookup(DemoSideAccountStatus.Unknown, Guid.Empty, default);
        }

        var id = reader.GetGuid(0);
        var since = reader.GetFieldValue<DateTimeOffset>(1);
        var status = reader.GetBoolean(3) ? DemoSideAccountStatus.MpAccount
            : !reader.GetBoolean(2) ? DemoSideAccountStatus.NoPositionConsent
            : DemoSideAccountStatus.Found;
        return new DemoSideAccountLookup(status, id, since);
    }
}
