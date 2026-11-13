using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lt2.ArchitectureTests;

/// <summary>
/// Design 9.5: the connection pools of every host together stay below what PostgreSQL allows, with room left for psql and
/// backups, so no part can take the database's connections from another and no burst can take the site down.
/// </summary>
public sealed partial class ConnectionBudgetTests
{
    /// <summary>Connections kept free for people (psql), backups and the read-only role.</summary>
    private const int Headroom = 20;

    /// <summary>Web instances that can run at once: two behind Caddy (design 9.1), and two new ones during a deploy (9.3).</summary>
    private const int WebInstances = 4;

    private static string Root { get; } = FindRoot();

    private static int Setting(string host, string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", host, "appsettings.json")));
        document.RootElement.GetProperty("Database").TryGetProperty(key, out var value).ShouldBeTrue($"{host} sets Database:{key} explicitly");
        return value.GetInt32();
    }

    private static int PostgresFlag(string compose, string name)
    {
        var match = Regex.Match(compose, $"\"{name}=(?<n>\\d+)\"");
        match.Success.ShouldBeTrue($"compose.yaml sets {name} explicitly");
        return int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void The_pools_of_web_worker_hangfire_and_migrator_stay_below_what_postgres_allows_with_room_for_psql()
    {
        var compose = File.ReadAllText(Path.Combine(Root, "deploy", "compose.yaml"));
        var available = PostgresFlag(compose, "max_connections") - PostgresFlag(compose, "superuser_reserved_connections");

        var web = Setting("Lt2.Web", "MaxPoolSize");
        var worker = Setting("Lt2.Worker", "MaxPoolSize");
        var hangfire = Setting("Lt2.Worker", "HangfireMaxPoolSize");
        var migrator = Setting("Lt2.Migrator", "MaxPoolSize");

        (WebInstances * web + worker + hangfire + migrator + Headroom).ShouldBeLessThanOrEqualTo(available);
        web.ShouldBeGreaterThanOrEqualTo(20, "each web instance serves visitors; it gets the largest share");
    }

    [Fact]
    public void Compose_does_not_override_a_pool_size_outside_the_budget()
    {
        var compose = File.ReadAllText(Path.Combine(Root, "deploy", "compose.yaml"));
        PoolOverride().IsMatch(compose).ShouldBeFalse("pool sizes live in appsettings.json, where the budget test reads them");
        compose.ShouldNotContain("Pool Size", Case.Insensitive);
    }

    [GeneratedRegex(@"Database__(Hangfire)?MaxPoolSize")]
    private static partial Regex PoolOverride();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LT2.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
