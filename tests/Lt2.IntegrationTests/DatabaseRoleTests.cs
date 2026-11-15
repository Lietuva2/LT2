using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>
/// What each database role may and may not do (design 5.5), checked against the database the migrator prepared:
/// by trying the statement as that role, not by reading the grant code.
/// </summary>
public sealed class DatabaseRoleTests(DatabaseFixture database)
{
    private static readonly string[] AllSchemas = ["identity", "notify", "audit", "config", "parliament", "appeals", "public", DatabaseRoles.HangfireSchema];

    public static TheoryData<string> RunningRoles() => new(DatabaseRoles.App, DatabaseRoles.Worker, DatabaseRoles.Readonly);

    public static TheoryData<string> WritingRoles() => new(DatabaseRoles.App, DatabaseRoles.Worker);

    [Theory]
    [MemberData(nameof(RunningRoles))]
    public async Task No_running_role_can_read_any_migration_history(string role)
    {
        var tables = await HistoryTablesAsync();
        tables.Count.ShouldBeGreaterThanOrEqualTo(5, "one history table per module");

        await using var connection = await OpenAsync(role);
        foreach (var table in tables)
        {
            await ShouldBeRefusedAsync(connection, $"SELECT count(*) FROM {table}", $"{role} reading {table}");
        }
    }

