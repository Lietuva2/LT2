using System.Globalization;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Comments;

namespace Lt2.Web.Client.Comments;

/// <summary>A part of a comment's text: plain text, or an <c>https://</c> address shown as a link.</summary>
/// <param name="Text">The text as typed (for a link, the address).</param>
/// <param name="Href">For a link, the address the link goes to; null for plain text.</param>
/// <param name="Shown">For a link, the address as shown: whole, or shortened after its host.</param>
public sealed record CommentPart(string Text, string? Href, string? Shown);

/// <summary>
/// Links in a comment (design 8.23.3, owner 2026-11-16: allowed, shown as links, no card): an address that starts with
/// <c>https://</c> becomes a link; any other scheme and a bare name stay text. Nothing a member types becomes markup: the
/// parts are rendered as text and an <c>href</c> by the component, which encodes them.
/// </summary>
public static partial class CommentLinks
{
    /// <summary>A link's <c>rel</c> (design 8.23.3): no follow, user content, no opener, no referrer.</summary>
    public const string Rel = "nofollow ugc noopener noreferrer";

    /// <summary>The longest a shown address gets before the part after its host is cut.</summary>
    public const int ShownMaxLength = 60;

    /// <summary>The text split into plain parts and links, in order; the parts joined give the text back.</summary>
    public static IReadOnlyList<CommentPart> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<CommentPart>();
        var at = 0;
        foreach (Match match in Address().Matches(text))
        {
            var address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '"', '\'', '»', '“', '”');
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 || uri.UserInfo.Length > 0)
            {
                continue;
            }

            if (match.Index > at)
            {
                parts.Add(new CommentPart(text[at..match.Index], null, null));
            }

            parts.Add(new CommentPart(address, address, Shorten(address, uri)));
            at = match.Index + address.Length;
        }

        if (at < text.Length)
        {
            parts.Add(new CommentPart(text[at..], null, null));
        }

        return parts;
    }

    /// <summary>The address as shown: whole when short, otherwise cut after the host (never before it) with "…".</summary>
    public static string Shorten(string address, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(uri);
        if (address.Length <= ShownMaxLength)
        {
            return address;
        }

        var head = "https://" + uri.Authority;
        return address.Length <= head.Length ? address : head + address[head.Length..Math.Max(head.Length, ShownMaxLength - 1)] + "…";
    }

    [GeneratedRegex(@"https://\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Address();
}

/// <summary>
/// "Rodyti daugiau komentarų" by the page's address (design 8.23.6, 8.23.14): <c>?komentarai={target id}-{n}</c>, one target
/// at a time, so it works without script.
/// </summary>
public static class CommentPaging
{
    /// <summary>How many comments of <paramref name="targetId"/> the address asks for; null when it asks for another target or none.</summary>
    public static int? Asked(string? value, Guid targetId)
    {
        if (string.IsNullOrEmpty(value) || value.Split('-') is not [var id, var count]
            || !Guid.TryParseExact(id, "N", out var target) || target != targetId
            || !int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        return n;
    }

    /// <summary>
    /// How many comments of <paramref name="targetId"/> to show, from the page's address: "Rodyti daugiau komentarų"
    /// (<c>?komentarai=</c>) and a link to one comment (<c>?komentaras=</c>, resolved by the server into
    /// <paramref name="linked"/>: through the linked one, on the thread's own pages, 10 then 20 a step, design 8.23.6), the more of the two; null
    /// when neither names this target, and the thread then stays closed.
    /// </summary>
    public static int? Asked(string? value, Guid targetId, CommentPlaceResponse? linked)
    {
        var paged = Asked(value, targetId);
        var through = linked is { } l && l.TargetId == targetId ? BillTextLinks.ShownThrough(l.Position) : (int?)null;
        return paged is null ? through : through is null ? paged : Math.Max(paged.Value, through.Value);
    }

    /// <summary>The linked comment when it stands under <paramref name="targetId"/>: the thread marks it.</summary>
    public static Guid? Marked(Guid targetId, CommentPlaceResponse? linked) => linked is { } l && l.TargetId == targetId ? l.CommentId : null;

    /// <summary>How many to show for a total: at least the first 10, at most all, in steps the address asked for.</summary>
    public static int Shown(int? asked, int total) => Math.Min(Math.Max(asked ?? CommentLimits.First, CommentLimits.First), total);

    /// <summary>The next step, or null when every comment is shown.</summary>
    public static int? More(int shown, int total) => shown < total ? Math.Min(shown + CommentLimits.Step, total) : null;

    /// <summary>
    /// The element id of the <paramref name="position"/>-th comment of a target (1-based).
    /// </summary>
    public static string PositionAnchor(Guid targetId, int position) =>
        "comment-" + targetId.ToString("N") + "-" + position.ToString(CultureInfo.InvariantCulture);

    /// <summary>The page's path with the responses' page parameter kept (design 8.5.6), for the links the islands build.</summary>
    public static string PagePath(string number, string? responsesShown) =>
        "/seimas/" + Uri.EscapeDataString(number) + (string.IsNullOrEmpty(responsesShown) ? "" : "?" + Lt2.Api.Contracts.Mps.MpResponseLimits.ShowParameter + "=" + Uri.EscapeDataString(responsesShown));

    /// <summary>The page's address asking for <paramref name="count"/> comments of the target.</summary>
    public static string Address(string path, Guid targetId, int count)
    {
        ArgumentNullException.ThrowIfNull(path);
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return path + separator + CommentLimits.ShowParameter + "=" + targetId.ToString("N") + "-" + count.ToString(CultureInfo.InvariantCulture);
    }
}
