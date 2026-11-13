using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 9.5: a signed-in browser's downloads of static files (the WebAssembly runtime, styles) touch no database, an
/// ended session still stops at once, and a burst of requests waits for the pool instead of opening more connections
/// than the budget gives.
/// </summary>
public sealed class ConnectionUseTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>Real routes from the build's static asset manifest: what a WebAssembly start downloads, and the styles.</summary>
    private static IReadOnlyList<string> StaticRoutes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Lt2.Web.staticwebassets.endpoints.json")));
        var routes = manifest.RootElement.GetProperty("Endpoints").EnumerateArray()
            .Where(e => e.GetProperty("Selectors").GetArrayLength() == 0)
            .Select(e => e.GetProperty("Route").GetString()!)
            .ToList();
        string First(string pattern) => routes.First(r => Regex.IsMatch(r, pattern));
        return
        [
            First(@"^_framework/blazor\.web\.js$"),
            First(@"^_framework/dotnet\.js$"),
            First(@"^_framework/Lt2\.Web\.Client\.[^/]+\.wasm$"),
            First(@"^_framework/System\.Private\.CoreLib\.[^/]+\.wasm$"),
            First(@"^_framework/icudt[^/]*\.dat$"),
            First(@"^css/lt2\.css$"),
        ];
    }

    [Fact]
    public async Task A_signed_in_browsers_request_for_a_static_file_runs_no_database_command_and_an_ended_session_still_stops_at_once()
    {
        using var commands = new CommandCounter();
        await using var factory = new StaticFilesFactory(Database);
        using var browser = factory.NewBrowser();
        var email = await browser.RegisterAndSignInAsync();

        // The counter does see a signed-in API request's session lookup, so a zero below means something.
        var (me, meCommands) = await commands.GetAsync(browser.Client, ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        meCommands.ShouldBeGreaterThan(0, "the session is looked up in the database for an API request");

        foreach (var route in StaticRoutes())
        {
            var (file, count) = await commands.GetAsync(browser.Client, "/" + route);
            file.StatusCode.ShouldBe(HttpStatusCode.OK, route);
            count.ShouldBe(0, $"{route} is served without the database");
            file.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"], "the security headers still apply");
        }

        // Ending the session (here: removed on the server, as "my sessions" or a sign-out elsewhere does) stops the API
        // at the very next request; static files never needed it.
        await Database.ExecuteAsync(
            "DELETE FROM identity.session WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));
        using (var after = await browser.Client.GetAsync(ApiRoutes.Me.Root))
        {
            after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var stillServed = await browser.Client.GetAsync("/" + StaticRoutes()[0]);
        stillServed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Many_parallel_signed_in_requests_wait_for_the_pool_and_never_open_more_connections_than_it_allows()
    {
        const int Pool = 3;
        var application = $"lt2-pool-{Guid.NewGuid():N}"[..24];
        await using var factory = new SizedPoolFactory(Database, Pool, application);
        using var browser = factory.NewBrowser();
        await browser.RegisterAndSignInAsync();

        const string Open = "SELECT count(*) FROM pg_stat_activity WHERE application_name = @a";
        var peak = 0L;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, await Database.ScalarAsync<long>(Open, ("a", application)));
                await Task.Delay(10);
            }
        });

        var statuses = await Task.WhenAll(Enumerable.Range(0, 60).Select(async i =>
        {
            using var response = await browser.Client.GetAsync(i % 2 == 0 ? ApiRoutes.Me.Root : ApiRoutes.Me.Sessions);
            return response.StatusCode;
        }));
        await stop.CancelAsync();
        await sampler;

        statuses.ShouldAllBe(s => s == HttpStatusCode.OK, "a burst waits for a free connection; nothing fails");
        var open = await Database.ScalarAsync<long>(Open, ("a", application));
        open.ShouldBeInRange(1, Pool, "the pool keeps its connections open, never more than its size");
        peak.ShouldBeLessThanOrEqualTo(Pool);
    }

    /// <summary>
    /// The web application serving the WebAssembly files from the build, as the published site serves them from its
    /// wwwroot (outside Development the test host would not find files that live in other projects' build output).
    /// </summary>
    private sealed class StaticFilesFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseStaticWebAssets();
        }
    }

    /// <summary>The web application with a small pool under its own name in <c>pg_stat_activity</c>.</summary>
    private sealed class SizedPoolFactory(DatabaseFixture database, int pool, string application) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Database:MaxPoolSize", pool.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Database:ApplicationName", application);
        }
    }

    /// <summary>
    /// Counts the database commands run while answering one request: each request carries its own trace id, and Npgsql
    /// reports every command as an activity in the trace of the request that ran it.
    /// </summary>
    private sealed class CommandCounter : IDisposable
    {
        private readonly ConcurrentDictionary<ActivityTraceId, int> _counts = new();
        private readonly ActivityListener _listener;

        public CommandCounter()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is "Npgsql" or "Microsoft.AspNetCore",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.Source.Name == "Npgsql")
                    {
                        _counts.AddOrUpdate(activity.TraceId, 1, (_, n) => n + 1);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public async Task<(HttpResponseMessage Response, int Commands)> GetAsync(HttpClient client, string path)
        {
            var trace = ActivityTraceId.CreateRandom();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("traceparent", $"00-{trace}-{ActivitySpanId.CreateRandom()}-01");
            var response = await client.SendAsync(request);
            await response.Content.LoadIntoBufferAsync();
            return (response, _counts.GetValueOrDefault(trace));
        }

        public void Dispose() => _listener.Dispose();
    }
}
