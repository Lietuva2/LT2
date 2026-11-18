using System.Globalization;

namespace Lt2.Web.Client.Analytics;

/// <summary>How the analytics page writes a share "for" (design 8.26.2), beside the line at 50 %.</summary>
public static class AnalyticsFormat
{
    private static readonly NumberFormatInfo Lithuanian = new() { NumberDecimalSeparator = "," };

    /// <summary>
    /// A share as a whole percent (<see cref="Display.Percent"/>), except one that rounds to 50 % without being exactly
    /// half: then with as many decimals (up to three) as it takes not to read as 50 %, so a majority and its absence never
    /// look the same ("50,4 %" and "49,6 %", not "50 %" twice).
    /// </summary>
    public static string? Share(decimal? share)
    {
        if (share is not { } s)
        {
            return null;
        }

        var percent = s * 100;
        if (s == 0.5m || Math.Round(percent, MidpointRounding.AwayFromZero) != 50)
        {
            return Display.Percent(s);
        }

        var decimals = 1;
        while (decimals < 3 && Math.Round(percent, decimals, MidpointRounding.AwayFromZero) == 50)
        {
            decimals++;
        }

        return Math.Round(percent, decimals, MidpointRounding.AwayFromZero).ToString("0." + new string('0', decimals), Lithuanian) + " %";
    }

    /// <summary>
    /// A difference of two shares as a whole percent, like the shares beside it ("15 %"; owner, 2026-11-18: "%", not
    /// "p. p."); null when there is none.
    /// </summary>
    public static string? Difference(decimal? difference) => Display.Percent(difference);

    /// <summary>A share as a place on a track, in per cent of its width, for an SVG attribute ("43.2%").</summary>
    public static string Position(decimal share) =>
        Math.Round(share * 100, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// A bill's label in the dot plot (design 8.26.1 item 2, owner 2026-11-19): its number and title, at most
    /// <paramref name="max"/> characters. A longer one is cut at the last whole word that fits and ends in "…" (SVG text does
    /// not wrap); the full title is the link's name and stands in the table.
    /// </summary>
    public static string Label(string number, string title, int max)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(title);
        var full = string.IsNullOrWhiteSpace(title) ? number : number + " " + title.Trim();
        if (full.Length <= max)
        {
            return full;
        }

        var cut = full[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > number.Length)
        {
            cut = cut[..space];
        }

        return cut.TrimEnd(' ', ',', ';', ':', '–', '-', '.') + "…";
    }
}
