using System.Text;
using Lt2.Parliament.Import.Client;
using Lt2.Parliament.ImportTests.Client;
using Lt2.SeimasStandIn;

namespace Lt2.Parliament.ImportTests.StandIn;

/// <summary>The stand-in answers the real service's paths and query strings, and everything it says is marked invented.</summary>
public sealed class StandInTests
{
    [Fact]
    public async Task The_client_reaches_every_service_of_the_chain_on_the_stand_in_and_every_answer_is_marked_synthetic()
    {
        var standIn = new StandInHandler(StandInFixture.Responses());
        var client = SeimasClientTests.Build(standIn);

        var responses = new[]
        {
            await client.GetAsync(SeimasRequest.Terms(), default),
            await client.GetAsync(SeimasRequest.Members(10), default),
            await client.GetAsync(SeimasRequest.Sessions(10), default),
            await client.GetAsync(SeimasRequest.Sittings(99000101), default),
            await client.GetAsync(SeimasRequest.Agenda(99001001), default),
            await client.GetAsync(SeimasRequest.Proceedings(99001001), default),
            await client.GetAsync(SeimasRequest.VoteResults(99100002), default),
        };

        responses.ShouldAllBe(r => r.Synthetic);
        responses.ShouldAllBe(r => Encoding.UTF8.GetString(r.Body).Contains(SyntheticMarker.Comment));
        Encoding.UTF8.GetString(responses[6].Body).ShouldContain("už=\"71\" prieš=\"38\"");
    }

    [Fact]
    public async Task An_unknown_identifier_of_a_known_service_gets_an_empty_synthetic_document()
    {
        var client = SeimasClientTests.Build(new StandInHandler(StandInFixture.Responses()));

        var response = await client.GetAsync(SeimasRequest.VoteResults(12345), default);

        Encoding.UTF8.GetString(response.Body).ShouldContain("<SeimoInformacija/>");
        response.Synthetic.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_service_is_not_found()
    {
        var answer = StandInHandler.Answer(new Uri("http://seimas.test/sip/p2b.nera_tokios"), StandInFixture.Responses());

        answer.Status.ShouldBe(404);
    }

    [Fact]
    public void File_names_map_to_the_path_and_query_of_the_request()
    {
        StandInResponses.KeyFromFileName("p2b.ad_seimo_nariai@kadencijos_id=10").ShouldBe("p2b.ad_seimo_nariai?kadencijos_id=10");
        StandInResponses.KeyFromFileName("p2b.ad_seimo_kadencijos").ShouldBe("p2b.ad_seimo_kadencijos");
    }

    [Fact]
    public void Dated_answers_name_days_relative_to_today_and_past_sitting_days_are_tuesdays_and_thursdays()
    {
        var wednesday = new DateOnly(2026, 11, 18);
        StandInResponses.RenderDays("{{day:-9}} {{past:1}} {{past:2}} {{past:4}}", wednesday).ShouldBe("2026-11-09 2026-11-17 2026-11-12 2026-11-05");

        // On a sitting day itself, that day is not yet past.
        StandInResponses.RenderDays("{{past:1}}", new DateOnly(2026, 11, 19)).ShouldBe("2026-11-17");
    }

    [Fact]
    public void Every_canned_response_says_it_is_synthetic_in_its_first_comment()
    {
        foreach (var file in Directory.EnumerateFiles(StandInFixture.SyntheticDirectory, "*.xml"))
        {
            var head = string.Join('\n', File.ReadLines(file).Take(3));
            head.ShouldContain("<!-- LT2-SYNTHETIC", customMessage: Path.GetFileName(file));
            head.ShouldContain("NOT a recorded Seimas response", customMessage: Path.GetFileName(file));
        }
    }

    [Fact]
    public void No_recorded_response_is_marked_synthetic_and_no_synthetic_response_sits_with_the_recorded_ones()
    {
        foreach (var file in Directory.EnumerateFiles(StandInFixture.RecordedDirectory, "*.xml"))
        {
            File.ReadAllText(file).ShouldNotContain(SyntheticMarker.Comment, customMessage: Path.GetFileName(file));
        }
    }
}

/// <summary>Where the synthetic and recorded responses live in the source tree.</summary>
internal static class StandInFixture
{
    public static string SeimasDirectory { get; } = Path.Combine(RepositoryRoot(), "tests", "Lt2.Parliament.ImportTests", "Seimas");

    public static string SyntheticDirectory => Path.Combine(SeimasDirectory, "Synthetic");

    public static string RecordedDirectory => Path.Combine(SeimasDirectory, "Recorded");

    /// <summary>A fresh copy of the canned answers; a test may change its own copy.</summary>
    public static StandInResponses Responses() => StandInResponses.Load(SyntheticDirectory);

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LT2.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
