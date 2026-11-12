using Npgsql;

namespace Lt2.Parliament.ImportTests.Import;

/// <summary>
/// The way back from invented data: <c>migrator --reset-parliament</c> empties the parliament schema, and refuses when
/// any stored import was real or an import is running.
/// </summary>
[Collection(ImportDatabaseGroup.Name)]
public sealed class ResetTests(ImportDatabase database) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<long> CountAsync(string table) => database.ScalarAsync<long>($"SELECT count(*) FROM parliament.{table}");

    [Fact]
    public async Task Invented_data_is_emptied_the_migration_history_stays_and_the_next_import_starts_from_nothing()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        (await CountAsync("person")).ShouldBe(14);

        await database.RunMigratorAsync("--reset-parliament");

        foreach (var table in new[] { "import_run", "raw_document", "fetch_state", "person", "mandate", "body", "vote", "ballot", "mp_stat", "term" })
        {
            (await CountAsync(table)).ShouldBe(0, table);
        }

        (await database.ScalarAsync<long>("SELECT count(*) FROM parliament.__ef_migrations_history")).ShouldBeGreaterThan(0);

        var again = await import.RunAsync();
        again.Status.ShouldBe("succeeded");
        again.Requests.ShouldBe(20, "nothing was left to skip");
        (await CountAsync("person")).ShouldBe(14);
    }

    [Fact]
    public async Task It_refuses_and_deletes_nothing_when_any_import_run_was_real()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        await database.ScalarAsync<int>(
            "INSERT INTO parliament.import_run (id, started_at, status, source, synthetic, requests, changes) " +
            "VALUES (gen_random_uuid(), now(), 'succeeded', 'https://apps.lrs.lt/sip/', false, 1, 0) RETURNING 1");

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => database.RunMigratorAsync("--reset-parliament"));

        refusal.Message.ShouldContain("real Seimas service");
        (await CountAsync("person")).ShouldBe(14);
        (await CountAsync("import_run")).ShouldBe(2);
    }

    [Fact]
    public async Task It_refuses_while_an_import_is_running()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        await using var running = new NpgsqlConnection(database.UnpooledAdminConnectionString);
        await running.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(" + 0x4C54_3250_4152_4C + ")", running))
        {
            await command.ExecuteScalarAsync();
        }

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => database.RunMigratorAsync("--reset-parliament"));

        refusal.Message.ShouldContain("import is running");
        (await CountAsync("person")).ShouldBe(14);
    }

    [Fact]
    public async Task An_empty_schema_is_left_as_it_is()
    {
        await database.RunMigratorAsync("--reset-parliament");

        (await CountAsync("import_run")).ShouldBe(0);
    }
}
