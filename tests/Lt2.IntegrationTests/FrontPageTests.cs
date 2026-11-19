using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.7, the reworked front page, over HTTP: the agenda answer carries the organisations with a public statement on
/// each bill, ordered as on the bill's page; the member's own answer (<c>/me/bills</c>) holds only the caller's own side and
/// follows; the page's HTML is the same for every member and holds nothing of anyone's own; and the public answer the page
/// is built from is kept for a minute and holds nothing per person.
/// </summary>
public sealed partial class FrontPageTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task The_agenda_lists_a_bills_organisations_as_its_page_orders_them_and_none_blocked_hidden_or_closed()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ClearStatementsAsync(AppealSteps.SecondBill);
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(3));
        var moderator = await ModeratorAsync();

        // An account registers at most three organisations (Organisations:PerAccount): a new representative for every three.
        var reps = new List<TestBrowser>();
        var registered = 0;
        async Task<(TestBrowser Rep, OrganisationCreatedResponse Created)> RegisterAsync()
        {
            if (registered++ % 3 == 0)
            {
                reps.Add((await Factory.MemberAsync()).Browser);
            }

            return (reps[^1], await reps[^1].RegisterOrganisationAsync());
        }

        try
        {
            // Published in this order: remarks, against, for, for; then one blocked, one hidden and one closed.
            var shown = new List<(string Slug, string Stance)>();
            foreach (var stance in new[] { "remarks", "against", "for", "for" })
            {
                var (rep, created) = await RegisterAsync();
                await rep.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, stance, "Pozicija sąrašui (bandymas).");
                shown.Add((created.Slug, stance));
            }

            var (blockedRep, blocked) = await RegisterAsync();
            await blockedRep.PublishStatementAsync(AppealSteps.SecondBill, blocked.Slug, "for", "Pozicija, kurios organizacija užblokuota.");
            await moderator.Browser.ActAsync(OrganisationRoutes.Block, blocked.Id);
            var (hiddenRep, hidden) = await RegisterAsync();
            var hiddenStatement = await hiddenRep.PublishStatementAsync(AppealSteps.SecondBill, hidden.Slug, "against", "Pozicija, kurią moderatorius paslėpė.");
            await moderator.Browser.ActAsync(OrganisationRoutes.HideStatement, hiddenStatement.Id);
            var (closedRep, closed) = await RegisterAsync();
            await closedRep.PublishStatementAsync(AppealSteps.SecondBill, closed.Slug, "remarks", "Pozicija uždarytos organizacijos.");
            using (var closing = await closedRep.PostAsync(OrganisationRoutes.CloseOf(closed.Slug)))
            {
                closing.StatusCode.ShouldBe(HttpStatusCode.NoContent, await closing.Content.ReadAsStringAsync());
            }

            using var visitor = NewBrowser();
            var agenda = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            var item = agenda.Days.SelectMany(d => d.Items).Single(i => i.Number == AppealSteps.SecondBill);
            var expected = shown.OrderBy(s => s.Stance switch { "for" => 0, "against" => 1, _ => 2 }).Select(s => s.Slug).ToList();
            item.Organisations.Select(o => o.Slug).ShouldBe(expected, "for, against, remarks; within each, in the order published");
            item.Organisations.Select(o => o.Stance).ShouldBe(["for", "for", "against", "remarks"]);
            item.Organisations.ShouldAllBe(o => o.Name.Length > 0 && o.Label != null);

            // The same order as the bill's own page lists them.
            var onPage = await visitor.StatementsAsync(AppealSteps.SecondBill);
            onPage.Items.OrderBy(i => i.Statement.Stance switch { "for" => 0, "against" => 1, _ => 2 }).Select(i => i.Slug).ShouldBe(expected);
            item.Organisations.Select(o => o.Slug).ShouldNotContain(blocked.Slug);
            item.Organisations.Select(o => o.Slug).ShouldNotContain(hidden.Slug);
            item.Organisations.Select(o => o.Slug).ShouldNotContain(closed.Slug);

            // The front page names them, each linking to its profile, without a count.
            ForgetFrontPage();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
            foreach (var slug in expected)
            {
                html.ShouldContain($"href=\"/o/{slug}\"");

                // "The row lines up": the stance, then the name and "Nepatvirtinta" together in one cell of their own.
                var stance = shown.Single(s => s.Slug == slug).Stance;
                html.ShouldMatch(
                    $"<span class=\"org-item\"><span class=\"choice choice--{stance}\">[^<]+</span>\\s*<span class=\"org-item__name\"><a href=\"/o/{Regex.Escape(slug)}\">[^<]+</a>\\s*<span class=\"tag[^\"]*\" data-org-label=\"none\">Nepatvirtinta</span>\\s*</span></span>",
                    $"{slug}: the stance before the name, the name and its label in one cell");
            }

            html.ShouldNotContain($"href=\"/o/{blocked.Slug}\"");
        }
        finally
        {
            await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE bill_number = @b", ("b", AppealSteps.SecondBill));
            await Database.UnplanAsync(AppealSteps.SecondBill);
            ForgetFrontPage();
            reps.ForEach(r => r.Dispose());
        }
    }

    [Fact]
    public async Task The_own_answer_holds_only_the_callers_own_side_and_follows()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (first, _) = await Factory.MemberAsync();
        using var _f = first;
        var (second, _) = await Factory.MemberAsync();
        using var _s = second;
        (await first.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await first.FollowBillAsync(AppealSteps.ThirdBill);
        (await second.TakeSideAsync(AppealSteps.SecondBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var query = new MyBillsQuery { Numbers = [AppealSteps.SecondBill, AppealSteps.ThirdBill, AppealSteps.TaxBill] }.ToQueryString();
        var mine = await first.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?" + query);
        mine.Bills.ShouldBe([new MyBillResponse(AppealSteps.SecondBill, "for", true), new MyBillResponse(AppealSteps.ThirdBill, null, true)], "a first side follows the bill (design 8.11.2)");
        var theirs = await second.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?" + query);
        theirs.Bills.ShouldBe([new MyBillResponse(AppealSteps.SecondBill, "against", true)], "the other member's own only");

        // Exactly these fields: nothing else about the bill or anyone.
        using var raw = await first.Client.GetAsync(ApiRoutes.Me.Bills + "?" + query);
        raw.Headers.CacheControl!.NoStore.ShouldBeTrue("a member's own answer is never stored");
        using var document = JsonDocument.Parse(await raw.Content.ReadAsStringAsync());
        document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["bills", "hasPositionConsent", "mpAccount"]);
        document.RootElement.GetProperty("hasPositionConsent").GetBoolean().ShouldBeTrue("the caller's own consent, once on the answer");
        foreach (var row in document.RootElement.GetProperty("bills").EnumerateArray())
        {
            // Design 8.11.8: "new", what was written on a followed bill since the caller last opened it (null when nothing).
            row.EnumerateObject().Select(p => p.Name).ShouldBe(["number", "side", "following", "new"]);
        }

        // Design 8.7: whether the caller gave the positions consent, so a row of the front page knows whether to ask.
        var (unconsented, _) = await Factory.MemberAsync(consent: false);
        using var _u = unconsented;
        var theirsWithout = await unconsented.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?" + query);
        theirsWithout.Bills.ShouldBeEmpty();
        theirsWithout.HasPositionConsent.ShouldBeFalse();
        using (var given = await unconsented.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsentText.Version }))
        {
            given.IsSuccessStatusCode.ShouldBeTrue(await given.Content.ReadAsStringAsync());
        }

        (await unconsented.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?" + query)).HasPositionConsent.ShouldBeTrue("given now");

        using var visitor = NewBrowser();
        using var refused = await visitor.Client.GetAsync(ApiRoutes.Me.Bills + "?" + query);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var none = await first.Client.GetAsync(ApiRoutes.Me.Bills + "?numbers=");
        await none.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        var tooMany = new MyBillsQuery { Numbers = Enumerable.Range(1, MyBillsQuery.MaxNumbers + 1).Select(i => $"XVP-{i:0000}").ToList() };
        using var many = await first.Client.GetAsync(ApiRoutes.Me.Bills + "?" + tooMany.ToQueryString());
        await many.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        using var odd = await first.Client.GetAsync(ApiRoutes.Me.Bills + "?numbers=" + Uri.EscapeDataString("XVP-0412;DROP"));
        await odd.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task The_front_page_is_the_same_for_two_members_and_holds_neither_ones_own()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(2));
        var (first, firstEmail) = await Factory.MemberAsync("Pirmoji Narė");
        using var _f = first;
        var (second, secondEmail) = await Factory.MemberAsync("Antrasis Narys");
        using var _s = second;
        try
        {
            (await first.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await first.FollowBillAsync(AppealSteps.SecondBill);
            var firstCode = (await first.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation)).Code;
            var secondCode = (await second.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation)).Code;
            firstCode.ShouldNotBeNullOrEmpty();
            secondCode.ShouldNotBeNullOrEmpty();

            ForgetFrontPage();
            var firstPage = await first.Client.GetStringAsync("/");
            var secondPage = await second.Client.GetStringAsync("/");


            // The proof that the sweep reads the right page: the member's version, with the bill and the islands' places.
            WebUtility.HtmlDecode(firstPage).ShouldContain("Sveiki sugrįžę");
            firstPage.ShouldMatch("id=\"agenda-\\d{4}-\\d{2}-\\d{2}-" + AppealSteps.SecondBill + "\"");
            firstPage.ShouldNotContain("id=\"home-head\"", customMessage: "no introduction for a member");
            Frameless(firstPage).ShouldBe(Frameless(secondPage), "the same page for every member, apart from the frame");
            foreach (var page in new[] { firstPage, secondPage })
            {
                var text = WebUtility.HtmlDecode(page);
                foreach (var own in new[] { firstCode!, secondCode!, firstEmail, secondEmail, "Pirmoji Narė", "Antrasis Narys" })
                {
                    text.ShouldNotContain(own, Case.Insensitive);
                }

                text.ShouldNotContain("Jūs prašote balsuoti");

                // What the frame carries for the islands: the antiforgery token and the session's account as Blazor
                // serialises it (an opaque account id and the role), nothing else of the person.
                var state = StateOf(page);
                state.ShouldNotContain(firstEmail, Case.Insensitive);
                state.ShouldNotContain(secondEmail, Case.Insensitive);
                state.ShouldNotContain("Narė", Case.Insensitive);
                state.ShouldNotContain("Narys", Case.Insensitive);
                using var auth = JsonDocument.Parse(JsonDocument.Parse(state).RootElement.GetProperty("__internal__AuthenticationState").GetString()!);
                auth.RootElement.GetProperty("claims").EnumerateArray().Select(c => c.GetProperty("type").GetString()![(c.GetProperty("type").GetString()!.LastIndexOf('/') + 1)..])
                    .ShouldBe(["name", "role"]);
                text.ShouldNotContain("id=\"home-mine-");
                text.ShouldNotContain("id=\"my-agenda\"", customMessage: "the panel is filled in the browser only");
            }
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            ForgetFrontPage();
        }
    }

    [Fact]
    public async Task A_visitor_gets_the_introduction_and_the_links_to_sign_in_but_no_form()
    {
        ForgetFrontPage();
        using var visitor = NewBrowser();
        var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
        html.ShouldContain("id=\"home-head\"");
        html.ShouldContain("Kaip tai veikia");
        html.ShouldContain("href=\"/prisijungti\"");
        html.ShouldContain("href=\"/prisijungti?registruotis=true\"");
        html.ShouldContain("href=\"/metodika#privatumas\"");
        html.ShouldNotContain("<form class=\"sheet sheet--cut", customMessage: "no sign-in form on the page");
        html.ShouldNotContain("id=\"email\"");
        html.ShouldNotContain("Aktyviausi projektai");
        html.IndexOf("Kaip tai veikia", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Artimiausiu metu Seime", StringComparison.Ordinal));
        html.IndexOf("Artimiausiu metu Seime", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"not-a-poll\"", StringComparison.Ordinal));

        // Owner, 2026-11-19: no row of links under the agenda ("Visi projektai", the summaries, the MPs); the footer repeats
        // the menu's sections instead (design 8.21, NavigationTests).
        html.ShouldNotContain("id=\"home-more\"");
        html.ShouldNotContain("Visi projektai");
        html.ShouldNotContain("Suvestinės Seimo nariams");
        var main = html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];
        main.ShouldNotContain("href=\"/suvestines\"", customMessage: "the summaries are reached from the menu and the footer");

        // Design 8.15: "Kas yra riba?" once, beside the note under the timeline, with the number that holds now; none per row.
        Regex.Count(html, "class=\"hint__trigger\" id=\"threshold-what\"").ShouldBe(1);
        html.ShouldContain("Riba – dalyvių skaičius (abi pusės kartu), kurį pasiekęs projektas gali patekti į suvestines Seimo nariams. Dabar – ");
        html.IndexOf("id=\"home-older-note\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"threshold-what\"", StringComparison.Ordinal));

        // The member's "Kaip tai veikia" lands on the same steps on the methodology page.
        var method = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/metodika"));
        method.ShouldContain("id=\"kaip-tai-veikia\"");

        // The summary rules' row "Riba" does not repeat its own word.
        method.ShouldMatch("<dt id=\"riba\">Riba</dt><dd class=\"num\">dalyvių skaičius");
        method.ShouldContain("Seimo nariai sužino");
    }

    [Fact]
    public async Task The_front_pages_agenda_is_kept_a_minute_and_holds_nothing_of_the_member_who_filled_it()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(2));
        var (member, email) = await Factory.MemberAsync("Pildanti Narė");
        using var _m = member;
        try
        {
            // A member's visit fills the cache; it holds exactly what a visitor's public answer holds.
            ForgetFrontPage();
            (await member.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await member.Client.GetStringAsync("/");
            using var visitor = NewBrowser();
            var cached = Cached();
            cached.ShouldNotBeNull("the page was built from the cached answer");
            var publicAnswer = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            JsonSerializer.Serialize(cached, JsonSerializerOptions.Web).ShouldBe(JsonSerializer.Serialize(publicAnswer, JsonSerializerOptions.Web), "nothing in it but the public answer");
            JsonSerializer.Serialize(cached, JsonSerializerOptions.Web).ShouldNotContain(email, Case.Insensitive);

            // Within its lifetime the page is built from it: a change that is not a side (here the member withdraws the
            // positions consent, which deletes their side) does not show until it goes. Sides mark it out of date:
            // FrontPageFreshnessTests.
            var countBefore = cached!.Days.SelectMany(d => d.Items).Single(i => i.Number == AppealSteps.SecondBill).Record!.Counts.For;
            using (var withdrawn = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent))
            {
                withdrawn.IsSuccessStatusCode.ShouldBeTrue(await withdrawn.Content.ReadAsStringAsync());
            }

            await visitor.Client.GetStringAsync("/");
            Cached()!.Days.SelectMany(d => d.Items).Single(i => i.Number == AppealSteps.SecondBill).Record!.Counts.For.ShouldBe(countBefore, "reused");
            (await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).Days.SelectMany(d => d.Items)
                .Single(i => i.Number == AppealSteps.SecondBill).Record!.Counts.For.ShouldBe(countBefore - 1, "the API stays live");
            FrontPageAgenda.Lifetime.ShouldBe(TimeSpan.FromSeconds(60));

            // Once it goes, the page reads afresh.
            ForgetFrontPage();
            await visitor.Client.GetStringAsync("/");
            Cached()!.Days.SelectMany(d => d.Items).Single(i => i.Number == AppealSteps.SecondBill).Record!.Counts.For.ShouldBe(countBefore - 1);
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            ForgetFrontPage();
        }
    }

    /// <summary>The page without what the frame varies per request and per session: antiforgery tokens and Blazor's markers.</summary>
    internal static string Frameless(string html)
    {
        html = AntiforgeryValue().Replace(html, "value=\"…\"");
        html = BlazorState().Replace(html, "<!--Blazor-state-->");
        return PrerenderId().Replace(html, "\"prerenderId\":\"…\"");
    }

    /// <summary>The page's persisted component state, each entry decoded: <c>{"key": "decoded value", …}</c> as one JSON text.</summary>
    internal static string StateOf(string html)
    {
        var comment = BlazorState().Match(html);
        comment.Success.ShouldBeTrue("a page with islands carries their state");
        var payload = comment.Value[(comment.Value.IndexOf(':', StringComparison.Ordinal) + 1)..^3];
        using var outer = JsonDocument.Parse(Convert.FromBase64String(payload));
        var entries = outer.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p.Value.GetString()!)));
        return JsonSerializer.Serialize(entries);
    }

    private AgendaResponse? Cached() => Factory.Services.GetRequiredService<IMemoryCache>().Get<AgendaResponse>(FrontPageAgenda.CacheKey);

    /// <summary>Lets the next front page read the agenda afresh, without waiting for the minute to pass.</summary>
    private void ForgetFrontPage() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);

    [GeneratedRegex("value=\"[A-Za-z0-9_\\-]{40,}\"")]
    private static partial Regex AntiforgeryValue();

    [GeneratedRegex("<!--Blazor-(WebAssembly-)?Component-State:[^>]*-->")]
    private static partial Regex BlazorState();

    [GeneratedRegex("\"prerenderId\":\"[^\"]*\"")]
    private static partial Regex PrerenderId();
}
