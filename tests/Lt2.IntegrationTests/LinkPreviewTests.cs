using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Components.Layout;
using Microsoft.AspNetCore.Hosting;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.22.5: a link to a public page shows a preview where it is shared, from Open Graph and Twitter card meta tags in the
/// served page: the page's title and one or two sentences, its own absolute address from <c>Site:Address</c> (no query, no
/// fragment, no token) and the site's image, served as a PNG. Pages behind sign-in, token pages and a member's page that is not
/// public carry none, and the private ones are never indexed.
/// </summary>
public sealed partial class LinkPreviewTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Site = "https://lt2.test";

    [GeneratedRegex("<meta (property|name)=\"([^\"]+)\" content=\"([^\"]*)\"")]
    private static partial Regex Meta();

    [GeneratedRegex("<h1[^>]*>([^<]*)</h1>")]
    private static partial Regex Heading();

    /// <summary>The application with the site's public address configured, as every deployment has it (design 5.6).</summary>
    private sealed class SiteFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Site:Address", Site);
        }
    }

    private static Dictionary<string, string> TagsOf(string html)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Meta().Matches(html))
        {
            tags.Add(m.Groups[2].Value, WebUtility.HtmlDecode(m.Groups[3].Value));
        }

        return tags;
    }

    private static string[] PreviewKeys(Dictionary<string, string> tags) =>
        [.. tags.Keys.Where(k => k.StartsWith("og:", StringComparison.Ordinal) || k.StartsWith("twitter:", StringComparison.Ordinal) || k == "description").Order(StringComparer.Ordinal)];

    private static async Task<string> PageAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Every tag a preview has, the address the page's own, and the image served as the PNG it says it is.</summary>
    private static async Task<Dictionary<string, string>> PreviewAsync(TestBrowser browser, string path, string address)
    {
        var tags = TagsOf(await PageAsync(browser, path));
        PreviewKeys(tags).ShouldBe(
        [
            "description", "og:description", "og:image", "og:image:alt", "og:image:height", "og:image:type", "og:image:width",
            "og:locale", "og:site_name", "og:title", "og:type", "og:url", "twitter:card",
        ], path);
        tags["og:site_name"].ShouldBe("Lietuva 2.0");
        tags["og:locale"].ShouldBe("lt_LT");
        tags["twitter:card"].ShouldBe("summary_large_image");
        tags["og:url"].ShouldBe(Site + address, path);
        tags["og:url"].ShouldNotContain("#");
        tags["og:url"].ShouldNotContain("?");
        tags["og:title"].ShouldNotBeNullOrWhiteSpace();
        tags["og:description"].ShouldNotBeNullOrWhiteSpace();
        tags["og:description"].Length.ShouldBeLessThanOrEqualTo(LinkPreviewText.MaxLength);
        tags["description"].ShouldBe(tags["og:description"]);
        (tags["og:image:width"], tags["og:image:height"], tags["og:image:type"]).ShouldBe(("1200", "630", "image/png"));
        tags["og:image:alt"].ShouldNotBeNullOrWhiteSpace();

        var image = tags["og:image"];
        image.ShouldStartWith(Site + "/img/link-preview");
        using var served = await browser.Client.GetAsync(image[Site.Length..], TestContext.Current.CancellationToken);
        served.StatusCode.ShouldBe(HttpStatusCode.OK, image);
        served.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        var bytes = await served.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        bytes[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "a PNG file");
        // Width and height from the PNG's header (IHDR), big-endian.
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)))
            .ShouldBe((1200, 630));
        served.Headers.GetValues("Cross-Origin-Resource-Policy").Single().ShouldBe("cross-origin", "other sites show the image");
        return tags;
    }

    [Fact]
    public async Task The_bill_page_an_mp_and_the_front_page_carry_a_preview_with_their_own_address_and_the_sites_image()
    {
        await using var site = new SiteFactory(Database);
        using var visitor = site.NewBrowser();

        // A bill: its number and title, never counts; a share link's code after the "#" is never part of it.
        var bill = await PreviewAsync(visitor, "/seimas/XVP-0412?argumentas=00000000-0000-0000-0000-000000000000", "/seimas/XVP-0412");
        var title = WebUtility.HtmlDecode(Heading().Match(await PageAsync(visitor, "/seimas/XVP-0412")).Groups[1].Value).Trim();
        title.ShouldNotBeNullOrWhiteSpace();
        bill["og:title"].ShouldBe("XVP-0412 · " + title);
        bill["og:type"].ShouldBe("article");

        var mp = await PreviewAsync(visitor, "/p/jonas-petraitis", "/p/jonas-petraitis");
        mp["og:title"].ShouldBe("Jonas Petraitis");
        mp["og:description"].ShouldContain("Seimo narys");
        mp["og:type"].ShouldBe("website");

        var front = await PreviewAsync(visitor, "/", "/");
        front["og:title"].ShouldBe("Lietuva 2.0");
        front["og:description"].ShouldBe("Matykite, ką Seimas svarstys artimiausiomis dienomis, ir pasakykite Seimo nariams, ko prašote.");

        foreach (var (path, heading) in new[] { ("/darbotvarke", "Seimo darbotvarkė"), ("/projektai", "Projektai"), ("/statistika", null), ("/metodika", "Metodika") })
        {
            var tags = await PreviewAsync(visitor, path, path);
            if (heading is not null)
            {
                tags["og:title"].ShouldBe(heading, path);
            }
        }
    }

    /// <summary>Design 8.13.1, 8.22.5: off the site only a readable title a person wrote or checked; never an AI one nobody read.</summary>
    [Fact]
    public async Task The_bill_preview_uses_a_readable_title_a_person_wrote_or_checked_and_never_an_ai_one()
    {
        const string Bill = AppealSteps.SecondBill;
        const string Readable = "Keičiama vietinių rinkliavų tvarka";
        await Database.FreeBillAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        try
        {
            await using var site = new SiteFactory(Database);
            using var visitor = site.NewBrowser();
            var official = (await visitor.GetJsonAsync<Lt2.Api.Contracts.Parliament.BillResponse>(Lt2.Api.Contracts.ParliamentRoutes.BillByNumber(Bill))).Title;

            using (var described = await moderator.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.AppealRoutes.BillDescriptionOf(Bill),
                       new Lt2.Api.Contracts.Appeals.ModeratorDescriptionRequest { Description = "Projektas keičia vietinių rinkliavų tvarką.", Statement = "Bandymas.", PlainTitle = Readable }))
            {
                described.StatusCode.ShouldBe(HttpStatusCode.NoContent, await described.Content.ReadAsStringAsync());
            }

            // A moderator's title and a checked draft's are shown.
            foreach (var source in new[] { "moderator", "ai-draft" })
            {
                await Database.ExecuteAsync("UPDATE appeals.bill_record SET description_source = @s WHERE bill_number = @n", ("s", source), ("n", Bill));
                (await PreviewAsync(visitor, "/seimas/" + Bill, "/seimas/" + Bill))["og:title"].ShouldBe($"{Bill} · {Readable}", source);
            }

            // An AI's, nobody has read: the official title, though the page's own heading is the readable one.
            await Database.ExecuteAsync("UPDATE appeals.bill_record SET description_source = 'ai' WHERE bill_number = @n", ("n", Bill));
            var tags = await PreviewAsync(visitor, "/seimas/" + Bill, "/seimas/" + Bill);
            tags["og:title"].ShouldBe($"{Bill} · {official}");
            tags.Values.ShouldAllBe(v => !v.Contains(Readable, StringComparison.Ordinal), "an AI title goes nowhere without its label");
            WebUtility.HtmlDecode(await PageAsync(visitor, "/seimas/" + Bill)).ShouldContain($"<h1>{Readable}</h1>");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_public_members_page_has_a_preview_without_what_they_wrote_and_a_retired_or_unknown_one_has_none()
    {
        await using var site = new SiteFactory(Database);
        var (member, _, id) = await site.PublicMemberAsync("Peržiūros Narė", about: "Mano pačios tekstas apie save.");
        using var _m = member;
        var address = (await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).Address.ShouldNotBeNull();
        using var visitor = site.NewBrowser();

        var tags = await PreviewAsync(visitor, address, address);
        tags["og:title"].ShouldBe("Peržiūros Narė");
        tags.Values.ShouldAllBe(v => !v.Contains("Mano pačios", StringComparison.Ordinal), "\"Apie mane\" is not in the preview");

        (await member.SavedProfileAsync(null, isPublic: false)).ProfileId.ShouldBeNull();
        foreach (var (path, status) in new[] { (address, HttpStatusCode.Gone), ("/nariai/" + ProfileSteps.UnknownProfileId(), HttpStatusCode.NotFound) })
        {
            using var response = await visitor.Client.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(status, path);
            var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            html.ShouldContain("<title>", customMessage: "the page was rendered");
            PreviewKeys(TagsOf(html)).ShouldBeEmpty(path);
        }
    }

    [Fact]
    public async Task The_account_screens_and_token_pages_carry_no_preview_and_are_never_indexed()
    {
        await using var site = new SiteFactory(Database);
        var (member, _) = await site.MemberAsync("Paskyros Narė");
        using var _m = member;
        using var visitor = site.NewBrowser();

        var pages = new List<(string Where, HttpResponseMessage Response)>
        {
            ("account", await member.Client.GetAsync("/paskyra", TestContext.Current.CancellationToken)),
            ("security", await member.Client.GetAsync("/paskyra/saugumas", TestContext.Current.CancellationToken)),
            ("sign-in", await visitor.Client.GetAsync("/prisijungti", TestContext.Current.CancellationToken)),
            ("take-over", await visitor.Client.GetAsync("/seimo-narys/perimti", TestContext.Current.CancellationToken)),
            ("office", await visitor.Client.GetAsync("/seimo-narys/biuras", TestContext.Current.CancellationToken)),
            ("representative", await member.Client.GetAsync("/organizacijos/kvietimas", TestContext.Current.CancellationToken)),
            ("mp mail", await visitor.Client.GetAsync("/suvestines/nustatymai/" + new string('b', 43), TestContext.Current.CancellationToken)),
            ("stop link", await visitor.Client.GetAsync("/laiskai/nebegauti/" + new string('c', 43), TestContext.Current.CancellationToken)),
        };

        try
        {
            foreach (var (where, response) in pages)
            {
                response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html", where);
                var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                html.ShouldContain("<title>", customMessage: $"{where}: the page was rendered");
                PreviewKeys(TagsOf(html)).ShouldBeEmpty(where);
                response.Headers.GetValues("X-Robots-Tag").Single().ShouldStartWith("noindex", customMessage: where);
            }

            // The check can fail: a public page read the same way has both.
            using var methodology = await visitor.Client.GetAsync("/metodika", TestContext.Current.CancellationToken);
            PreviewKeys(TagsOf(await methodology.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))).ShouldContain("og:title");
            methodology.Headers.Contains("X-Robots-Tag").ShouldBeFalse();
        }
        finally
        {
            foreach (var (_, response) in pages)
            {
                response.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("Trumpas aprašymas.", "Trumpas aprašymas.")]
    [InlineData("Pirma eilutė\n\n- punktas vienas\n- punktas du", "Pirma eilutė punktas vienas punktas du")]
    public void A_short_text_is_one_line_without_list_markers(string text, string expected) =>
        LinkPreviewText.Excerpt(text).ShouldBe(expected);

    [Fact]
    public void A_long_text_keeps_whole_sentences_and_a_long_sentence_is_cut_at_a_word()
    {
        var sentence = "Projektas keičia gyventojų pajamų mokesčio tarifus ir lengvatas. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 6));
        var excerpt = LinkPreviewText.Excerpt(text);
        excerpt.Length.ShouldBeLessThanOrEqualTo(LinkPreviewText.MaxLength);
        excerpt.ShouldEndWith("lengvatas.");
        excerpt.ShouldStartWith(sentence.Trim());

        var words = string.Join(' ', Enumerable.Repeat("žodis", 60));
        var cut = LinkPreviewText.Excerpt(words);
        cut.Length.ShouldBeLessThanOrEqualTo(LinkPreviewText.MaxLength);
        cut.ShouldEndWith("žodis…");
    }

    [Theory]
    [InlineData("/seimas/XVP-0412?argumentas=1#reason-1", "/seimas/XVP-0412")]
    [InlineData("seimas/XVP-0412#kodas=ABCD-EFGH", "/seimas/XVP-0412")]
    [InlineData("", "/")]
    public void The_pages_address_has_no_query_and_no_fragment(string path, string expected) =>
        LinkPreviewText.PagePath(path).ShouldBe(expected);
}
