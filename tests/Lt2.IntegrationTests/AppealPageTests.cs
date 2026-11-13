using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// The appeal pages as the server renders them (design 8.1, design/ui/kreipimasis.html): readable without JavaScript,
/// every number with both sides and the older-account counts, the "not a representative poll" note, nobody's side.
/// </summary>
public sealed partial class AppealPageTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static StaffMember? _moderator;

    private async Task<TestBrowser> ModeratorAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _moderator ??= await StaffMembers.ActiveAsync(Factory, Database, "moderator");
            return _moderator.Browser;
        }
        finally
        {
            Gate.Release();
        }
    }

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

        // A person's side is a "pozicija", never "balsavimas" (the Seimas's own votes are linked, and may be).
        Regex.IsMatch(Regex.Replace(MainOf(html), "<a [^>]*>[^<]*</a>|href=\"[^\"]*\"", ""), "balsavim", RegexOptions.IgnoreCase).ShouldBeFalse();
    }

    /// <summary>The page's own content; the layout's navigation is checked elsewhere.</summary>
    private static string MainOf(string html) => html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];

    /// <summary>
    /// Found by Playwright on the stack: a member's appeal page renders several components that read runtime settings at
    /// the same moment; with the settings cache cold they all reached the one scoped settings context and the page
    /// answered 500. Every request here starts with an empty cache.
    /// </summary>
    [Fact]
    public async Task A_members_appeal_page_renders_when_the_settings_cache_is_empty()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var cache = (Microsoft.Extensions.Caching.Memory.MemoryCache)Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(Factory.Services);

        for (var i = 0; i < 10; i++)
        {
            cache.Clear();
            var (status, _) = await PageAsync(member, "/seimas/XVP-0618/kreipimasis");
            status.ShouldBe(HttpStatusCode.OK, $"request {i + 1}");
        }
    }

    /// <summary>Design 8.1: an appeal is opened without arguments, so a published one may have none, on either side.</summary>
    [Fact]
    public async Task An_open_appeal_without_participants_or_arguments_says_so_on_both_sides_without_javascript()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, side: null);
        using var stranger = NewBrowser();

        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0618/kreipimasis");
        status.ShouldBe(HttpStatusCode.OK);
        var main = MainOf(html);
        Regex.Count(main, "Argumentų dar nėra\\.").ShouldBe(2, "one empty state per side, server-rendered");
        main.ShouldContain("Tai nėra reprezentatyvi apklausa");
        (await stranger.AppealAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));
        var (listStatus, _) = await PageAsync(stranger, "/kreipimaisi");
        listStatus.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_appeal_page_shows_both_sides_with_older_account_counts_the_description_arguments_and_the_note()
    {
        var (initiator, initiatorEmail) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against", threshold: 1000);
        await Database.AgeAccountAsync(initiatorEmail, 60);
        var (supporter, _) = await Factory.MemberAsync();
        using var _s = supporter;
        (await supporter.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0618/kreipimasis");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Kreipimasis į Seimo narius");
        Regex.Count(html, "<h1[ >]").ShouldBe(1);
        html.ShouldContain("Kreipimąsi atidarė Rūta Mockutė");
        html.ShouldContain("Prašo balsuoti už");
        html.ShouldContain("Prašo balsuoti prieš");
        html.ShouldContain("iš jų 0 – paskyros senesnės nei 30 d.");
        html.ShouldContain("iš jų 1 – paskyros senesnės nei 30 d.");
        html.ShouldContain(AppealSteps.Description);
        html.ShouldContain("Aprašymą parašė kreipimosi iniciatorius, patvirtino moderatorius");
        html.ShouldContain(AppealSteps.AgainstReason, customMessage: "the arguments are prerendered, readable without JavaScript");
        html.ShouldContain("Man svarbu · 0");
        html.ShouldContain("riba 1 000");
        html.ShouldContain("Ribą nustatė kreipimosi iniciatorius.");
        html.ShouldContain("Lietuvos Respublikos Seimo atvirieji duomenys");
        html.ShouldNotContain(" style=\"");
        ShouldSpeakOfUsersNotThePublic(html);

        // Visitors get links to sign in instead of buttons: they work without JavaScript.
        html.ShouldContain("<a class=\"btn btn--for btn--wide\" href=\"/prisijungti\">Prašau balsuoti už</a>");
        html.ShouldNotContain("Jūs prašote balsuoti");
    }

    [Fact]
    public async Task A_member_sees_only_their_own_side_on_the_page()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;

        var (_, mine) = await PageAsync(member, "/seimas/XVP-0618/kreipimasis");
        mine.ShouldContain("Jūs prašote balsuoti už. Tai matote tik jūs.");
        mine.ShouldContain("id=\"side-for\" aria-pressed=\"true\"");

        var (_, theirs) = await PageAsync(other, "/seimas/XVP-0618/kreipimasis");
        theirs.ShouldNotContain("Jūs prašote balsuoti");
        theirs.ShouldNotContain("aria-pressed=\"true\"");
        theirs.ShouldContain("Jūsų pasirinkimas nėra viešas.");
    }

    [Fact]
    public async Task Everyone_sees_a_report_link_on_the_appeal_and_every_argument_and_a_visitor_is_told_how_to_report()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (writer, _) = await Factory.MemberAsync();
        using var _w = writer;
        (await writer.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Gynyba finansuojama nekeliant PVM.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        // A visitor without a session (this HTML is what a browser with JavaScript off gets): a <details> per argument and
        // one for the appeal, telling them to sign in or to write to the configured address. No form, no anonymous report.
        using var stranger = NewBrowser();
        var (_, html) = await PageAsync(stranger, "/seimas/XVP-0618/kreipimasis");
        Regex.Count(html, "<details class=\"report-visitor\">").ShouldBe(3, "two arguments and the appeal");
        html.ShouldContain("<summary class=\"linkish small\">Pranešti apie kreipimąsi</summary>");
        Regex.Count(html, "<summary class=\"linkish small\">Pranešti</summary>").ShouldBe(2);
        html.ShouldContain("Pranešti apie turinį moderatoriams gali prisijungę Lietuva 2.0 naudotojai:");
        html.ShouldContain("<a href=\"/prisijungti\">prisijunkite</a>");
        html.ShouldContain("<a href=\"mailto:moderatoriai@lt2.test\">moderatoriai@lt2.test</a>");
        html.ShouldNotContain("id=\"report-appeal\"");

        // A member reports in the product: a button on the appeal and on each argument.
        var (_, member) = await PageAsync(writer, "/seimas/XVP-0618/kreipimasis");
        member.ShouldContain("id=\"report-appeal\"");
        Regex.Count(member, ">Pranešti</button>").ShouldBe(2);
        member.ShouldNotContain("report-visitor");
    }

    [Fact]
    public async Task A_pending_appeal_page_is_shown_only_to_its_initiator()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        (await initiator.OpenAppealAsync(AppealSteps.SecondBill)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var (status, html) = await PageAsync(initiator, "/seimas/XVP-0533/kreipimasis");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Kreipimasis laukia moderatoriaus. Kol jis nepaskelbtas, jį matote tik jūs.");
        html.ShouldContain("aprašymą");

        using var stranger = NewBrowser();
        var (_, other) = await PageAsync(stranger, "/seimas/XVP-0533/kreipimasis");
        other.ShouldNotContain(AppealSteps.Description);
        other.ShouldContain("Šiam projektui kreipimosi dar nėra.");
    }

    [Fact]
    public async Task A_bill_without_an_appeal_offers_to_open_one()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        using var stranger = NewBrowser();
        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0533/kreipimasis");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Šiam projektui kreipimosi dar nėra.");
        html.ShouldContain("Prisijunkite, kad atidarytumėte kreipimąsi");

        var (missing, _) = await PageAsync(stranger, "/seimas/XVP-9999/kreipimasis");
        missing.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_shows_published_appeals_with_both_sides_and_the_note()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "for");

        using var stranger = NewBrowser();
        var (status, html) = await PageAsync(stranger, "/kreipimaisi");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("href=\"/seimas/XVP-0618/kreipimasis\"");
        html.ShouldContain("prašo už 1 (iš jų 0 – senesnės nei 30 d. paskyros), prieš 0 (iš jų 0)");
        ShouldSpeakOfUsersNotThePublic(html);
        (await PageAsync(stranger, "/kreipimaisi?rikiuoti=naujausi")).Html.ShouldContain("href=\"/seimas/XVP-0618/kreipimasis\"");

        var (chooser, bills) = await PageAsync(stranger, "/kreipimaisi/naujas?q=pajamu");
        chooser.ShouldBe(HttpStatusCode.OK);
        bills.ShouldContain("href=\"/seimas/XVP-0412/kreipimasis\"");
        bills.ShouldNotContain("href=\"/seimas/XVP-0533/kreipimasis\"");
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
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        (await initiator.OpenAppealAsync(AppealSteps.SecondBill)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var (status, html) = await PageAsync(await ModeratorAsync(), "/moderavimas");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(AppealSteps.Description);
        // Each action is its own control (design 8.3); the fields open only when one is chosen.
        html.ShouldContain(">Skelbti</button>", customMessage: "a pending appeal is offered for publication");
        html.ShouldContain(">Grąžinti autoriui</button>");
        html.ShouldContain(">Keisti ribą</button>");
        html.ShouldNotContain(">Pataisyti planuojamą datą</button>", customMessage: "the planned date applies only to a published appeal");
        html.ShouldNotContain("id=\"statement-", customMessage: "no statement field before an action is chosen");

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await PageAsync(member, "/moderavimas")).Html.ShouldContain("Moderuoti galima tik prisijungus darbuotojo prieigos raktu");
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
        method.ShouldContain("siūloma 100, ne mažiau kaip 50 dalyvių");
        method.ShouldContain("pirmadienį 07:00");
        method.ShouldContain("12:00 darbo dieną prieš posėdį");
        method.ShouldContain("2 suvestinės vienam Seimo nariui");
        method.ShouldContain("pasikeitė bent 10 %");
        method.ShouldContain("Argumentai yra vieši: kiekvienas rodomas su autoriaus vardu ir puse");
        method.ShouldContain("Savo argumentą autorius gali atsiimti, kol kreipimasis atviras.");
        method.ShouldContain("Argumentų dėl vieno projekto</dt><dd class=\"num\">neribojama");
        method.ShouldContain("„Man svarbu“ žymų dėl vieno projekto</dt><dd class=\"num\">neribojama");
        method.ShouldContain("kai bent 30 % per paskutines 72 valandas");
        method.ShouldContain("bent 20 žmonių");

        var api = await stranger.GetJsonAsync<Lt2.Api.Contracts.Digests.DigestRulesResponse>(Lt2.Api.Contracts.Digests.DigestRoutes.Rules);
        api.ShouldBe(new Lt2.Api.Contracts.Digests.DigestRulesResponse(100, 50, "07:00", "12:00", 2, 0.10m, 0.30m, 50, 72, 20, 30, 2));
    }

    [Fact]
    public async Task The_edit_history_of_a_published_appeals_description_is_public_without_the_editor()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var moderator = await ModeratorAsync();
        var (initiator, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        var appeal = await (await initiator.OpenAppealAsync(AppealSteps.SecondBill, description: "Pirmasis, nepakankamai neutralus aprašymas.")).ReadAsync<AppealResponse>();
        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(AppealRoutes.HistoryOf(AppealSteps.SecondBill))).StatusCode.ShouldBe(HttpStatusCode.NotFound, "not before publication");

        (await moderator.ReturnAppealAsync(appeal.Id, "Aprašymas turi būti neutralus.")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await initiator.SendAsync(HttpMethod.Put, AppealRoutes.AppealOf(AppealSteps.SecondBill), new EditAppealRequest { Description = AppealSteps.Description }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.PublishAsync(appeal.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var history = await stranger.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.SecondBill));
        history.Single().OldText.ShouldBe("Pirmasis, nepakankamai neutralus aprašymas.");
        history.Single().NewText.ShouldBe(AppealSteps.Description);

        var (status, html) = await PageAsync(stranger, "/seimas/XVP-0533/kreipimasis/istorija");
        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Pirmasis, nepakankamai neutralus aprašymas.");
        html.ShouldNotContain("Rūta Mockutė");
        (await PageAsync(stranger, "/seimas/XVP-0533/kreipimasis")).Html.ShouldContain("href=\"/seimas/XVP-0533/kreipimasis/istorija\"");
    }
}