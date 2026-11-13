using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Microsoft.Extensions.Configuration;

namespace Lt2.IntegrationTests;

/// <summary>
/// The migrator's <c>--local-code-limits</c> (design 6.2, local stack only): raises the code limits per address through
/// <c>config.setting</c>, audited like any change, and refuses unless the configuration allows local data.
/// </summary>
public sealed class LocalCodeLimitsTests(DatabaseFixture database) : ApiTest(database)
{
    private static IConfiguration Configuration(bool allowed) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Allowed"] = allowed ? "true" : null }).Build();

    [Fact]
    public async Task It_refuses_unless_the_configuration_allows_local_data()
    {
        const string Rows = "SELECT coalesce(string_agg(key || '=' || value::text, ',' ORDER BY key), '') FROM config.setting WHERE key LIKE 'SignIn:Codes%'";
        var before = await Database.ScalarAsync<string>(Rows);
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            LocalCodeLimits.RunAsync(Configuration(false), Database.MigratorConnectionString, CancellationToken.None));
        error.Message.ShouldContain("Demo:Allowed");
        (await Database.ScalarAsync<string>(Rows)).ShouldBe(before, "nothing changed");
    }

    [Fact]
    public async Task On_the_local_stack_it_raises_both_code_limits_once_and_audits_each_change()
    {
        try
        {
            (await LocalCodeLimits.RunAsync(Configuration(true), Database.MigratorConnectionString, CancellationToken.None)).ShouldBe(2);
            (await Database.ScalarAsync<string>("SELECT value::text FROM config.setting WHERE key = 'SignIn:CodesPerHour'")).ShouldBe("20");
            (await Database.ScalarAsync<string>("SELECT value::text FROM config.setting WHERE key = 'SignIn:CodesPerDay'")).ShouldBe("50");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'setting.changed' AND actor_role = 'migrator' AND target LIKE 'SignIn:Codes%'")).ShouldBe(2);

            (await LocalCodeLimits.RunAsync(Configuration(true), Database.MigratorConnectionString, CancellationToken.None)).ShouldBe(0, "running again changes nothing");
        }
        finally
        {
            // Back to the defaults for the other tests.
            await Database.ExecuteAsync("DELETE FROM config.setting WHERE key LIKE 'SignIn:Codes%'");
        }
    }
}
