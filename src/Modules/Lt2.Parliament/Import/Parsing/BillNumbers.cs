using System.Text.RegularExpressions;

namespace Lt2.Parliament.Import.Parsing;

/// <summary>
/// A Seimas bill's registration number as lrs.lt writes it, which is the bill's identity here (<c>parliament.bill.number</c>,
/// the Appeals module's records): <c>XVP-0412</c>, or with the letters some numbers carry, <c>XIVP-4009GR</c> (a bill of the
/// last term taken up again). A revision, "(2)", is a later text of the same bill and is left out, so every revision is
/// one bill. One rule for the open data's titles and the upcoming-agenda pages.
/// </summary>
internal static partial class BillNumbers
{
    // The letters count only as a whole word part: "XVP-0412Svarstymas" (text run together) is XVP-0412.
    [GeneratedRegex(@"\b(?<base>[IVX]{1,5}P-\d{1,5})(?:\(\d+\))?(?:(?<letters>[A-Z]{1,3})(?!\p{L}))?(?:\(\d+\))?", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The first registration number in <paramref name="text"/>, without its revision; null when there is none.</summary>
    public static string? First(string text) =>
        Pattern().Match(text) is { Success: true } m ? m.Groups["base"].Value + m.Groups["letters"].Value : null;
}
