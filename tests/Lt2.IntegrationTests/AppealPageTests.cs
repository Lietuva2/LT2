using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// The bill's page as the server renders it (design 8.12.1, design/ui/kreipimasis.html): one page for the bill and what
/// people ask of MPs, readable without JavaScript, every number with both sides and the older-account counts, the "not a
/// representative poll" note, nobody's side; the list /projektai and the old addresses.
/// </summary>
public sealed partial class AppealPageTests(DatabaseFixture database) : ApiTest(database)
{
    private static async Task<(HttpStatusCode Status, string Html)> PageAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path);
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    private static void ShouldSpeakOfUsersNotThePublic(string html)
    {
        html.ShouldContain("Tai nėra reprezentatyvi apklausa: skaičiai rodo savanoriškai dalyvaujančių Lietuva 2.0 naudotojų nuomonę.");
        foreach (var word in new[] { "visuomen", "rinkėj", "referendum" })
        {
            Regex.IsMatch(MainOf(html), word, RegexOptions.IgnoreCase).ShouldBeFalse(word);
        }

        // A person's side is a "pozicija", never "balsavimas" (the Seimas's own votes are linked, and may be; on the bill's
        // page they are listed from the stages on).
        var people = MainOf(html);
        var seimas = people.IndexOf("aria-labelledby=\"h-stages\"", StringComparison.Ordinal);
        people = seimas < 0 ? people : people[..seimas];
        Regex.IsMatch(Regex.Replace(people, "<a [^>]*>[^<]*</a>|href=\"[^\"]*\"", ""), "balsavim", RegexOptions.IgnoreCase).ShouldBeFalse();
    }

    /// <summary>
    /// A hint as the page renders it (design 8.15): a button with the trigger's id opening its own popover panel, the
    /// answer, and the "Plačiau" address (null when there is none).
    /// </summary>
    internal static (string Trigger, string Answer, string? More) HintOf(string html, string id)
    {
        var at = Regex.Escape(id);
        var match = Regex.Match(html,
            $"<button type=\"button\" class=\"hint__trigger\" id=\"{at}\" popovertarget=\"{at}-panel\" aria-describedby=\"{at}-says\">(?<trigger>[^<]*)</button>"
            + $"<span class=\"visually-hidden\" id=\"{at}-says\">Atveria trumpą paaiškinimą</span>"
            + $"<span class=\"hint__panel\" id=\"{at}-panel\" popover=\"auto\"><span class=\"hint__answer\">(?<answer>[^<]*)</span>(?:<a class=\"hint__more\" href=\"(?<more>[^\"]*)\">Plačiau</a>)?</span>");
        match.Success.ShouldBeTrue($"a hint #{id} on the page");
        Regex.Count(html, $"id=\"{at}\"").ShouldBe(1, $"#{id} is unique");
        return (match.Groups["trigger"].Value, match.Groups["answer"].Value, match.Groups["more"].Success ? match.Groups["more"].Value : null);
    }

    /// <summary>The page's own content; the layout's navigation is checked elsewhere.</summary>
    private static string MainOf(string html) => html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];

    /// <summary>
    /// Found by Playwright on the stack: a member's record page renders several components that read runtime settings at
    /// the same moment; with the settings cache cold they all reached the one scoped settings context and the page
    /// answered 500. Every request here starts with an empty cache.
    /// </summary>
    [Fact]
    public async Task A_members_record_page_renders_when_the_settings_cache_is_empty()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var cache = (Microsoft.Extensions.Caching.Memory.MemoryCache)Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(Factory.Services);

        for (var i = 0; i < 10; i++)
        {
            cache.Clear();
            var (status, _) = await PageAsync(member, "/seimas/XVP-0618");
            status.ShouldBe(HttpStatusCode.OK, $"request {i + 1}");
        }
    }

    /// <summary>Design 8.1: a record is opened without arguments, so a published one may have none, on either side.</summary>
    [Fact]
    public async Task An_open_record_without_participants_or_arguments_says_so_on_both_sides_without_javascript()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, side: null);
        using var stranger = NewBrowser();

        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0618");
        status.ShouldBe(HttpStatusCode.OK);
        var main = MainOf(html);
        Regex.Count(main, "Argumentų dar nėra\\.").ShouldBe(2, "one empty state per side, server-rendered");
        main.ShouldContain("Tai nėra reprezentatyvi apklausa");
        (await stranger.RecordAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));
        var (listStatus, _) = await PageAsync(stranger, "/projektai");
        listStatus.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_record_page_shows_both_sides_with_older_account_counts_the_description_arguments_and_the_note()
    {
        var (initiator, initiatorEmail) = await Factory.MemberAsync(displayName: null);
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against", threshold: 1000);
        await Database.AgeAccountAsync(initiatorEmail, 60);
        var (supporter, _) = await Factory.MemberAsync();
        using var _s = supporter;
        (await supporter.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0618");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Apie ką projektas");
        Regex.Count(html, "<h1[ >]").ShouldBe(1);
        html.ShouldNotContain("atidarė", customMessage: "nothing says who was first");
        html.ShouldNotContain("Rūta Mockutė", customMessage: "the first participant's name is nowhere but beside an argument they wrote");
        html.ShouldContain("Prašo balsuoti už");
        html.ShouldContain("Prašo balsuoti prieš");
        html.ShouldContain("iš jų 0 – paskyros senesnės nei 30 d.");
        html.ShouldContain("iš jų 1 – paskyros senesnės nei 30 d.");
        html.ShouldContain(AppealSteps.Description);
        html.ShouldContain("Parašė moderatorius");
        html.ShouldContain(AppealSteps.AgainstReason, customMessage: "the arguments are prerendered, readable without JavaScript");
        html.ShouldContain("Man svarbu · 0");
        html.ShouldContain("riba 1 000");
        html.ShouldContain("Šio projekto ribą nustatė moderatorius.");
        html.ShouldContain("Lietuvos Respublikos Seimo atvirieji duomenys");
        html.ShouldNotContain(" style=\"");
        ShouldSpeakOfUsersNotThePublic(html);

        // Before its first summary the record says so, and the public answer behind it is empty.
        html.ShouldContain("Suvestinėse Seimo nariams");
        html.ShouldContain("Šis projektas dar nebuvo nė vienoje suvestinėje.");
        (await stranger.GetJsonAsync<List<Lt2.Api.Contracts.Digests.RecordDigestResponse>>(Lt2.Api.Contracts.Digests.DigestRoutes.OfRecordOn(AppealSteps.ThirdBill))).ShouldBeEmpty();

        // Visitors get links to sign in instead of buttons: they work without JavaScript.
        html.ShouldContain("<a class=\"btn btn--for btn--wide\" href=\"/prisijungti#tikslas=%2Fseimas%2FXVP-0618\">Prašau balsuoti už</a>");
        html.ShouldNotContain("Jūs prašote balsuoti");
    }

    [Fact]
    public async Task A_member_sees_only_their_own_side_on_the_page()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;

        var (_, mine) = await PageAsync(member, "/seimas/XVP-0618");
        mine.ShouldContain("Jūs prašote balsuoti už. Tai matote tik jūs.");
        mine.ShouldContain("id=\"side-for\" aria-pressed=\"true\"");

        var (_, theirs) = await PageAsync(other, "/seimas/XVP-0618");
        theirs.ShouldNotContain("Jūs prašote balsuoti");
        theirs.ShouldNotContain("aria-pressed=\"true\"");
        theirs.ShouldContain("Jūsų pasirinkimas nėra viešas.");
    }

    [Fact]
    public async Task Everyone_sees_a_report_link_on_the_description_and_every_argument_and_a_visitor_is_told_how_to_report()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (writer, _) = await Factory.MemberAsync();
        using var _w = writer;
        (await writer.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Gynyba finansuojama nekeliant PVM.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        // A visitor without a session (this HTML is what a browser with JavaScript off gets): a <details> per argument and
        // one for the record, telling them to sign in or to write to the configured address. No form, no anonymous report.
        using var stranger = NewBrowser();
        var (_, html) = await PageAsync(stranger, "/seimas/XVP-0618");
        Regex.Count(html, "<details class=\"more report-visitor\">").ShouldBe(3, "two arguments and the description");
        html.ShouldContain("<summary class=\"linkish small\">Pranešti apie klaidą</summary>", customMessage: "design 8.13: beside the description");
        Regex.Count(html, "<summary class=\"linkish small\">Pranešti</summary>").ShouldBe(2);
        html.ShouldContain("Pranešti apie turinį moderatoriams gali prisijungę Lietuva 2.0 naudotojai:");
        html.ShouldContain("<a href=\"/prisijungti#tikslas=%2Fseimas%2FXVP-0618\">prisijunkite</a>");
        html.ShouldContain("<a href=\"mailto:moderatoriai@lt2.test\">moderatoriai@lt2.test</a>");
        html.ShouldNotContain("id=\"report-appeal\"");

        // A member reports in the product: a button on the record and on each argument.
        var (_, member) = await PageAsync(writer, "/seimas/XVP-0618");
        member.ShouldContain("id=\"report-appeal\"");
        Regex.Count(member, ">Pranešti</button>").ShouldBe(2);
        member.ShouldNotContain("report-visitor");
    }

    [Fact]
    public async Task The_old_appeal_addresses_redirect_permanently_to_the_bill()
    {
        using var stranger = NewBrowser();
        foreach (var (from, to) in new[]
        {
            ("/seimas/XVP-0533/kreipimasis", "/seimas/XVP-0533"),
            ("/seimas/XVP-0533/kreipimasis/istorija", "/seimas/XVP-0533/istorija"),
            ("/kreipimaisi/naujas", "/projektai"),
        })
        {
            using var response = await stranger.Client.GetAsync(from);
            response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently, from);
            response.Headers.Location!.ToString().ShouldEndWith(to, Case.Sensitive, from);
        }
    }

    /// <summary>Design 8.12.5 (decided 2026-11-14): the list moved to /projektai; its old address answers 301 with its query.</summary>
    [Fact]
    public async Task The_old_list_address_redirects_permanently_to_projektai_with_its_query()
    {
        using var stranger = NewBrowser();
        foreach (var (from, to) in new[]
        {
            ("/kreipimaisi", "/projektai"),
            ("/kreipimaisi?rikiuoti=dalyviai&q=x", "/projektai?rikiuoti=dalyviai&q=x"),
            ("/kreipimaisi?skirtukas=uzdaryti", "/projektai?skirtukas=uzdaryti"),
        })
        {
            using var response = await stranger.Client.GetAsync(from);
            response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently, from);
            response.Headers.Location!.OriginalString.ShouldBe(to, from);
        }

        using var unrelated = await stranger.Client.GetAsync("/kreipimaisi-x");
        unrelated.StatusCode.ShouldNotBe(HttpStatusCode.MovedPermanently, "only the list's own address is redirected");
        unrelated.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task A_bill_nobody_took_part_in_shows_the_same_controls_with_zero_counts_and_one_line_saying_so()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        using var stranger = NewBrowser();
        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0533");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("id=\"nobody-yet\"");
        html.ShouldContain("Dėl šio projekto dar niekas nenurodė, ko prašo Seimo narių.");
        html.ShouldContain("Šio projekto aprašymo dar nėra.");
        html.ShouldContain("<a class=\"btn btn--for btn--wide\" href=\"/prisijungti#tikslas=%2Fseimas%2FXVP-0533\">Prašau balsuoti už</a>");
        Regex.Count(MainOf(html), "Argumentų dar nėra\\.").ShouldBe(2);
        // Design 8.15: what the threshold is, answered in place by a hint with the number that holds now; the default needs
        // no sentence of its own.
        HintOf(html, "threshold-what").ShouldBe(("Kas yra riba?", "Riba – dalyvių skaičius (abi pusės kartu), kurį pasiekęs projektas gali patekti į suvestines Seimo nariams. Dabar – 100.", "/metodika#riba"));
        html.ShouldNotContain("<a href=\"/metodika#riba\" id=\"threshold-what\"", customMessage: "no link leaves the page only to explain the term");
        html.ShouldNotContain("Riba ta pati visiems projektams.");
        html.ShouldNotContain("Pranešti apie klaidą", customMessage: "no description, nothing to report");
        foreach (var gone in new[] { "Noriu kreipimosi", "kreipimosi dar nėra", "Atidaryti kreipimąsi", "pasiūl", "iniciator" })
        {
            MainOf(html).ShouldNotContain(gone, Case.Insensitive, gone);
        }

        ShouldSpeakOfUsersNotThePublic(html);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = 'XVP-0533'")).ShouldBe(0, "showing the page creates nothing");

        var (missing, _) = await PageAsync(stranger, "/seimas/XVP-9999");
        missing.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_shows_active_bills_by_activity_with_participants_and_arguments_and_the_note()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "for");

        using var stranger = NewBrowser();
        var (status, html) = await PageAsync(stranger, "/projektai");
        status.ShouldBe(HttpStatusCode.OK);

        // Design 8.12.5 (decided 2026-11-14): the menu item, the heading and the title are "Projektai". Design 8.21: the menu
        // item opens the agenda and is current here; this page is the tab "Visi projektai".
        html.ShouldContain("<title>Projektai · Lietuva 2.0</title>");
        html.ShouldContain("<h1>Projektai</h1>");
        html.ShouldContain("<a href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Projektai</a>", customMessage: "the menu item");
        html.ShouldContain("<a href=\"/projektai\" id=\"tab-projektai\" aria-current=\"page\">Visi projektai</a>", customMessage: "the tab");
        html.ShouldNotContain("Kreipimaisi");
        html.ShouldContain("href=\"/seimas/XVP-0618\"");
        html.ShouldContain("href=\"/seimas/XVP-0533\"", customMessage: "every active bill is listed, also one nobody took part in");
        html.ShouldContain("dalyvių 1 · argumentų 1");
        html.ShouldContain("už 1 (0) · prieš 0 (0)");
        html.ShouldContain("Skliaustuose – kiek iš jų paskyrų senesnės nei 30 d.", customMessage: "one legend under the list (design 8.15, annex P2)");
        html.IndexOf("id=\"bill-XVP-0618\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"bill-XVP-0533\"", StringComparison.Ordinal), "most activity first");
        html.ShouldContain("<b aria-current=\"true\" id=\"sort-aktyvumas\">pagal aktyvumą</b>");
        html.ShouldContain("href=\"/projektai?rikiuoti=argumentai\"");
        html.ShouldNotContain("/kreipimaisi", customMessage: "the site links to /projektai directly, not through the redirect");
        ShouldSpeakOfUsersNotThePublic(html);

        var (_, found) = await PageAsync(stranger, "/projektai?q=pajamu%20mokescio");
        found.ShouldNotContain("id=\"bill-XVP-0533\"");
        var (_, foundLt) = await PageAsync(stranger, "/projektai?q=" + Uri.EscapeDataString("pajamų mokesčio"));
        foundLt.ShouldContain("id=\"bill-XVP-0412\"");
        foundLt.ShouldNotContain("id=\"bill-XVP-0533\"");

        // The planned filter keeps only bills planned within two weeks.
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(3));
        try
        {
            var (_, planned) = await PageAsync(stranger, "/projektai?planuojami=taip");
            planned.ShouldContain("id=\"bill-XVP-0533\"");
            planned.ShouldNotContain("id=\"bill-XVP-0618\"");
            var (_, byDate) = await PageAsync(stranger, "/projektai?rikiuoti=data");
            byDate.IndexOf("id=\"bill-XVP-0533\"", StringComparison.Ordinal).ShouldBeLessThan(byDate.IndexOf("id=\"bill-XVP-0618\"", StringComparison.Ordinal), "the planned one first");
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
        }

        var (closedStatus, closed) = await PageAsync(stranger, "/projektai?skirtukas=uzdaryti");
        closedStatus.ShouldBe(HttpStatusCode.OK);
        closed.ShouldNotContain("id=\"bill-XVP-0618\"", customMessage: "an open bill is not on the closed tab");
    }

    [Fact]
    public async Task The_list_answer_is_sorted_by_the_asked_order_and_public()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var moderator = (await ModeratorAsync()).Browser;
        var (a, _) = await Factory.MemberAsync();
        var (b, _) = await Factory.MemberAsync();
        using var _a = a;
        using var _b = b;
        await Database.PrepareBillAsync(a, moderator, AppealSteps.ThirdBill, "for");
        (await b.TakeSideAsync(AppealSteps.ThirdBill, "against")).Dispose();
        for (var i = 0; i < 3; i++)
        {
            (await a.WriteReasonAsync(AppealSteps.SecondBill, "for", "Argumentas apie rinkliavas nr. " + i)).Dispose();
        }

        // XVP-0618: 2 participants, 1 argument (activity 3); XVP-0533: 0 participants, 3 arguments (activity 3).
        using var stranger = NewBrowser();
        async Task<string[]> Order(string? sort) => (await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, sort)))
            .Items.Select(i => i.Bill.Number).Where(n => n is AppealSteps.SecondBill or AppealSteps.ThirdBill).ToArray();
        (await Order("participants")).ShouldBe([AppealSteps.ThirdBill, AppealSteps.SecondBill]);
        (await Order("arguments")).ShouldBe([AppealSteps.SecondBill, AppealSteps.ThirdBill]);
        (await Order(null)).ShouldBe([AppealSteps.SecondBill, AppealSteps.ThirdBill], "equal activity, then by number");
        var item = (await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.Appeals)).Items.Single(i => i.Bill.Number == AppealSteps.SecondBill);
        item.Arguments.ShouldBe(3);
        item.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));
        using var response = await stranger.Client.GetAsync(AppealRoutes.AppealsList(null, false, false, null, "arguments"));
        response.Headers.CacheControl!.Public.ShouldBeTrue();
    }
    [Theory]
    [InlineData("/moderavimas")]
    [InlineData("/administravimas/nustatymai")]
    public async Task Staff_pages_send_visitors_to_sign_in(string path)
    {
        using var stranger = NewBrowser();
        using var response = await stranger.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldStartWith("https://lt2.test/prisijungti");
    }

    [Fact]
    public async Task The_moderation_page_lists_the_queue_for_a_moderator_and_refuses_a_member()
    {
        var (writer, _) = await Factory.MemberAsync();
        using var _w = writer;
        const string Flagged = "Daugiau rasite https://pavyzdys.test/rinkliavos";
        (await writer.WriteReasonAsync(AppealSteps.SecondBill, "for", Flagged)).Dispose();

        var (status, html) = await PageAsync((await ModeratorAsync()).Browser, "/moderavimas");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(Flagged);
        // Each action is its own control (design 8.3); the fields open only when one is chosen.
        html.ShouldContain(">Skelbti</button>", customMessage: "a waiting argument is offered for publication");
        html.ShouldNotContain(">Grąžinti autoriui</button>");
        html.ShouldNotContain("Prašomi kreipimaisi");
        html.ShouldNotContain("id=\"statement-", customMessage: "no statement field before an action is chosen");

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await PageAsync(member, "/moderavimas")).Html.ShouldContain("Moderuoti galima tik prisijungus darbuotojo prieigos raktu");
    }

    /// <summary>
    /// Design 8.15: "Kas yra riba?" answers in place with the number that holds now: a bill's own threshold when a moderator
    /// set one, otherwise the current setting, which the lists' hint and the methodology's row "Riba" (without repeating the word) say too.
    /// </summary>
    [Fact]
    public async Task The_threshold_hint_says_the_number_that_holds_now_for_the_bill_and_follows_the_setting()
    {
        const string Meaning = "Riba – dalyvių skaičius (abi pusės kartu), kurį pasiekęs projektas gali patekti į suvestines Seimo nariams.";
        var moderator = (await ModeratorAsync()).Browser;
        await Database.PrepareBillAsync(moderator, moderator, AppealSteps.ThirdBill, side: null, threshold: 150);
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        using var stranger = NewBrowser();

        var (ownStatus, own) = await PageAsync(stranger, "/seimas/" + AppealSteps.ThirdBill);
        ownStatus.ShouldBe(HttpStatusCode.OK);
        HintOf(own, "threshold-what").ShouldBe(("Kas yra riba?", Meaning + " Šiam projektui moderatorius nustatė savą ribą: dabar – 150.", "/metodika#riba"));
        own.ShouldContain("id=\"threshold-set-by\"", customMessage: "the moderator's own number is still said beside the progress");

        var admin = (await AdministratorAsync()).Browser;
        await using (await admin.WithSettingAsync("Digest:ThresholdDefault", 120))
        {
            HintOf((await PageAsync(stranger, "/seimas/" + AppealSteps.SecondBill)).Html, "threshold-what").Answer.ShouldBe(Meaning + " Dabar – 120.");
            HintOf((await PageAsync(stranger, "/seimas/" + AppealSteps.ThirdBill)).Html, "threshold-what").Answer.ShouldEndWith("dabar – 150.", customMessage: "a bill's own number does not follow the default");

            // "riba" repeats in the lists' rows: one hint beside the list says what it is and the default that holds now.
            var listHint = (Meaning + " Dabar – 120, jei moderatorius projektui nenustatė kitos; eilutėje rodoma to projekto riba.", "/metodika#riba");
            var list = HintOf((await PageAsync(stranger, "/projektai")).Html, "threshold-what");
            (list.Answer, list.More).ShouldBe(listHint);
            var agenda = HintOf((await PageAsync(stranger, "/darbotvarke")).Html, "threshold-what");
            (agenda.Answer, agenda.More).ShouldBe(listHint);

            (await PageAsync(stranger, "/metodika")).Html.ShouldContain("<dd class=\"num\">dalyvių skaičius (abi pusės kartu), kurį pasiekęs projektas gali patekti į suvestines Seimo nariams. Dabar – 120. Šį skaičių gali keisti administratorius");
        }

        HintOf((await PageAsync(stranger, "/seimas/" + AppealSteps.SecondBill)).Html, "threshold-what").Answer.ShouldBe(Meaning + " Dabar – 100.", "the setting is back");
    }

    [Fact]
    public async Task The_rules_for_moderators_and_the_summary_rules_with_their_current_values_are_public()
    {
        using var stranger = NewBrowser();
        var (rulesStatus, rules) = await PageAsync(stranger, "/moderavimo-taisykles");
        rulesStatus.ShouldBe(HttpStatusCode.OK);
        rules.ShouldContain("Moderavimo taisyklės");
        rules.ShouldContain("nemato, kas kurią pusę pasirinko");
        rules.ShouldContain("mailto:moderatoriai@lt2.test");

        var (_, method) = await PageAsync(stranger, "/metodika");
        method.ShouldContain("Kas patektų į suvestines Seimo nariams");
        // Design 8.15: the row "Riba" says what the threshold is, then its number as what holds now and who can change it.
        method.ShouldContain("<dt id=\"riba\">Riba</dt><dd class=\"num\">dalyvių skaičius (abi pusės kartu), kurį pasiekęs projektas gali patekti į suvestines Seimo nariams. Dabar – 100. Šį skaičių gali keisti administratorius, o moderatorius – nustatyti projektui kitą, ne mažesnį už mažiausią ribą (dabar – 50).</dd>");
        method.ShouldNotContain("visiems projektams");
        method.ShouldContain("pirmadienį 07:00");
        method.ShouldContain("12:00 darbo dieną prieš posėdį");
        method.ShouldContain("2 suvestinės vienam Seimo nariui");
        method.ShouldContain("pasikeitė bent 10 %");
        method.ShouldContain("Argumentai yra vieši: kiekvienas rodomas su autoriaus vardu ir puse");
        method.ShouldContain("Savo argumentą autorius gali atsiimti, kol dalyvauti dėl projekto galima.");
        method.ShouldContain("Argumentų dėl vieno projekto</dt><dd class=\"num\">neribojama");
        method.ShouldContain("„Man svarbu“ žymų dėl vieno projekto</dt><dd class=\"num\">neribojama");
        method.ShouldContain("kai bent 30 % per paskutines 72 valandas");
        method.ShouldContain("bent 20 žmonių");

        var api = await stranger.GetJsonAsync<Lt2.Api.Contracts.Digests.DigestRulesResponse>(Lt2.Api.Contracts.Digests.DigestRoutes.Rules);
        api.ShouldBe(new Lt2.Api.Contracts.Digests.DigestRulesResponse(100, 50, "07:00", "12:00", 2, 0.10m, 0.30m, 50, 72, 20, 30, 2));
    }

    [Fact]
    public async Task The_edit_history_of_a_description_is_public_without_the_editor()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var moderator = (await ModeratorAsync()).Browser;
        foreach (var text in new[] { "Pirmasis, nepakankamai tikslus aprašymas.", AppealSteps.Description })
        {
            (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill), new ModeratorDescriptionRequest { Description = text, Statement = "Bandymas." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var stranger = NewBrowser();
        var history = await stranger.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.SecondBill));
        history.Last().OldText.ShouldBe("Pirmasis, nepakankamai tikslus aprašymas.");
        history.Last().NewText.ShouldBe(AppealSteps.Description);

        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0533/istorija");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Pirmasis, nepakankamai tikslus aprašymas.");
        html.ShouldNotContain("Bandymas.", Case.Sensitive, "nor the statement");
        (await PageAsync(stranger, "/seimas/XVP-0533")).Html.ShouldContain("href=\"/seimas/XVP-0533/istorija\"");
    }
}