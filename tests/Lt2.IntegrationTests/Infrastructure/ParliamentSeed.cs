using Lt2.Parliament;
using Lt2.Parliament.Import.Client;
using Lt2.SeimasStandIn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// The invented parliament in the test database: the real import, as the worker runs it, against the in-process
/// stand-in, once per test run. Afterwards the parliament data is marked invented (stored responses are synthetic).
/// </summary>
public static class ParliamentSeed
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _done;

    public static async Task EnsureAsync(DatabaseFixture database)
    {
        ArgumentNullException.ThrowIfNull(database);
        await Gate.WaitAsync();
        try
        {
            if (_done)
            {
                return;
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seimas:BaseAddress"] = "http://seimas-standin.test/sip/",
                ["Seimas:MinimumIntervalMilliseconds"] = "0",
                ["Outbound:AllowedHosts:0"] = "seimas-standin.test",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            services.AddSingleton(_ => new NpgsqlDataSourceBuilder(database.WorkerConnectionString).Build());
            services.AddParliamentModule();
            services.AddParliamentImport(configuration);
            var standIn = new StandInHandler(StandInResponses.Load(SyntheticDirectory()));
            services.AddHttpClient(SeimasClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => standIn);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<IParliamentImport>().RunAsync(ImportParts.None, CancellationToken.None);
            if (outcome.Status != "succeeded")
            {
                throw new InvalidOperationException($"Seeding the invented parliament failed: {outcome.Error}");
            }

            _done = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string SyntheticDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LT2.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "tests", "Lt2.Parliament.ImportTests", "Seimas", "Synthetic");
    }
}
