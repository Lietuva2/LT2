using System.Net;
using System.Text.RegularExpressions;
using Hangfire;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Jobs;
using Lt2.Web.Jobs;
using Lt2.Web.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// The administrators' Hangfire dashboard (design 3, 6.3; owner 2026-11-18), <c>/administravimas/darbai</c>: only an
/// administrator in a staff-passkey session gets it; its writes need this site's Origin and the antiforgery token, and each
/// one (not the counters' poll) is audited without details. It reads the worker's Hangfire tables as <c>lt2_app</c>.
/// </summary>
public sealed partial class JobsDashboardTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Dashboard = JobsDashboard.Path;
    private const string Trigger = Dashboard + "/recurring/trigger";

    [GeneratedRegex("<meta name=\"csrf-header\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfHeader();

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfToken();

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_never_gets_the_dashboard()
    {
        using var visitor = NewBrowser();
        foreach (var path in new[] { Dashboard, Dashboard + "/recurring", Dashboard + "/jobs/failed" })
        {
            using var response = await visitor.Client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            response.Headers.Location!.AbsolutePath.ShouldBe("/prisijungti");
            (await response.Content.ReadAsStringAsync()).ShouldNotContain("hangfire", Case.Insensitive);
        }

        using var post = await PostFormAsync(visitor, Trigger, token: null);
        post.StatusCode.ShouldNotBe(HttpStatusCode.NoContent);
        (await post.Content.ReadAsStringAsync()).ShouldNotContain("hangfire", Case.Insensitive);
    }

    [Fact]
    public async Task A_member_is_refused()
    {
        using var member = NewBrowser();
        await member.RegisterAndSignInAsync();

        await ShouldBeRefusedAsync(member);
    }

    [Fact]
    public async Task An_active_administrator_signed_in_with_a_code_is_refused()
    {
        using var admin = await AdministratorAsync();
        using var codeSession = NewBrowser();
        using (await codeSession.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = admin.Email }))
        {
        }

        await codeSession.SignInWithCodeAsync(admin.Email);

        await ShouldBeRefusedAsync(codeSession);
    }

    [Fact]
    public async Task An_administrator_in_a_staff_passkey_session_gets_the_dashboard_with_its_own_headers()
    {
        using var admin = await AdministratorAsync();

        using var response = await admin.Browser.Client.GetAsync(Dashboard);
        var html = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        html.ShouldContain("Foniniai darbai");
        CsrfToken().IsMatch(html).ShouldBeTrue("Hangfire puts the antiforgery token in the page for its forms");
        html.ShouldNotContain("Host=", customMessage: "the storage's connection is not shown");

        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.GetValues("X-Robots-Tag").ShouldBe(["noindex, nofollow"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(SecurityHeaders.JobsDashboardContentSecurityPolicy);

        // The dashboard's own script and style, from this host, under the same headers.
        foreach (Match asset in Regex.Matches(html, "(?:src|href)=\"(" + Regex.Escape(Dashboard) + "/(?:js|css)[^\"]+)\""))
        {
            using var file = await admin.Browser.Client.GetAsync(WebUtility.HtmlDecode(asset.Groups[1].Value));
            file.StatusCode.ShouldBe(HttpStatusCode.OK, asset.Groups[1].Value);
            file.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }

        using var recurring = await admin.Browser.Client.GetAsync(Dashboard + "/recurring");
        recurring.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Every other page keeps the site's policy, with no inline style.
        using var settings = await admin.Browser.Client.GetAsync("/administravimas/nustatymai");
        settings.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(SecurityHeaders.ContentSecurityPolicy);
    }

    [Fact]
    public async Task A_write_without_the_antiforgery_token_or_from_another_origin_is_refused_and_not_audited()
    {
        using var admin = await AdministratorAsync();
        var before = await AuditCountAsync();
        var (header, token) = await TokenAsync(admin.Browser);

        using (var withoutToken = await PostFormAsync(admin.Browser, Trigger, token: null))
        {
            withoutToken.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var wrongToken = await PostFormAsync(admin.Browser, Trigger, (header, "not-a-token")))
        {
            wrongToken.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var otherOrigin = await PostFormAsync(admin.Browser, Trigger, (header, token), origin: "https://elsewhere.test"))
        {
            otherOrigin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var noOrigin = await PostFormAsync(admin.Browser, Trigger, (header, token), origin: ""))
        {
            noOrigin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task An_administrator_triggers_the_parliament_import_for_the_worker_and_the_action_is_audited_without_details()
    {
        using var admin = await AdministratorAsync();
        const string id = "parliament-import";

        // The worker registers its recurring jobs at start-up; here the test does, through the web host's own storage (lt2_app).
        var jobs = Factory.Services.GetRequiredService<IRecurringJobManager>();
        jobs.AddOrUpdate<ParliamentImportJob>(id, job => job.RunAsync(null, CancellationToken.None), "*/15 * * * *");
        try
        {
            var enqueued = await EnqueuedImportsAsync();
            var audited = await AuditCountAsync();
            var (header, token) = await TokenAsync(admin.Browser);

            // The counters' poll is a read: allowed, not audited.
            using (var poll = await PostFormAsync(admin.Browser, Dashboard + "/stats", (header, token), "metrics[]=enqueued:count"))
            {
                poll.StatusCode.ShouldBe(HttpStatusCode.OK, await poll.Content.ReadAsStringAsync());
            }

            (await AuditCountAsync()).ShouldBe(audited);

            using (var trigger = await PostFormAsync(admin.Browser, Trigger, (header, token), "jobs[]=" + id))
            {
                trigger.StatusCode.ShouldBe(HttpStatusCode.NoContent, await trigger.Content.ReadAsStringAsync());
            }

            // Enqueued for the worker (no Hangfire server runs in the web host, so it waits there).
            (await EnqueuedImportsAsync()).ShouldBe(enqueued + 1);
            (await AuditCountAsync()).ShouldBe(audited + 1);
            var entry = await Database.ScalarAsync<string>(
                "SELECT concat_ws('|', actor_role, coalesce(target, '-'), coalesce(details::text, '-'), (ip_hash IS NOT NULL)::text) FROM audit.event WHERE action = @a ORDER BY occurred_at DESC, id DESC LIMIT 1",
                ("a", JobsDashboard.AuditAction));
            entry.ShouldBe("administrator|-|-|true");
        }
        finally
        {
            jobs.RemoveIfExists(id);
        }
    }

    [Fact]
    public async Task The_imports_manual_jobs_are_listed_never_scheduled_and_an_administrator_triggers_one_audited()
    {
        using var admin = await AdministratorAsync();
        string[] ids = [ParliamentImportPartsJob.Members, ParliamentImportPartsJob.Sittings, ParliamentImportPartsJob.Votes, ParliamentImportPartsJob.Everything];

        // The worker's own registration (design 7.2, owner 2026-11-18), here through the web host's storage.
        var jobs = Factory.Services.GetRequiredService<IRecurringJobManager>();
        ParliamentImportPartsJob.Register(jobs, Lt2.SharedKernel.VilniusTime.Zone);
        try
        {
            ids.ShouldBe(["parliament-members", "parliament-sittings", "parliament-votes", "parliament-everything"]);
            foreach (var id in ids)
            {
                // Never due: no next execution, below every score the scheduler takes (from 0 to now).
                (await Database.ScalarAsync<string>("SELECT value FROM hangfire.hash WHERE key = @k AND field = 'NextExecution'", ("k", "recurring-job:" + id))).ShouldBeNullOrEmpty(id);
                (await Database.ScalarAsync<double>("SELECT score FROM hangfire.set WHERE key = 'recurring-jobs' AND value = @v", ("v", id))).ShouldBeLessThan(0, id);
            }

            var page = WebUtility.HtmlDecode(await admin.Browser.Client.GetStringAsync(Dashboard + "/recurring"));
            foreach (var name in new[]
                     {
                         "Seimas import by hand: MPs (terms, MP list with memberships and contacts, sessions)",
                         "Seimas import by hand: sittings (every sitting list, agenda and proceedings)",
                         "Seimas import by hand: votes (every per-MP result; run after the MPs are imported)",
                         "Seimas import by hand: everything (MPs, then sittings, then votes)",
                     })
            {
                page.ShouldContain(name);
            }

            var enqueued = await EnqueuedAsync("EverythingAsync");
            var audited = await AuditCountAsync();
            var (header, token) = await TokenAsync(admin.Browser);
            using (var trigger = await PostFormAsync(admin.Browser, Trigger, (header, token), "jobs[]=" + ParliamentImportPartsJob.Everything))
            {
                trigger.StatusCode.ShouldBe(HttpStatusCode.NoContent, await trigger.Content.ReadAsStringAsync());
            }

            (await EnqueuedAsync("EverythingAsync")).ShouldBe(enqueued + 1);
            (await AuditCountAsync()).ShouldBe(audited + 1);
        }
        finally
        {
            foreach (var id in ids)
            {
                jobs.RemoveIfExists(id);
            }
        }
    }

    private static async Task ShouldBeRefusedAsync(TestBrowser browser)
    {
        foreach (var path in new[] { Dashboard, Dashboard + "/recurring" })
        {
            using var response = await browser.Client.GetAsync(path);
            response.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Forbidden);
            if (response.StatusCode == HttpStatusCode.Redirect)
            {
                response.Headers.Location!.OriginalString.ShouldNotContain(Dashboard);
            }

            (await response.Content.ReadAsStringAsync()).ShouldNotContain("hangfire", Case.Insensitive);
        }

        using var post = await PostFormAsync(browser, Trigger, token: null, "jobs[]=parliament-import");
        post.StatusCode.ShouldNotBe(HttpStatusCode.NoContent);
        (await post.Content.ReadAsStringAsync()).ShouldNotContain("hangfire", Case.Insensitive);
    }

    /// <summary>The antiforgery header's name and the token, as the dashboard's page gives them to its script.</summary>
    private static async Task<(string Header, string Token)> TokenAsync(TestBrowser browser)
    {
        var html = await browser.Client.GetStringAsync(Dashboard);
        return (WebUtility.HtmlDecode(CsrfHeader().Match(html).Groups[1].Value), WebUtility.HtmlDecode(CsrfToken().Match(html).Groups[1].Value));
    }

    /// <summary>A form the dashboard's script posts, with the browser's Origin unless another is given ("" for none).</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        TestBrowser browser, string path, (string Header, string Token)? token, string form = "", string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(form, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        if (token is { } t)
        {
            request.Headers.Add(t.Header, t.Token);
        }

        if (origin is not null)
        {
            browser.Client.DefaultRequestHeaders.Remove("Origin");
            if (origin.Length > 0)
            {
                request.Headers.Add("Origin", origin);
            }
        }

        try
        {
            return await browser.Client.SendAsync(request);
        }
        finally
        {
            if (origin is not null)
            {
                browser.Client.DefaultRequestHeaders.Add("Origin", Lt2Factory.Origin);
            }
        }
    }

    private Task<long> AuditCountAsync() =>
        Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = @a", ("a", JobsDashboard.AuditAction));

    private Task<long> EnqueuedImportsAsync() =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM hangfire.job WHERE statename = 'Enqueued' AND invocationdata::text LIKE '%Lt2.Jobs.ParliamentImportJob%'");

    private Task<long> EnqueuedAsync(string method) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM hangfire.job WHERE statename = 'Enqueued' AND invocationdata::text LIKE '%Lt2.Jobs.ParliamentImportPartsJob%' AND invocationdata::text LIKE @m",
            ("m", "%\"" + method + "\"%"));
}
