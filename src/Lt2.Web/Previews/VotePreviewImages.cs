using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Components;
using Lt2.Web.Components.Parliament;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Previews;

/// <summary>
/// Serves a vote's link-preview image (design 8.22.5, owner 2026-11-26) at <c>/img/votes/{id}/{version}.png</c>. Nothing is
/// made ahead: the image is drawn when someone first asks for it (a network reading a shared link, as a rule) and kept in
/// memory, bounded, for the next one. The version is a digest of what is drawn, so the address a page names changes with the
/// picture and the answer may be kept by anyone for a year; an older version's address leads to the current one.
/// </summary>
internal sealed class VotePreviewImages : IDisposable
{
    /// <summary>About 300 images of some 40 KB.</summary>
    private const long CacheBytes = 12 * 1024 * 1024;

    private readonly MemoryCache _images = new(new MemoryCacheOptions { SizeLimit = CacheBytes });

    /// <summary>The vote's current preview with the record's part, as the vote's page builds it.</summary>
    public static async Task<VotePreview?> PreviewAsync(
        long id, IParliamentApi parliament, IAppealsApi appeals, Formats formats, IStringLocalizer<PageText> text, CancellationToken cancellationToken)
    {
        if ((await parliament.GetVoteAsync(id, cancellationToken)).Value is not { } vote)
        {
            return null;
        }

        var reported = false;
        if (vote.Summary.Vote.Bill is { } bill)
        {
            (reported, _) = VotePreview.FromRecord((await appeals.GetRecordAsync(bill.Number, cancellationToken)).Value);
        }

        return VotePreview.Of(vote, reported, formats, text);
    }

    public byte[] ImageOf(VotePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var key = (preview.VoteId, preview.Version);
        if (_images.TryGetValue(key, out byte[]? kept) && kept is not null)
        {
            return kept;
        }

        var png = VotePreviewImage.Draw(preview);
        _images.Set(key, png, new MemoryCacheEntryOptions { Size = png.Length, SlidingExpiration = TimeSpan.FromDays(7) });
        return png;
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapMethods("/img/votes/{id:long}/{version}.png", [HttpMethods.Get, HttpMethods.Head], async (
                long id, string version, HttpContext http, VotePreviewImages images, IParliamentApi parliament, IAppealsApi appeals,
                Formats formats, IStringLocalizer<PageText> text, CancellationToken cancellationToken) =>
            {
                if (await PreviewAsync(id, parliament, appeals, formats, text, cancellationToken) is not { } preview)
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
