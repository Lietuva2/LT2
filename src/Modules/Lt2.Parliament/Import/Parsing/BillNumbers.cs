using System.Text.RegularExpressions;

namespace Lt2.Parliament.Import.Parsing;

/// <summary>
/// A Seimas bill's registration number as lrs.lt writes it, which is the bill's identity here (<c>parliament.bill.number</c>,
/// the Appeals module's records): <c>XVP-0412</c>, or with the letters some numbers carry, <c>XIVP-4009GR</c> (a bill of the
/// last term taken up again). A revision, "(2)", is a later text of the same bill and is left out, so every revision is
/// one bill. One rule for the open data's titles, the upcoming-agenda pages and TAIS's project numbers.
/// </summary>
internal static partial class BillNumbers
{
    // The letters count only as a whole word part: "XVP-0412Svarstymas" (text run together) is XVP-0412.
    [GeneratedRegex(@"\b(?<base>[IVX]{1,5}P-\d{1,5})(?:\(\d+\))?(?:(?<letters>[A-Z]{1,3})(?!\p{L}))?(?:\(\d+\))?", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\((?<revision>\d{1,4})\)", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    /// <summary>The first registration number in <paramref name="text"/>, without its revision; null when there is none.</summary>
    public static string? First(string text) =>
        Pattern().Match(text) is { Success: true } m ? m.Groups["base"].Value + m.Groups["letters"].Value : null;

    /// <summary>
    /// The number when <paramref name="text"/> is a registration number and nothing else (TAIS's <c>projectRegistrationNo</c>:
    /// <c>XVP-1334(2)</c> is XVP-1334, revision 2), with its revision, 1 when it has none; null for anything else, such as
    /// another institution's project (<c>26-14366</c>).
    /// </summary>
    public static (string Number, int Revision)? Whole(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        var m = Pattern().Match(trimmed);
        if (!m.Success || m.Index != 0 || m.Length != trimmed.Length)
        {
            return null;
        }

        var revision = RevisionPattern().Matches(m.Value)
            .Select(r => int.Parse(r.Groups["revision"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(1)
            .Max();
        return (m.Groups["base"].Value + m.Groups["letters"].Value, revision);
    }
}
