using System.Net;
using System.Text;
using Lt2.Parliament.Import.Client;
using Lt2.SharedKernel.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lt2.Parliament.ImportTests.Client;

/// <summary>Design 3 and 7.2: the client retries, times out, stays on the allowlist and reports an outage as one exception.</summary>
public sealed class SeimasClientTests
{
    private const string Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><SeimoInformacija/>";

    [Fact]
    public async Task Each_call_of_the_chain_asks_for_the_service_path_and_query_of_the_open_data_service()
    {
        var handler = new ScriptedHandler(_ => Ok(Xml));
        var client = Build(handler);

        await client.GetTermsAsync(default);
        await client.GetMembersAsync(10, default);
        await client.GetSessionsAsync(10, default);
        await client.GetSittingsAsync(-501, default);
        await client.GetAgendaAsync(-502, default);
        await client.GetProceedingsAsync(-502, default);
        await client.GetVoteResultsAsync(-60034, default);

        handler.Requests.Select(u => u.PathAndQuery).ShouldBe(
        [
            "/sip/p2b.ad_seimo_kadencijos",
            "/sip/p2b.ad_seimo_nariai?kadencijos_id=10",
            "/sip/p2b.ad_seimo_sesijos?kadencijos_id=10",
            "/sip/p2b.ad_seimo_posedziai?sesijos_id=-501",
            "/sip/p2b.ad_sp_darbotvarke?posedzio_id=-502",
            "/sip/p2b.ad_sp_klausimo_svarstymo_eiga?posedzio_id=-502",
            "/sip/p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=-60034",
        ]);
        handler.Requests.ShouldAllBe(u => u.Host == "seimas.test");
    }

    [Fact]
    public async Task The_parameter_of_the_proceedings_service_is_configuration_until_it_is_confirmed()
    {
        var handler = new ScriptedHandler(_ => Ok(Xml));
        var client = Build(handler, ("Seimas:ProceedingsParameter", "darbotvarkes_klausimo_id"));

        await client.GetProceedingsAsync(77, default);

        handler.Requests.Single().PathAndQuery.ShouldBe("/sip/p2b.ad_sp_klausimo_svarstymo_eiga?darbotvarkes_klausimo_id=77");
    }

    [Fact]
    public async Task A_dropped_connection_is_retried_and_the_answer_returned()
    {
        var handler = new ScriptedHandler(n => n <= 2 ? throw new HttpRequestException("connection reset") : Ok(Xml));
        var client = Build(handler);

        var response = await client.GetTermsAsync(default);

        handler.Requests.Count.ShouldBe(3);
        Encoding.UTF8.GetString(response.Body).ShouldBe(Xml);
        response.Synthetic.ShouldBeFalse();
    }

    [Fact]
    public async Task A_server_error_is_retried()
    {
        var handler = new ScriptedHandler(n => n == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(Xml));
        var client = Build(handler);

        await client.GetTermsAsync(default);

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_outage_that_outlasts_the_retries_ends_in_one_unavailable_exception()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = Build(handler);

        var failure = await Should.ThrowAsync<SeimasUnavailableException>(() => client.GetMembersAsync(10, default));

        failure.Request.ShouldBe(SeimasRequest.Members(10));
        handler.Requests.Count.ShouldBe(1 + 3);
    }

    [Fact]
    public async Task A_service_that_does_not_answer_times_out_and_is_retried()
    {
        var handler = new ScriptedHandler(n => n == 1 ? Hang() : Ok(Xml));
        var client = Build(handler);

        await client.GetTermsAsync(default);

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_body_that_is_not_well_formed_xml_is_asked_for_again()
    {
        var handler = new ScriptedHandler(n => n == 1 ? Ok("<SeimoInformacija><BendriBalsav") : Ok(Xml));
        var client = Build(handler);

        await client.GetVoteResultsAsync(-60034, default);

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_body_that_stays_malformed_is_an_outage()
    {
        var handler = new ScriptedHandler(_ => Ok("<html>Paslauga laikinai neveikia"));
        var client = Build(handler);

        await Should.ThrowAsync<SeimasUnavailableException>(() => client.GetTermsAsync(default));
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_base_address_off_the_allowlist_is_refused_before_any_request_is_made()
    {
        var handler = new ScriptedHandler(_ => Ok(Xml));
        var client = Build(handler, ("Seimas:BaseAddress", "https://kitur.test/sip/"));

        await Should.ThrowAsync<OutboundHostNotAllowedException>(() => client.GetTermsAsync(default));

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_response_from_the_stand_in_is_marked_synthetic_by_its_header()
    {
        var handler = new ScriptedHandler(_ =>
        {
            var response = Ok(Xml);
            response.Headers.Add(SyntheticMarker.Header, "1");
            return response;
        });
        var client = Build(handler);

        (await client.GetTermsAsync(default)).Synthetic.ShouldBeTrue();
    }

    [Fact]
    public async Task A_response_carrying_the_synthetic_comment_is_marked_synthetic_without_the_header()
    {
        var handler = new ScriptedHandler(_ => Ok("<?xml version=\"1.0\"?><!-- LT2-SYNTHETIC --><SeimoInformacija/>"));
        var client = Build(handler);

        (await client.GetTermsAsync(default)).Synthetic.ShouldBeTrue();
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_minimum_interval()
    {
        var handler = new ScriptedHandler(_ => Ok(Xml));
        var client = Build(handler, ("Seimas:MinimumIntervalMilliseconds", "150"));
        var started = DateTimeOffset.UtcNow;

        await client.GetTermsAsync(default);
        await client.GetTermsAsync(default);
        await client.GetTermsAsync(default);

        (DateTimeOffset.UtcNow - started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(290));
    }

    internal static ISeimasClient Build(HttpMessageHandler handler, params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Seimas:BaseAddress"] = "https://seimas.test/sip/",
            ["Seimas:AttemptTimeoutSeconds"] = "0.3",
            ["Seimas:TotalTimeoutSeconds"] = "10",
            ["Seimas:RetryAttempts"] = "3",
            ["Seimas:RetryDelaySeconds"] = "0.01",
            ["Seimas:MinimumIntervalMilliseconds"] = "0",
            ["Outbound:AllowedHosts:0"] = "seimas.test",
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddParliamentImport(configuration);
        services.AddHttpClient(SeimasClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        return services.BuildServiceProvider().GetRequiredService<ISeimasClient>();
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };

    /// <summary>A request the service never answers: the handler waits until the attempt is cancelled.</summary>
    private static HttpResponseMessage Hang() => ScriptedHandler.NoAnswer;

    /// <summary>Answers the n-th request (1-based) with the given function and records every address asked for.</summary>
    internal sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public static readonly HttpResponseMessage NoAnswer = new(HttpStatusCode.OK);

        private int _count;

        public List<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!);
            }

            var n = Interlocked.Increment(ref _count);
            var response = respond(n);
            if (ReferenceEquals(response, NoAnswer))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return response;
        }
    }
}