    [Theory]
    [MemberData(nameof(WritingRoles))]
    public async Task The_writing_roles_may_only_insert_into_the_audit_log(string role)
    {
        await using var connection = await OpenAsync(role);
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO audit.event (id, occurred_at, action) VALUES (gen_random_uuid(), now(), 'test.insert')", connection))
        {
            (await insert.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        foreach (var sql in new[] { "SELECT count(*) FROM audit.event", "UPDATE audit.event SET action = 'x'", "DELETE FROM audit.event", "TRUNCATE audit.event" })
        {
            await ShouldBeRefusedAsync(connection, sql, $"{role}: {sql}");
        }
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("notify")]
    public async Task The_read_only_role_cannot_see_into_identity_or_notify(string schema)
    {
        (await database.ScalarAsync<bool>("SELECT has_schema_privilege(@r, @s, 'USAGE')", ("r", DatabaseRoles.Readonly), ("s", schema)))
            .ShouldBeFalse();

        await using var connection = await OpenAsync(DatabaseRoles.Readonly);
        var tables = await TablesInAsync(schema);
        tables.ShouldNotBeEmpty();
        foreach (var table in tables)
        {
            await ShouldBeRefusedAsync(connection, $"SELECT count(*) FROM {table}", table);
        }
    }

    [Fact]
    public async Task The_read_only_role_cannot_read_positions_or_endorsements_but_reads_the_rest_of_appeals()
    {
        // The real tables, after the grants a deploy applies (the fixture ran the migrator twice).
        (await database.ScalarAsync<bool>("SELECT to_regclass('appeals.position') IS NOT NULL")).ShouldBeTrue();
        (await database.ScalarAsync<bool>("SELECT has_table_privilege(@r, 'appeals.position', 'SELECT')", ("r", DatabaseRoles.Readonly)))
            .ShouldBeFalse();

        await using var readonlyConnection = await OpenAsync(DatabaseRoles.Readonly);
        foreach (var hidden in new[] { "appeals.position", "appeals.endorsement", "appeals.writing_restriction" })
        {
            await ShouldBeRefusedAsync(readonlyConnection, $"SELECT count(*) FROM {hidden}", hidden);
            await ShouldBeRefusedAsync(readonlyConnection, $"SELECT user_id FROM {hidden} LIMIT 1", hidden + " by column");
        }

        foreach (var visible in new[] { "appeals.bill_record", "appeals.reason", "appeals.daily_stat", "config.setting" })
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {visible}", readonlyConnection);
            await command.ExecuteScalarAsync();
        }

        // Applying the grants again (every deploy does) keeps the tables hidden.
        await using var migrator = new NpgsqlConnection(database.MigratorConnectionString);
        await migrator.OpenAsync();
        await DatabaseRoles.GrantAsync(migrator, CancellationToken.None);
        await ShouldBeRefusedAsync(readonlyConnection, "SELECT count(*) FROM appeals.position", "appeals.position after a second deploy");
    }

    [Fact]
    public async Task The_read_only_role_reads_exactly_the_granted_tables_on_a_new_database()
    {
        await ReadsExactlyTheGrantedTablesAsync();
    }

    [Fact]
    public async Task A_database_with_the_old_every_table_but_grants_ends_in_the_same_state_and_a_new_table_stays_invisible()
    {
        await using var migrator = new NpgsqlConnection(database.MigratorConnectionString);
        await migrator.OpenAsync();
        try
        {
            // The grants before 2026-11-15 (design 6.9 replaced them): every table of three schemas, then a hand-kept list
            // revoked. A table added later (here appeals.extra_test) was readable by the next deploy's grants.
            await ExecuteAsync(migrator, "CREATE TABLE appeals.extra_test (id int)");
            foreach (var schema in new[] { "parliament", "appeals", "config" })
            {
                await ExecuteAsync(migrator, $"GRANT USAGE ON SCHEMA {schema} TO {DatabaseRoles.Readonly}; GRANT SELECT ON ALL TABLES IN SCHEMA {schema} TO {DatabaseRoles.Readonly};");
            }

            foreach (var hidden in new[] { "appeals.position", "appeals.endorsement", "appeals.writing_restriction", "appeals.organisation", "appeals.organisation_representative", "appeals.organisation_invite", "appeals.organisation_code", "appeals.organisation_statement" })
            {
                await ExecuteAsync(migrator, $"REVOKE ALL ON TABLE {hidden} FROM {DatabaseRoles.Readonly}");
            }

            (await database.ScalarAsync<bool>("SELECT has_table_privilege(@r, 'appeals.extra_test', 'SELECT')", ("r", DatabaseRoles.Readonly))).ShouldBeTrue("the old grants");

            // The migrator runs again with the lists.
            await DatabaseRoles.GrantAsync(migrator, CancellationToken.None);

            await using var readonlyConnection = await OpenAsync(DatabaseRoles.Readonly);
            await ShouldBeRefusedAsync(readonlyConnection, "SELECT count(*) FROM appeals.extra_test", "a table on neither list");
            await ReadsExactlyTheGrantedTablesAsync("appeals.extra_test");
        }
        finally
        {
            await ExecuteAsync(migrator, "DROP TABLE IF EXISTS appeals.extra_test");
        }
    }

    [Theory]
    [MemberData(nameof(RunningRoles))]
    public async Task Only_the_migrator_can_create_tables(string role)
    {
        foreach (var schema in AllSchemas)
        {
            // Hangfire creates and upgrades its own tables when the worker starts, so lt2_worker has CREATE there and
            // nowhere else (DatabaseRoles.EnsureSchemasAsync).
            var expected = role == DatabaseRoles.Worker && schema == DatabaseRoles.HangfireSchema;
            (await database.ScalarAsync<bool>("SELECT has_schema_privilege(@r, @s, 'CREATE')", ("r", role), ("s", schema)))
                .ShouldBe(expected, $"{role} CREATE on {schema}");
        }

        (await database.ScalarAsync<bool>("SELECT has_database_privilege(@r, current_database(), 'CREATE')", ("r", role)))
            .ShouldBeFalse($"{role} may not create schemas");

        await using var connection = await OpenAsync(role);
        foreach (var schema in new[] { "identity", "config", "public" })
        {
            await ShouldBeRefusedAsync(connection, $"CREATE TABLE {schema}.extra (id int)", $"{role} creating a table in {schema}");
        }

        await ShouldBeRefusedAsync(connection, "CREATE SCHEMA extra", $"{role} creating a schema");
    }

    /// <summary>
    /// Every table of every module schema: readable by <c>lt2_readonly</c> exactly when it is on the granted list (tried as
    /// that role, and by the catalogue), and no USAGE on a schema without a granted table.
    /// </summary>
    private async Task ReadsExactlyTheGrantedTablesAsync(params string[] unlisted)
    {
        await using var readonlyConnection = await OpenAsync(DatabaseRoles.Readonly);
        var tables = new List<string>();
        foreach (var schema in DatabaseRoles.AllSchemas)
        {
            tables.AddRange(await TablesInAsync(schema));
        }

        foreach (var quoted in tables)
        {
            var table = quoted.Replace("\"", "", StringComparison.Ordinal);
            var granted = DatabaseRoles.ReadableTables.Contains(table);
            (granted || DatabaseRoles.WithheldTables.Contains(table) || unlisted.Contains(table)).ShouldBeTrue($"{table} is on neither list");
            (await database.ScalarAsync<bool>("SELECT has_table_privilege(@r, @t, 'SELECT')", ("r", DatabaseRoles.Readonly), ("t", quoted))).ShouldBe(granted, table);
            if (granted)
            {
#pragma warning disable CA2100 // Test statements built from catalogue names.
                await using var command = new NpgsqlCommand($"SELECT count(*) FROM {quoted}", readonlyConnection);
#pragma warning restore CA2100
                await command.ExecuteScalarAsync();
            }
            else
            {
                await ShouldBeRefusedAsync(readonlyConnection, $"SELECT count(*) FROM {quoted}", table);
            }
        }

        foreach (var schema in new[] { "identity", "notify", "audit", DatabaseRoles.HangfireSchema })
        {
            (await database.ScalarAsync<bool>("SELECT has_schema_privilege(@r, @s, 'USAGE')", ("r", DatabaseRoles.Readonly), ("s", schema))).ShouldBeFalse(schema);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
#pragma warning disable CA2100 // Test statements built from constants.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenAsync(string role)
    {
        var connection = new NpgsqlConnection(database.ConnectionStringFor(role));
        await connection.OpenAsync();
        return connection;
    }

    private async Task<List<string>> HistoryTablesAsync()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT format('%I.%I', schemaname, tablename) FROM pg_tables WHERE tablename = '__ef_migrations_history' ORDER BY 1", admin);
        return await ReadAllAsync(command);
    }

    private async Task<List<string>> TablesInAsync(string schema)
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT format('%I.%I', schemaname, tablename) FROM pg_tables WHERE schemaname = @s ORDER BY 1", admin);
        command.Parameters.AddWithValue("s", schema);
        return await ReadAllAsync(command);
    }

    private static async Task<List<string>> ReadAllAsync(NpgsqlCommand command)
    {
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task ShouldBeRefusedAsync(NpgsqlConnection connection, string sql, string what)
    {
#pragma warning disable CA2100 // Test statements built from catalogue names.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), what);
        error.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, what);
    }
}
