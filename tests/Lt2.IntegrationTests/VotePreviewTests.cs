using System.Buffers.Binary;
using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Components;
using Lt2.Web.Components.Parliament;
using Lt2.Web.Previews;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SkiaSharp;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.22.5 (owner, 2026-11-26): a vote's link preview says the day and the Seimas's numbers, then the bill's description,
/// and its image is the seat map coloured by each MP's choice with the title and the legend, drawn when first asked for, kept,
/// and served at an address that changes with the picture. A vote without per-MP ballots keeps the site's image.
/// </summary>
public sealed partial class VotePreviewTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Site = "https://lt2.test";
    private const long Vote = ReasonsToComeSteps.TaxConsiderationVote;

    [GeneratedRegex("<meta (property|name)=\"([^\"]+)\" content=\"([^\"]*)\"")]
    private static partial Regex Meta();

    private sealed class SiteFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Site:Address", Site);
        }
    }

    public async ValueTask InitializeAsync() => await ParliamentSeed.EnsureAsync(Database);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<Dictionary<string, string>> TagsAsync(TestBrowser browser, string path)
    {
        var html = await browser.Client.GetStringAsync(path, TestContext.Current.CancellationToken);
        return Meta().Matches(html).ToDictionary(m => m.Groups[2].Value, m => WebUtility.HtmlDecode(m.Groups[3].Value), StringComparer.Ordinal);
    }

    private static (int Width, int Height) SizeOf(byte[] png)
    {
        png[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "a PNG file");
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public async Task A_votes_preview_has_the_numbers_as_its_text_and_its_own_seat_map_image_served_as_a_kept_png()
    {
        await using var site = new SiteFactory(Database);
        using var visitor = site.NewBrowser();
        var tags = await TagsAsync(visitor, $"/seimas/balsavimai/{Vote}");

        tags["og:description"].ShouldMatch(@"^Seimo balsavimas \d{4}-\d{2}-\d{2}: už \d+, prieš \d+, susilaikė \d+\.");
        tags["description"].ShouldBe(tags["og:description"]);
        tags["og:image"].ShouldMatch($@"^{Regex.Escape(Site)}/img/votes/{Vote}/[0-9a-f]{{16}}\.png$");
        (tags["og:image:width"], tags["og:image:height"], tags["og:image:type"]).ShouldBe(("1200", "630", "image/png"));
        tags["og:image:alt"].ShouldStartWith("Seimo salės schema, kurioje kiekvieno Seimo nario vieta nuspalvinta pagal balsą: už ");

        var path = tags["og:image"][Site.Length..];
        using var served = await visitor.Client.GetAsync(path, TestContext.Current.CancellationToken);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        served.Headers.GetValues("Cross-Origin-Resource-Policy").Single().ShouldBe("cross-origin", "other sites show the image");
        served.Headers.CacheControl?.ToString().ShouldBe("public, max-age=31536000, immutable");
        var first = await served.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        SizeOf(first).ShouldBe((1200, 630));

        using var again = await visitor.Client.GetAsync(path, TestContext.Current.CancellationToken);
        (await again.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(first, "the same picture, kept");

        using var head = await visitor.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), TestContext.Current.CancellationToken);
        head.StatusCode.ShouldBe(HttpStatusCode.OK, "link-preview fetchers ask with HEAD too");
    }

    [Fact]
    public async Task An_old_versions_address_leads_to_the_current_image_and_an_unknown_vote_has_none()
    {
        using var visitor = NewBrowser();
        using var old = await visitor.Client.GetAsync($"/img/votes/{Vote}/0000000000000000.png", TestContext.Current.CancellationToken);
        old.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        old.Headers.Location?.ToString().ShouldMatch($@"^/img/votes/{Vote}/[0-9a-f]{{16}}\.png$");

        using var unknown = await visitor.Client.GetAsync("/img/votes/123456789012/0000000000000000.png", TestContext.Current.CancellationToken);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static BallotResponse Ballot(int i, string? faction, string choice) => new(new MpLink($"mp-{i}", "Vardas", $"Pavardė{i}"), faction, choice);

    private static VoteResponse Sample(string? plainTitle = "Keičiami gyventojų pajamų mokesčio tarifai", IReadOnlyList<BallotResponse>? ballots = null, bool consensus = false)
    {
        ballots ??=
        [
            .. Enumerable.Range(0, 70).Select(i => Ballot(i, "RPF", i < 60 ? BallotChoices.For : BallotChoices.Abstain)),
            .. Enumerable.Range(70, 50).Select(i => Ballot(i, "PSF", i < 110 ? BallotChoices.Against : BallotChoices.DidNotVote)),
            .. Enumerable.Range(120, 21).Select(i => Ballot(i, null, BallotChoices.For)),
        ];
        var link = new VoteLink(42, new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero), "Dėl projekto", VoteKinds.StageResult, BillStages.Consideration, consensus, !consensus,
            new BillLink("XVP-0412", "Gyventojų pajamų mokesčio įstatymo Nr. IX-1007 1, 6 ir 20 straipsnių pakeitimo įstatymo projektas", plainTitle));
        var totals = consensus ? new VoteTotalsResponse(null, null, null, null, null) : new VoteTotalsResponse(81, 40, 10, 131, 141);
        return new VoteResponse(new VoteSummaryResponse(link, totals, null), link.HeldAt, "Gyventojų pajamų mokesčio įstatymo pataisa", [], ballots);
    }

    private (Formats Formats, IStringLocalizer<PageText> Text, IServiceScope Scope) Services()
    {
        var scope = Factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<Formats>(), scope.ServiceProvider.GetRequiredService<IStringLocalizer<PageText>>(), scope);
    }

    [Fact]
    public void The_preview_names_the_readable_title_unless_reported_and_its_version_follows_what_is_drawn()
    {
        var (formats, text, scope) = Services();
        using var _ = scope;
        var preview = VotePreview.Of(Sample(), reported: false, formats, text).ShouldNotBeNull();
        preview.Title.ShouldBe("Keičiami gyventojų pajamų mokesčio tarifai");
        preview.Label.ShouldBe("Seimo balsavimas · " + formats.Stage(BillStages.Consideration) + " · 2026-11-20");
        preview.Legend.Select(c => (c.Code, c.Count)).ShouldBe(
            [(BallotChoices.For, 81), (BallotChoices.Against, 40), (BallotChoices.Abstain, 10), (BallotChoices.DidNotVote, 10)]);
        preview.Choices.Count.ShouldBe(141);
        preview.Path.ShouldBe($"/img/votes/42/{preview.Version}.png");
        preview.Alt(text).ShouldBe("Seimo salės schema, kurioje kiekvieno Seimo nario vieta nuspalvinta pagal balsą: už 81, prieš 40, susilaikė 10, nebalsavo 10.");

        VotePreview.Of(Sample(), reported: false, formats, text)!.Version.ShouldBe(preview.Version, "the same picture, the same address");
        var reported = VotePreview.Of(Sample(), reported: true, formats, text).ShouldNotBeNull();
        reported.Title.ShouldBe("Gyventojų pajamų mokesčio įstatymo Nr. IX-1007 1, 6 ir 20 straipsnių pakeitimo įstatymo projektas");
        reported.Version.ShouldNotBe(preview.Version);
        var otherBallot = Sample(ballots: [.. Sample().Ballots.Select((b, i) => i == 0 ? b with { Choice = BallotChoices.Against } : b)]);
        VotePreview.Of(otherBallot, reported: false, formats, text)!.Version.ShouldNotBe(preview.Version);

        VotePreview.Of(Sample(ballots: [], consensus: true), reported: false, formats, text).ShouldBeNull("a consensus has no seats to colour");
    }

    [Fact]
    public void The_text_is_the_day_and_the_numbers_then_the_bills_description_and_a_consensus_says_so()
    {
        var (_, text, scope) = Services();
        using var _ = scope;
        VotePreview.Description(Sample(), null, text).ShouldBe("Seimo balsavimas 2026-11-20: už 81, prieš 40, susilaikė 10.");
        VotePreview.Description(Sample(), "Projektas keičia tarifus.", text).ShouldBe("Seimo balsavimas 2026-11-20: už 81, prieš 40, susilaikė 10. Projektas keičia tarifus.");
        VotePreview.Description(Sample(ballots: [], consensus: true), null, text).ShouldBe("Seimo balsavimas 2026-11-20: pritarta bendru sutarimu.");
    }

    [Fact]
    public void The_image_is_1200_by_630_and_a_long_title_takes_at_most_two_lines()
    {
        var (formats, text, scope) = Services();
        using var _ = scope;
        var preview = VotePreview.Of(Sample(plainTitle: null), reported: false, formats, text)!;
        var png = VotePreviewImage.Draw(preview);
        SizeOf(png).ShouldBe((1200, 630));

        using var font = new SKFont(PreviewDrawing.Display, 48);
        var lines = PreviewDrawing.Lines(string.Join(' ', Enumerable.Repeat("Gyventojų pajamų mokesčio įstatymo pataisa", 6)), font, 600, 2);
        lines.Count.ShouldBe(2);
        lines[^1].ShouldEndWith("…");
        lines.ShouldAllBe(l => font.MeasureText(l) <= 600);
    }
}
