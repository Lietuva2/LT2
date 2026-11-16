using Npgsql;

namespace Lt2.Migrator;

/// <summary>
/// The database roles of design 5.5 and their grants:
/// <list type="bullet">
/// <item><c>lt2_migrator</c> owns the schemas and runs migrations;</item>
/// <item><c>lt2_app</c> has data access only, and may only insert into <c>audit.event</c>;</item>
/// <item><c>lt2_worker</c> the same, plus its own <c>hangfire</c> schema;</item>
/// <item><c>lt2_readonly</c> reads for analysis exactly the tables on <see cref="ReadableTables"/> (design 6.9), nothing else.</item>
/// </list>
/// </summary>
public static class DatabaseRoles
{
    public const string Migrator = "lt2_migrator";
    public const string App = "lt2_app";
    public const string Worker = "lt2_worker";
    public const string Readonly = "lt2_readonly";
    public const string HangfireSchema = "hangfire";

    /// <summary>Schemas where the application and worker read and write.</summary>
    public static readonly string[] DataSchemas = ["identity", "notify", "config", "parliament", "appeals", "usage"];

    /// <summary>
    /// Every schema the application's databases hold. Each of their tables is on <see cref="ReadableTables"/> or
    /// <see cref="WithheldTables"/>; <see cref="HangfireSchema"/> is the worker's and withheld as a whole.
    /// </summary>
    public static readonly string[] AllSchemas = ["identity", "notify", "audit", "config", "parliament", "appeals", "usage"];

    /// <summary>
    /// What <c>lt2_readonly</c> may read (design 5.5, 6.9: "only what is listed"): the public parliament data, the bills'
    /// records with their arguments, reports, flags, moderation actions and history, the daily counts, the settings, and the
    /// usage statistics' counts (design 8.27).
    /// A table that is not here is invisible to it, also after a migration adds one: a test fails until the new table is put
    /// on this list or on <see cref="WithheldTables"/>.
    /// </summary>
    public static readonly string[] ReadableTables =
    [
        "appeals.ai_draft_request", "appeals.bill_record", "appeals.daily_stat", "appeals.flag", "appeals.moderation_action",
        "appeals.reason", "appeals.report", "appeals.revision", "appeals.summary_attempt",
        "config.setting",
        "parliament.agenda_item", "parliament.agenda_snapshot", "parliament.ballot", "parliament.bill", "parliament.body",
        "parliament.contact", "parliament.faction_position", "parliament.fetch_state", "parliament.import_run", "parliament.mandate",
        "parliament.membership", "parliament.mp_stat", "parliament.person", "parliament.planned_item", "parliament.raw_document",
        "parliament.session", "parliament.sitting", "parliament.term", "parliament.vote",
        "usage.page", "usage.referrer", "usage.total",
    ];

