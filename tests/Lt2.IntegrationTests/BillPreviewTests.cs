using System.Buffers.Binary;
using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Appeals;
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
/// Design 8.22.5 (owner, 2026-11-27): a bill's link-preview image is its title card: "Seimo projektas · XVP-0412", the title
/// the preview names, the stage line and, while the record is open, one line inviting people to take part. Never the counts.
/// Drawn when first asked for, kept, and served at an address that changes with the picture.
/// </summary>
public sealed partial class BillPreviewTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Site = "https://lt2.test";
    private const string Bill = "XVP-0412";

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

    private static (int Width, int Height) SizeOf(byte[] png)
    {
        png[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "a PNG file");
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public async Task A_bills_preview_image_is_its_title_card_served_as_a_kept_png()
    {
        await using var site = new SiteFactory(Database);
        using var visitor = site.NewBrowser();
        var html = await visitor.Client.GetStringAsync($"/seimas/{Bill}", TestContext.Current.CancellationToken);
        var tags = Meta().Matches(html).ToDictionary(m => m.Groups[2].Value, m => WebUtility.HtmlDecode(m.Groups[3].Value), StringComparer.Ordinal);

        tags["og:image"].ShouldMatch($@"^{Regex.Escape(Site)}/img/bills/{Bill}/[0-9a-f]{{16}}\.png$");
        (tags["og:image:width"], tags["og:image:height"], tags["og:image:type"]).ShouldBe(("1200", "630", "image/png"));
        tags["og:image:alt"].ShouldBe($"Seimo projektas {Bill}: " + tags["og:title"][$"{Bill} · ".Length..]);

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
    public async Task An_old_versions_address_leads_to_the_current_image_and_an_unknown_bill_has_none()
    {
        using var visitor = NewBrowser();
        using var old = await visitor.Client.GetAsync($"/img/bills/{Bill}/0000000000000000.png", TestContext.Current.CancellationToken);
        old.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        old.Headers.Location?.ToString().ShouldMatch($@"^/img/bills/{Bill}/[0-9a-f]{{16}}\.png$");

        using var unknown = await visitor.Client.GetAsync("/img/bills/XVP-9999999/0000000000000000.png", TestContext.Current.CancellationToken);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private const string Official = "Gyventojų pajamų mokesčio įstatymo Nr. IX-1007 1, 6 ir 20 straipsnių pakeitimo įstatymo projektas";
    private const string Plain = "Keičiami gyventojų pajamų mokesčio tarifai";

    private static BillResponse Sample(PlannedDateResponse? planned = null, IReadOnlyList<VoteSummaryResponse>? votes = null) =>
        new(Bill, Official, null, votes ?? [], planned);

    private static BillRecordResponse Record(string status = BillRecordStatuses.Open, bool reported = false, int @for = 6800, int against = 4280) =>
        new(Guid.NewGuid(), new BillRefResponse(Bill, Official, Plain), status, "Projektas keičia tarifus.", DescriptionSources.Moderator,
            new SideCountsResponse(@for, @for, against, against), new ThresholdResponse(100, ThresholdSetters.Default, null, @for + against, true, false), [],
            PlainTitle: Plain, DescriptionReported: reported);

    private static VoteSummaryResponse StageVote(string stage) =>
        new(new VoteLink(42, new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero), "Dėl projekto", VoteKinds.StageResult, stage, false, true,
            new BillLink(Bill, Official, Plain)), new VoteTotalsResponse(81, 40, 10, 131, 141), null);

    private (Formats Formats, IStringLocalizer<PageText> Text, IServiceScope Scope) Services()
    {
        var scope = Factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<Formats>(), scope.ServiceProvider.GetRequiredService<IStringLocalizer<PageText>>(), scope);
    }

    [Fact]
    public void The_card_names_the_readable_title_unless_reported_the_stage_line_and_the_call_only_while_open()
    {
        var (formats, text, scope) = Services();
        using var _ = scope;

        var open = BillPreview.Of(Sample(new PlannedDateResponse(new DateOnly(2026, 12, 3), BillStages.Consideration, null, false)), Record(), formats, text);
        open.Title.ShouldBe(Plain);
        open.Label.ShouldBe("Seimo projektas · XVP-0412");
        open.Stage.ShouldBe("Svarstymas · Planuojama 2026-12-03");
        open.Call.ShouldBe("Paprašykite Seimo narių balsuoti už ar prieš");
        open.Path.ShouldBe($"/img/bills/{Bill}/{open.Version}.png");

        BillPreview.Of(Sample(new PlannedDateResponse(new DateOnly(2026, 12, 3), BillStages.Consideration, null, false)), Record(), formats, text)
            .Version.ShouldBe(open.Version, "the same picture, the same address");
        BillPreview.Of(Sample(new PlannedDateResponse(new DateOnly(2026, 12, 3), BillStages.Consideration, null, false)), Record(@for: 9000), formats, text)
            .Version.ShouldBe(open.Version, "the counts are never drawn, so they never change the image");

        var reported = BillPreview.Of(Sample(), Record(reported: true), formats, text);
        reported.Title.ShouldBe(Official, "while a report about the description is open, the official title");
        reported.Stage.ShouldBeNull();
        reported.Version.ShouldNotBe(open.Version);

        var closed = BillPreview.Of(Sample(votes: [StageVote(BillStages.Adoption)]), Record(BillRecordStatuses.Closed), formats, text);
        closed.Stage.ShouldBe("Priėmimas · Uždarytas");
        closed.Call.ShouldBeNull("nobody is asked to take part in a closed record");

        var none = BillPreview.Of(Sample(votes: [StageVote(BillStages.Presentation)]), null, formats, text);
        (none.Title, none.Stage, none.Call).ShouldBe((Official, "Pateikimas", (string?)null));
    }

    [Fact]
    public void The_image_is_1200_by_630_and_a_long_title_takes_at_most_four_lines()
    {
        var (formats, text, scope) = Services();
        using var _ = scope;
        var png = BillPreviewImage.Draw(BillPreview.Of(Sample(), Record(reported: true), formats, text));
        SizeOf(png).ShouldBe((1200, 630));

        using var font = new SKFont(PreviewDrawing.Display, BillPreviewImage.TitleSize);
        var lines = PreviewDrawing.Lines(string.Join(' ', Enumerable.Repeat(Official, 4)), font, PreviewDrawing.ContentWidth, BillPreviewImage.TitleLines);
        lines.Count.ShouldBe(4);
        lines[^1].ShouldEndWith("…");
        lines.ShouldAllBe(l => font.MeasureText(l) <= PreviewDrawing.ContentWidth);
    }
}
