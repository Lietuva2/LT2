using System.Net;
using System.Text.RegularExpressions;
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
        html.ShouldContain("Paaiškinimas autoriui");
        html.ShouldContain(">Paskelbti</button>", customMessage: "a pending appeal is offered for publication");
        html.ShouldContain(">Grąžinti taisyti</button>");

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await PageAsync(member, "/moderavimas")).Html.ShouldContain("Moderuoti galima tik prisijungus darbuotojo prieigos raktu");
    }
}
