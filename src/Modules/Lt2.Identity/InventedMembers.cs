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
/// stand-in's): completed member accounts with the age declaration, the consent to store positions and the terms of use in
/// force (design 8.22.2), written in bulk.
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
                foreach (var (kind, version) in new[] { (ConsentKinds.Adult, ConsentKinds.AdultTextVersion), (PositionConsent.Kind, PositionConsent.TextVersion), (ConsentKinds.Terms, TermsOfUse.Version) })
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

    /// <summary>
    /// When each of <paramref name="ids"/> completed its registration, for those that are invented demonstration accounts
    /// (an address on <see cref="Domain"/>), registered and with the consent to store positions in force; others are left
    /// out. Design 8.26.5: the invented closed bills take their sides only from these accounts.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> SinceAsync(string connectionString, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var query = new NpgsqlCommand(
            $"""
            SELECT p.user_id, p.registered_at FROM identity.profile p
            WHERE p.user_id = ANY(@ids) AND p.registered_at IS NOT NULL AND p.normalized_email LIKE @pattern AND {CurrentPositionConsent.ExistsSql}
            """,
            connection);
        query.Parameters.AddWithValue("ids", ids.ToArray());
        query.Parameters.AddWithValue("pattern", "%@" + Domain);
        CurrentPositionConsent.AddParameters(query);
        var since = new Dictionary<Guid, DateTimeOffset>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            since[reader.GetGuid(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        }

        return since;
    }

    /// <summary>
    /// Design 8.22.2: gives the terms of use in force to those of <paramref name="members"/> that exist as members and have not
    /// accepted them, dated like their other consents, so demonstration accounts made before the terms (or before a new
    /// version) stay consistent. Repeatable: running it again gives nothing twice. Returns how many accepted now.
    /// </summary>
    public static async Task<int> AcceptTermsAsync(string connectionString, IReadOnlyList<InventedMember> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Any(m => !m.Email.EndsWith("@" + Domain, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invented members must have addresses on {Domain}.", nameof(members));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO identity.consent (id, user_id, kind, text_version, given_at)
            SELECT x.id, x.user_id, @kind, @version, x.given_at
            FROM unnest(@ids, @users, @given) AS x(id, user_id, given_at)
            WHERE EXISTS (SELECT 1 FROM identity.profile p WHERE p.user_id = x.user_id AND p.registered_at IS NOT NULL)
              AND NOT EXISTS (SELECT 1 FROM identity.consent c
                              WHERE c.user_id = x.user_id AND c.kind = @kind AND c.text_version >= @version AND c.withdrawn_at IS NULL)
            """,
            connection);
        insert.Parameters.AddWithValue("kind", ConsentKinds.Terms);
        insert.Parameters.AddWithValue("version", TermsOfUse.Version);
        insert.Parameters.AddWithValue("ids", members.Select(m => Guid.CreateVersion7(m.MemberSince)).ToArray());
        insert.Parameters.AddWithValue("users", members.Select(m => m.Id).ToArray());
        insert.Parameters.AddWithValue("given", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, members.Select(m => m.MemberSince).ToArray());
        return await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Design 6.5: gives the positions consent text in force (<see cref="PositionConsent.TextVersion"/>) to those of
    /// <paramref name="members"/> that hold the consent only to an older text, not withdrawn, dated like their other consents,
    /// so demonstration accounts made before a new wording keep counting as consenting (their sides stay in the invented closed
    /// bills, <see cref="SinceAsync"/>). As a person giving it again does, the older row stays; an account that withdrew its
    /// consent, or never gave it, is left as it is. Repeatable: running it again gives nothing twice. Returns how many gave it now.
    /// </summary>
    public static async Task<int> GivePositionConsentAsync(string connectionString, IReadOnlyList<InventedMember> members, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Any(m => !m.Email.EndsWith("@" + Domain, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invented members must have addresses on {Domain}.", nameof(members));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO identity.consent (id, user_id, kind, text_version, given_at)
            SELECT x.id, x.user_id, @kind, @version, x.given_at
            FROM unnest(@ids, @users, @given) AS x(id, user_id, given_at)
            WHERE EXISTS (SELECT 1 FROM identity.profile p WHERE p.user_id = x.user_id AND p.registered_at IS NOT NULL)
              AND EXISTS (SELECT 1 FROM identity.consent c
                          WHERE c.user_id = x.user_id AND c.kind = @kind AND c.text_version < @version AND c.withdrawn_at IS NULL)
              AND NOT EXISTS (SELECT 1 FROM identity.consent c
                              WHERE c.user_id = x.user_id AND c.kind = @kind AND c.text_version >= @version AND c.withdrawn_at IS NULL)
            """,
            connection);
        insert.Parameters.AddWithValue("kind", PositionConsent.Kind);
        insert.Parameters.AddWithValue("version", PositionConsent.TextVersion);
        insert.Parameters.AddWithValue("ids", members.Select(m => Guid.CreateVersion7(m.MemberSince)).ToArray());
        insert.Parameters.AddWithValue("users", members.Select(m => m.Id).ToArray());
        insert.Parameters.AddWithValue("given", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, members.Select(m => m.MemberSince).ToArray());
        return await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
