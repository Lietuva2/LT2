using Lt2.Parliament.Import.Client;
using Lt2.Parliament.ImportTests.Client;
using Lt2.Parliament.ImportTests.StandIn;
using Lt2.SeimasStandIn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lt2.Parliament.ImportTests.Live;

/// <summary>
/// The contract with the real Seimas open data. Skipped unless <c>LT2_SEIMAS_LIVE=1</c>: it fetches the real chain
/// (about 25 requests, one a second), saves every response into Seimas/Recorded/, and gives every line of the register a
/// verdict in Seimas/Recorded/contract-report.md. It fails when a line is contradicted or a parser fails, naming them.
/// Optional: <c>LT2_SEIMAS_BASE_ADDRESS</c> (default https://apps.lrs.lt/sip/).
/// </summary>
public sealed class LiveContractTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_live_service_confirms_every_line_of_the_register()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LT2_SEIMAS_LIVE") == "1",
            "Set LT2_SEIMAS_LIVE=1 to check the register against the real Seimas open data.");

        var baseAddress = Environment.GetEnvironmentVariable("LT2_SEIMAS_BASE_ADDRESS") ?? "https://apps.lrs.lt/sip/";
        var client = LiveClient(new Uri(baseAddress));

        var result = await ContractRun.RunAsync(client, 10, StandInFixture.RecordedDirectory, baseAddress, TimeProvider.System, TestContext.Current.CancellationToken);

        output.WriteLine(result.Report);
        result.FetchErrors.ShouldBeEmpty();
        result.ParseErrors.ShouldBeEmpty();
        result.Contradicted.Select(v => $"{v.Line}: {v.Detail}").ShouldBeEmpty();
    }

    /// <summary>
    /// The same run against the stand-in. The synthetic data contradicts only R19 by design: its totals are
    /// Seimas-wide, its per-MP rows cover 13 MPs.
    /// </summary>
    [Fact]
    public async Task The_contract_run_against_the_stand_in_saves_every_response_and_contradicts_nothing_but_the_synthetic_totals()
    {
        var saveTo = Path.Combine(Path.GetTempPath(), "lt2-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = SeimasClientTests.Build(new StandInHandler(StandInFixture.Responses()));

            var result = await ContractRun.RunAsync(client, 10, saveTo, "stand-in", TimeProvider.System, TestContext.Current.CancellationToken);

            output.WriteLine(result.Report);
            result.FetchErrors.ShouldBeEmpty();
            result.ParseErrors.ShouldBeEmpty();
            result.Contradicted.Select(v => v.Line.Id).ShouldBe(["R19"]);
            result.Verdicts.Count(v => v.Verdict == Verdict.Confirmed).ShouldBeGreaterThan(70);
            File.Exists(Path.Combine(saveTo, "contract-report.md")).ShouldBeTrue();
            File.ReadAllBytes(Path.Combine(saveTo, "p2b.ad_sp_balsavimo_rezultatai@balsavimo_id=99100102.xml"))
                .ShouldBe(File.ReadAllBytes(Path.Combine(StandInFixture.SyntheticDirectory, "p2b.ad_sp_balsavimo_rezultatai@balsavimo_id=99100102.xml")));
        }
        finally
        {
            Directory.Delete(saveTo, recursive: true);
        }
    }

    private static ISeimasClient LiveClient(Uri baseAddress)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seimas:BaseAddress"] = baseAddress.ToString(),
            ["Seimas:MinimumIntervalMilliseconds"] = "1000",
            ["Outbound:AllowedHosts:0"] = baseAddress.IdnHost,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddParliamentImport(configuration);
        return services.BuildServiceProvider().GetRequiredService<ISeimasClient>();
    }
}
