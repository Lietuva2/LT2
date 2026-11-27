using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Components;
using Lt2.Web.Components.Parliament;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Previews;

/// <summary>
/// Serves a bill's link-preview image (design 8.22.5, owner 2026-11-27) at <c>/img/bills/{number}/{version}.png</c>, as
/// <see cref="VotePreviewImages"/> does a vote's: drawn when first asked for, kept in memory (bounded), at an address whose
/// version is a digest of what is drawn; an older version's address leads to the current one.
/// </summary>
internal sealed class BillPreviewImages : IDisposable
{
    /// <summary>About 300 images of some 30 KB.</summary>
    private const long CacheBytes = 10 * 1024 * 1024;

    private readonly MemoryCache _images = new(new MemoryCacheOptions { SizeLimit = CacheBytes });

    /// <summary>The bill's current preview, as the bill's page builds it; null for a bill the site does not know.</summary>
    public static async Task<BillPreview?> PreviewAsync(
        string number, IParliamentApi parliament, IAppealsApi appeals, Formats formats, IStringLocalizer<PageText> text, CancellationToken cancellationToken)
    {
        if ((await parliament.GetBillAsync(number, cancellationToken)).Value is not { } bill)
        {
            return null;
        }

        var record = (await appeals.GetRecordAsync(bill.Number, cancellationToken)).Value;
        return BillPreview.Of(bill, record, formats, text);
    }

    public byte[] ImageOf(BillPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var key = (preview.Number, preview.Version);
        if (_images.TryGetValue(key, out byte[]? kept) && kept is not null)
        {
            return kept;
        }

        var png = BillPreviewImage.Draw(preview);
        _images.Set(key, png, new MemoryCacheEntryOptions { Size = png.Length, SlidingExpiration = TimeSpan.FromDays(7) });
        return png;
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapMethods("/img/bills/{number}/{version}.png", [HttpMethods.Get, HttpMethods.Head], async (
                string number, string version, HttpContext http, BillPreviewImages images, IParliamentApi parliament, IAppealsApi appeals,
                Formats formats, IStringLocalizer<PageText> text, CancellationToken cancellationToken) =>
            {
                if (await PreviewAsync(number, parliament, appeals, formats, text, cancellationToken) is not { } preview)
                {
                    return Results.NotFound();
                }

                if (!string.Equals(version, preview.Version, StringComparison.Ordinal))
                {
                    http.Response.Headers.CacheControl = "no-cache";
                    return Results.Redirect(preview.Path);
                }

                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Results.File(images.ImageOf(preview), "image/png");
            })
            // Like the static files, the image needs no person: answered by the routing step, before the session is looked up.
            .ShortCircuit()
            .ExcludeFromDescription();

    public void Dispose() => _images.Dispose();
}