    /// <summary>
    /// What <c>lt2_readonly</c> never reads, each for a reason:
    /// <list type="bullet">
    /// <item>positions (design 5.5), and the "Man svarbu" marks, which tell the side too;</item>
    /// <item>moderators' writing restrictions on accounts (design 8.3), shown to nobody but the person and staff;</item>
    /// <item>organisations' tables, which hold an account beside an organisation (who represents, registered, wrote,
    /// was invited, asked for a code; design 8.14.14); their public content is in the public API;</item>
    /// <item>MPs' responses (design 8.5.5), which hold the account that wrote one; their public content is in the public API;</item>
    /// <item>every account table (<c>identity</c>, the known browsers of design 8.20.2 included), the outbox, follows and their emails (<c>notify</c>), the audit log;</item>
    /// <item>MP mail (design 8.19.7): the MPs' choices and extra recipients, their links, the introduction letter's versions with
    /// who wrote them, and the suppression list;</item>
    /// <item>the usage statistics' salts and sketches of the periods under way (design 8.27): with a salt, a guessed address and
    /// browser could be tested against a sketch until the period ends;</item>
    /// <item>the migrations' history, the migrator's business only.</item>
    /// </list>
    /// </summary>
    public static readonly string[] WithheldTables =
    [
        "appeals.endorsement", "appeals.position", "appeals.writing_restriction",
        "appeals.organisation", "appeals.organisation_code", "appeals.organisation_invite", "appeals.organisation_representative",
        "appeals.organisation_statement", "appeals.mp_response",
        "audit.event",
        "identity.consent", "identity.data_protection_key", "identity.feedback", "identity.invitation", "identity.invitation_daily",
        "identity.known_device",
        "identity.party_support", "identity.politician_link", "identity.politician_token", "identity.profile", "identity.role",
        "identity.role_activation", "identity.role_claim",
        "identity.session", "identity.sign_in_code", "identity.user", "identity.user_claim", "identity.user_login",
        "identity.user_passkey", "identity.user_role", "identity.user_token",
        "notify.bill_event", "notify.digest_delivery", "notify.digest_item", "notify.digest_run", "notify.follow",
        "notify.notice_setting", "notify.outbox",
        "notify.introduction_letter", "notify.mp_email_pref", "notify.mp_mail_token", "notify.suppression",
        "appeals.__ef_migrations_history", "audit.__ef_migrations_history", "identity.__ef_migrations_history",
        "notify.__ef_migrations_history", "parliament.__ef_migrations_history",
        "usage.sketch", "usage.__ef_migrations_history",
    ];
    /// <summary>
    /// Run with a superuser (or a role with CREATEROLE) once per deploy: creates the login roles, sets their passwords
    /// and hands the database to <c>lt2_migrator</c>. Idempotent.
    /// </summary>
    public static async Task BootstrapAsync(
        NpgsqlConnection admin,
        IReadOnlyDictionary<string, string?> passwords,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(passwords);
        var database = admin.Database;
        foreach (var (role, password) in passwords)
        {
            var clause = string.IsNullOrEmpty(password) ? "" : $" PASSWORD {Literal(password.Trim())}";
            await ExecuteAsync(
                admin,
                $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{role}') THEN
                        CREATE ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
                    END IF;
                END $$;
                ALTER ROLE {role} LOGIN{clause};
                """,
                cancellationToken);
        }

        await ExecuteAsync(
            admin,
            $"""
            ALTER DATABASE {Identifier(database)} OWNER TO {Migrator};
            REVOKE ALL ON DATABASE {Identifier(database)} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {Identifier(database)} TO {App}, {Worker}, {Readonly};
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            """,
            cancellationToken);
    }

    /// <summary>Run as <c>lt2_migrator</c> before migrations: creates every schema with the right owner.</summary>
    public static async Task EnsureSchemasAsync(NpgsqlConnection migrator, IEnumerable<string> moduleSchemas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        foreach (var schema in moduleSchemas.Distinct(StringComparer.Ordinal))
        {
            await ExecuteAsync(migrator, $"CREATE SCHEMA IF NOT EXISTS {Identifier(schema)} AUTHORIZATION {Migrator};", cancellationToken);
        }

        // Hangfire installs and upgrades its own tables at worker start-up, so the worker owns that schema.
        await ExecuteAsync(
            migrator,
            $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_namespace WHERE nspname = '{HangfireSchema}') THEN
                    CREATE SCHEMA {HangfireSchema};
                END IF;
            END $$;
            GRANT USAGE, CREATE ON SCHEMA {HangfireSchema} TO {Worker};
            """,
            cancellationToken);
    }

    /// <summary>Run as <c>lt2_migrator</c> after migrations: grants on existing tables and defaults for future ones.</summary>
    public static async Task GrantAsync(NpgsqlConnection migrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        foreach (var schema in DataSchemas)
        {
            var s = Identifier(schema);
            await ExecuteAsync(
                migrator,
                $"""
                GRANT USAGE ON SCHEMA {s} TO {App}, {Worker};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {s} TO {App}, {Worker};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {s} TO {App}, {Worker};
                ALTER DEFAULT PRIVILEGES FOR ROLE {Migrator} IN SCHEMA {s}
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {App}, {Worker};
                ALTER DEFAULT PRIVILEGES FOR ROLE {Migrator} IN SCHEMA {s}
                    GRANT USAGE, SELECT ON SEQUENCES TO {App}, {Worker};
                """,
                cancellationToken);
        }

        // audit.event: insert only.
        await ExecuteAsync(
            migrator,
            $"""
            GRANT USAGE ON SCHEMA audit TO {App}, {Worker};
            REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM {App}, {Worker};
            GRANT INSERT ON audit.event TO {App}, {Worker};
            """,
            cancellationToken);

        await GrantReadOnlyAsync(migrator, cancellationToken);
        // Migration history is the migrator's business only. A module's history table lives in its first schema
        // (audit's covers config too), so revoke wherever such a table actually exists rather than per data schema.
        await ExecuteAsync(
            migrator,
            $"""
            DO $$
            DECLARE history record;
            BEGIN
                FOR history IN
                    SELECT schemaname FROM pg_tables WHERE tablename = '{HistoryTable}'
                LOOP
                    EXECUTE format('REVOKE ALL ON TABLE %I.%I FROM {App}, {Worker}, {Readonly}', history.schemaname, '{HistoryTable}');
                END LOOP;
            END $$;
            """,
            cancellationToken);
    }

    private const string HistoryTable = "__ef_migrations_history";

    /// <summary>
    /// <c>lt2_readonly</c> ends up with SELECT on exactly the existing tables of <see cref="ReadableTables"/>, USAGE on their
    /// schemas only, and nothing else: whatever earlier grants left ("every table but") is revoked, so an existing database
    /// reaches the same state as a new one. No default privileges, so a new table stays invisible until it is listed.
    /// </summary>
    private static async Task GrantReadOnlyAsync(NpgsqlConnection migrator, CancellationToken cancellationToken)
    {
        var readableSchemas = ReadableSchemas().ToHashSet(StringComparer.Ordinal);
        var existing = new List<string>();
        await using (var tables = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname = ANY(@schemas)", migrator))
        {
            tables.Parameters.AddWithValue("schemas", AllSchemas);
            await using var reader = await tables.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var table in existing)
        {
            var (schema, name) = (table[..table.IndexOf('.', StringComparison.Ordinal)], table[(table.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            var target = $"{Identifier(schema)}.{Identifier(name)}";
            await ExecuteAsync(
                migrator,
                ReadableTables.Contains(table, StringComparer.Ordinal) ? $"GRANT SELECT ON TABLE {target} TO {Readonly};" : $"REVOKE ALL ON TABLE {target} FROM {Readonly};",
                cancellationToken);
        }

        foreach (var schema in AllSchemas.Append(HangfireSchema))
        {
            await ExecuteAsync(
                migrator,
                $"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_namespace WHERE nspname = '{schema}') THEN
                        {(readableSchemas.Contains(schema) ? $"GRANT USAGE ON SCHEMA {Identifier(schema)} TO {Readonly};" : $"REVOKE ALL ON SCHEMA {Identifier(schema)} FROM {Readonly};")}
                    END IF;
                END $$;
                """,
                cancellationToken);
        }
    }

    /// <summary>The schemas of <see cref="ReadableTables"/>.</summary>
    private static IEnumerable<string> ReadableSchemas() => ReadableTables.Select(t => t[..t.IndexOf('.', StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // Identifiers come from constants; the only literal (a password) is escaped.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
