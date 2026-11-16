using System.Globalization;
using Lt2.Api.Contracts.Mps;

namespace Lt2.Web.Client.Mps;

/// <summary>
/// Design 8.5.6: a list of MPs' responses (on the bill's page and on the profile) shows the first 10, and "Rodyti daugiau"
/// adds more by the page's own address (<c>?atsakymai=</c>), landing on the first new item, as the ballot list does (8.17), so it
/// works without JavaScript. A value the page would not make falls back to the default, never to an error.
/// </summary>
public static class ResponsePaging
{
    /// <summary>How many to show: <c>atsakymai</c> when it is a whole number from the first page's length to the list's, else that length.</summary>
    public static int Shown(string? show, int total) =>
        int.TryParse(show, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= MpResponseLimits.First && n <= Math.Max(MpResponseLimits.First, total)
            ? n
            : MpResponseLimits.First;

    /// <summary>The next value of <c>atsakymai</c>, or null when every item is shown.</summary>
    public static int? More(int shown, int total) => shown < total ? Math.Min(shown + MpResponseLimits.Step, total) : null;

    /// <summary>The page's address with this length, landing on <paramref name="fragment"/>; the default length is left out.</summary>
    public static string Address(string path, int? show, string fragment) =>
        (show is { } n && n != MpResponseLimits.First ? path + "?" + MpResponseLimits.ShowParameter + "=" + n.ToString(CultureInfo.InvariantCulture) : path) + "#" + fragment;

    /// <summary>The element id of the item at <paramref name="position"/> (1-based): "Rodyti daugiau" lands on the first new one.</summary>
    public static string ItemId(int position) => "atsakymas-" + position.ToString(CultureInfo.InvariantCulture);
}
