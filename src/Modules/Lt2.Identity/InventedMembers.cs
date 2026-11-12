using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Identity;

/// <summary>One invented member for local demonstration data: an address on the reserved <c>.test</c> domain.</summary>
public sealed record InventedMember(Guid Id, string Email, string? DisplayName, DateTimeOffset MemberSince);

/// <summary>
/// Local demonstration data only (the migrator's <c>--demo-appeal</c>, which refuses unless the parliament data is the
/// stand-in's): completed member accounts with the age declaration and the consent to store positions, written in bulk.
/// Every address must be on the reserved <c>.test</c> domain, so no real person can be among them.
/// </summary>
public static class InventedMembers
{
    public const string Domain = "pavyzdys.test";

    /// <summary>Creates the accounts that do not exist yet (by address); returns how many were created.</summary>
    public static async Task<int> CreateAsync(string connectionString, IReadOnlyList<InventedMember> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Any(m => !m.Email.EndsWith("@" + Domain, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invented members must have addresses on {Domain}.", nameof(members));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        await using (var query = new NpgsqlCommand("SELECT normalized_email FROM identity.profile WHERE normalized_email LIKE @pattern", connection))
        {
            query.Parameters.AddWithValue("pattern", "%@" + Domain);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(0));
            }
        }

        var fresh = members.Where(m => !existing.Contains(EmailAddress.Normalize(m.Email)!)).ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var users = await connection.BeginBinaryImportAsync(
            "COPY identity.\"user\" (id, user_name, normalized_user_name, email, normalized_email, email_confirmed, security_stamp, concurrency_stamp, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            foreach (var m in fresh)
            {
                await users.StartRowAsync(cancellationToken);
                await users.WriteAsync(m.Id, NpgsqlDbType.Uuid, cancellationToken);
                await users.WriteAsync(m.Id.ToString("N"), NpgsqlDbType.Varchar, cancellationToken);
                await users.WriteAsync(m.Id.ToString("N").ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
                await users.WriteAsync(m.Email, NpgsqlDbType.Varchar, cancellationToken);
                await users.WriteAsync(m.Email.ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
                await users.WriteAsync(true, NpgsqlDbType.Boolean, cancellationToken);
                await users.WriteAsync(Guid.NewGuid().ToString("N"), NpgsqlDbType.Text, cancellationToken);
                await users.WriteAsync(Guid.NewGuid().ToString(), NpgsqlDbType.Text, cancellationToken);
                await users.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
                await users.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
                await users.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
                await users.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
            }

            await users.CompleteAsync(cancellationToken);
        }

        await using (var profiles = await connection.BeginBinaryImportAsync(
            "COPY identity.profile (user_id, display_name, normalized_email, verification_level, created_at, registered_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            foreach (var m in fresh)
            {
                await profiles.StartRowAsync(cancellationToken);
                await profiles.WriteAsync(m.Id, NpgsqlDbType.Uuid, cancellationToken);
                if (m.DisplayName is null)
                {
                    await profiles.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await profiles.WriteAsync(m.DisplayName, NpgsqlDbType.Varchar, cancellationToken);
                }

                await profiles.WriteAsync(EmailAddress.Normalize(m.Email)!, NpgsqlDbType.Varchar, cancellationToken);
                await profiles.WriteAsync(VerificationLevels.Email, NpgsqlDbType.Varchar, cancellationToken);
                await profiles.WriteAsync(m.MemberSince, NpgsqlDbType.TimestampTz, cancellationToken);
                await profiles.WriteAsync(m.MemberSince, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await profiles.CompleteAsync(cancellationToken);
        }

        await using (var roles = await connection.BeginBinaryImportAsync("COPY identity.user_role (user_id, role_id) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var m in fresh)
            {
                await roles.WriteRowAsync(cancellationToken, m.Id, SeedRoles.MemberId);
            }

            await roles.CompleteAsync(cancellationToken);
        }

        await using (var consents = await connection.BeginBinaryImportAsync(
            "COPY identity.consent (id, user_id, kind, text_version, given_at) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var m in fresh)
            {
                foreach (var (kind, version) in new[] { (ConsentKinds.Adult, ConsentKinds.AdultTextVersion), (PositionConsent.Kind, PositionConsent.TextVersion) })
                {
                    await consents.StartRowAsync(cancellationToken);
                    await consents.WriteAsync(Guid.CreateVersion7(m.MemberSince), NpgsqlDbType.Uuid, cancellationToken);
                    await consents.WriteAsync(m.Id, NpgsqlDbType.Uuid, cancellationToken);
                    await consents.WriteAsync(kind, NpgsqlDbType.Varchar, cancellationToken);
                    await consents.WriteAsync(version, NpgsqlDbType.Varchar, cancellationToken);
                    await consents.WriteAsync(m.MemberSince, NpgsqlDbType.TimestampTz, cancellationToken);
                }
            }

            await consents.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return fresh.Count;
    }
}
