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
    /// A difference of two shares in percentage points with one decimal ("14,8 p. p."), so it reads right beside two shares
    /// shown as whole percents; null when there is none.
    /// </summary>
    public static string? Points(decimal? difference) =>
        difference is { } d ? Math.Round(d * 100, 1, MidpointRounding.AwayFromZero).ToString("0.0", Lithuanian) + " p. p." : null;

    /// <summary>A share as a place on a track, in per cent of its width, for an SVG attribute ("43.2%").</summary>
    public static string Position(decimal share) =>
        Math.Round(share * 100, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
