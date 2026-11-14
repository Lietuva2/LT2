using Npgsql;

namespace Lt2.Migrator;

/// <summary>
/// The database roles of design 5.5 and their grants:
/// <list type="bullet">
/// <item><c>lt2_migrator</c> owns the schemas and runs migrations;</item>
/// <item><c>lt2_app</c> has data access only, and may only insert into <c>audit.event</c>;</item>
/// <item><c>lt2_worker</c> the same, plus its own <c>hangfire</c> schema;</item>
/// <item><c>lt2_readonly</c> reads for analysis, with no access to <c>identity</c>, <c>notify</c>, <c>appeals.position</c> or <c>appeals.endorsement</c>.</item>
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
    public static readonly string[] DataSchemas = ["identity", "notify", "config", "parliament", "appeals"];

    /// <summary>Schemas the read-only role may see.</summary>
    public static readonly string[] ReadableSchemas = ["parliament", "appeals", "config"];

    /// <summary>
    /// Tables in readable schemas that stay hidden from the read-only role: positions (design 5.5), and the "Man svarbu"
    /// marks, which are allowed only on one's own side and so tell the side as well; and moderators' writing
    /// restrictions on accounts (design 8.3), which are never shown to anyone but the person and staff.
    /// <para>
    /// Organisations (design 8.14.14): who represents one is told to nobody but its other representatives, so the tables
    /// that hold an account beside an organisation stay hidden too: representatives, one-time links, confirmation codes,
    /// and the organisation and statement rows (who registered it, who wrote a statement). Their public content is in the
    /// public API.
    /// </para>
    /// </summary>
    public static readonly string[] HiddenTables =
    [
        "appeals.position", "appeals.endorsement", "appeals.writing_restriction",
        "appeals.organisation", "appeals.organisation_representative", "appeals.organisation_invite", "appeals.organisation_code",
        "appeals.organisation_statement",
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

        foreach (var schema in ReadableSchemas)
        {
            var s = Identifier(schema);
            await ExecuteAsync(
                migrator,
                $"""
                GRANT USAGE ON SCHEMA {s} TO {Readonly};
                GRANT SELECT ON ALL TABLES IN SCHEMA {s} TO {Readonly};
                """,
                cancellationToken);
        }

        foreach (var table in HiddenTables)
        {
            await ExecuteAsync(
                migrator,
                $"""
                DO $$
                BEGIN
                    IF to_regclass('{table}') IS NOT NULL THEN
                        REVOKE ALL ON TABLE {table} FROM {Readonly};
                    END IF;
                END $$;
                """,
                cancellationToken);
        }

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
