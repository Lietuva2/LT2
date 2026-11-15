using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// The module's transaction advisory locks, each held until its transaction ends, so the rules of design 6.8 hold under
/// parallel requests: one current code per member or organisation, and the sign-in code limits per address.
/// </summary>
internal static class IdentityLocks
{
    private const string Sql = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";

    /// <summary>A member's personal invitation code is made or replaced one request at a time.</summary>
    public static Task PersonalCodeAsync(IdentityDb db, Guid userId, CancellationToken ct) => TakeAsync(db, "lt2.personal-code:" + userId.ToString("N"), ct);

    /// <summary>An organisation's invitation code is made or replaced one request at a time.</summary>
    public static Task OrganisationCodeAsync(IdentityDb db, Guid organisationId, CancellationToken ct) =>
        TakeAsync(db, "lt2.organisation-code:" + organisationId.ToString("N"), ct);

    /// <summary>Sign-in codes for one account are counted and issued one request at a time.</summary>
    public static Task SignInCodesAsync(IdentityDb db, Guid userId, CancellationToken ct) => TakeAsync(db, "lt2.sign-in-code:" + userId.ToString("N"), ct);

    private static Task<int> TakeAsync(IdentityDb db, string key, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(Sql, [new NpgsqlParameter("key", key)], ct);
}
