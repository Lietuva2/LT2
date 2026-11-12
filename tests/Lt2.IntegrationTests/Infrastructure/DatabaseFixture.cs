using Lt2.Migrator;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Lt2.IntegrationTests.Infrastructure.DatabaseFixture))]

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// A real PostgreSQL in a container, prepared exactly as in production: the migrator creates the roles, applies every
/// module's migrations as <c>lt2_migrator</c> and grants access. The web application then connects as <c>lt2_app</c>,
/// so the tests also prove the grants are sufficient.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string AppPassword = "app-test-password";
    public const string WorkerPassword = "worker-test-password";
    public const string ReadonlyPassword = "readonly-test-password";
    private const string MigratorPassword = "migrator-test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("lt2")
        .WithUsername("postgres")
        .WithPassword("postgres-test-password")
        .Build();

    /// <summary>Superuser connection, for test set-up and inspection only.</summary>
    public string AdminConnectionString => _container.GetConnectionString();

    public string ConnectionStringFor(string role, string password) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Username = role, Password = password }.ConnectionString;

    public string AppConnectionString => ConnectionStringFor(DatabaseRoles.App, AppPassword);

    public string WorkerConnectionString => ConnectionStringFor(DatabaseRoles.Worker, WorkerPassword);

    public string ReadonlyConnectionString => ConnectionStringFor(DatabaseRoles.Readonly, ReadonlyPassword);

    public string MigratorConnectionString => ConnectionStringFor(DatabaseRoles.Migrator, MigratorPassword);

    /// <summary>The connection string of a running role by name: <c>lt2_app</c>, <c>lt2_worker</c> or <c>lt2_readonly</c>.</summary>
    public string ConnectionStringFor(string role) => role switch
    {
        DatabaseRoles.App => AppConnectionString,
        DatabaseRoles.Worker => WorkerConnectionString,
        DatabaseRoles.Readonly => ReadonlyConnectionString,
        DatabaseRoles.Migrator => MigratorConnectionString,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Admin"] = AdminConnectionString,
                ["ConnectionStrings:Lt2"] = ConnectionStringFor(DatabaseRoles.Migrator, MigratorPassword),
                ["Database:Password"] = MigratorPassword,
                ["Database:AppPassword"] = AppPassword,
                ["Database:WorkerPassword"] = WorkerPassword,
                ["Database:ReadonlyPassword"] = ReadonlyPassword,
            })
            .Build();
        await MigrationRunner.RunAsync(configuration, ["--seed-invitation", TestData.Invitation + ":1000"], CancellationToken.None);

        // Running the migrator twice must change nothing (it runs on every deploy).
        await MigrationRunner.RunAsync(configuration, ["--seed-invitation", TestData.Invitation + ":1000"], CancellationToken.None);

        // The invented parliament, before any test host starts (pages cache what they read for a minute).
        await ParliamentSeed.EnsureAsync(this);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) =>
        await ScalarAsync<object>(sql, parameters);
}

/// <summary>Fictional test data (names from the design's examples).</summary>
public static class TestData
{
    public const string Invitation = "BANDYMAS-2026";

    /// <summary>A unique fictional address on the reserved <c>.test</c> domain.</summary>
    public static string Email(string name = "ruta.mockute") => $"{name}.{Guid.NewGuid():N}@pavyzdys.test";
}
