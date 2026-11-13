using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>
/// Two migrator runs never work at the same time: each holds a PostgreSQL advisory lock from its first step to its last,
/// so a second copy waits (two at once collided on role grants: "tuple concurrently updated").
/// </summary>
public sealed class MigratorLockTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_migrator_run_waits_while_another_holds_the_lock_and_then_completes()
    {
        await using var other = new NpgsqlConnection(database.AdminConnectionString);
        await other.OpenAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", other))
        {
            take.Parameters.AddWithValue("key", MigrationRunner.LockKey);
            await take.ExecuteNonQueryAsync();
        }

        var run = MigrationRunner.RunAsync(database.MigratorConfiguration(), [], TestContext.Current.CancellationToken);
        var finishedFirst = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        finishedFirst.ShouldNotBe(run, "the run waits while another holds the lock");
        (await WaitingForTheLockAsync()).ShouldBeGreaterThan(0, "and it waits in PostgreSQL, for the advisory lock");

        await using (var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", other))
        {
            release.Parameters.AddWithValue("key", MigrationRunner.LockKey);
            await release.ExecuteNonQueryAsync();
        }

        await run.WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        (await LocksHeldAsync()).ShouldBe(0, "the run gave the lock back");
    }

    [Fact]
    public async Task Two_runs_started_together_both_complete()
    {
        var configuration = database.MigratorConfiguration();
        await Task.WhenAll(
            MigrationRunner.RunAsync(configuration, [], TestContext.Current.CancellationToken),
            MigrationRunner.RunAsync(configuration, [], TestContext.Current.CancellationToken))
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        (await LocksHeldAsync()).ShouldBe(0);
    }

    private Task<long> WaitingForTheLockAsync() =>
        database.ScalarAsync<long>(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND ((classid::bigint << 32) | objid::bigint) = @key",
            ("key", MigrationRunner.LockKey));

    private Task<long> LocksHeldAsync() =>
        database.ScalarAsync<long>(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND granted AND ((classid::bigint << 32) | objid::bigint) = @key",
            ("key", MigrationRunner.LockKey));
}
