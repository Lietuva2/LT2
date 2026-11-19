using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Infrastructure;

namespace Lt2.Parliament.Import.Agenda;

/// <summary>One bill on the upcoming plenary agenda, as the lrs.lt page states it.</summary>
/// <param name="Stage"><c>presentation</c>, <c>consideration</c>, <c>adoption</c>, or <c>null</c> when the row names none.</param>
internal sealed record AgendaRow(DateOnly SittingDate, string BillNumber, string? Stage, string? SourceUrl);

/// <summary>What a page parse gave: the rows, or why the page could not be read (the layout changed).</summary>
internal sealed record AgendaParse(IReadOnlyList<AgendaRow> Rows, string? Failure)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// The parser's assumptions about the upcoming-agenda pages of lrs.lt (design 8.4: published about 10–12 days ahead,
/// changes often). None of them has been seen in a live page yet: the register
/// <c>tests/Lt2.Parliament.ImportTests/Agenda/ASSUMPTIONS.md</c> lists each one with its status, and the live check
/// (<c>LT2_LRS_LIVE=1</c>) reports on them. The parser reads text, not a fixed structure, so small layout changes do not
/// break it; when not one sitting date can be found the page is refused as a changed layout (design 8.4: alert, keep the
/// last dates and the moderators' corrections, never act on a guess).
/// </summary>
internal static class AgendaAssumptions
{
    public const string SittingDate = "A1";
    public const string BillNumber = "A2";
    public const string Stage = "A3";
    public const string Rows = "A4";
    public const string SourceLink = "A5";
    public const string Encoding = "A6";
    public const string Address = "A7";

    public static readonly IReadOnlyList<string> All = [SittingDate, BillNumber, Stage, Rows, SourceLink, Encoding, Address];
}

internal static partial class AgendaParser
{
    private static readonly string[] Months =
        ["sausio", "vasario", "kovo", "balandžio", "gegužės", "birželio", "liepos", "rugpjūčio", "rugsėjo", "spalio", "lapkričio", "gruodžio"];

    /// <summary>A1: a sitting is introduced by its date written "2026 m. lapkričio 17 d."; items follow until the next date.</summary>
    [GeneratedRegex(@"(?<y>\d{4})\s*m\.\s*(?<m>\p{L}+)\s+(?<d>\d{1,2})\s*d\.", RegexOptions.CultureInvariant)]
    private static partial Regex SittingDatePattern();

    /// <summary>A2: a bill is named by its registration number, for example XVP-0412 or XIVP-1234(2).</summary>
    [GeneratedRegex(@"\b(?<n>[XVI]{1,5}P-\d{1,5})(?:\(\d+\))?", RegexOptions.CultureInvariant)]
    private static partial Regex BillPattern();

    /// <summary>
    /// A3: the stage is named in the row: pateikimas, svarstymas or priėmimas, in any grammatical form; several in one
    /// cell, separated by commas ("Pateikimas, svarstymas, priėmimas"), are one combined stage.
    /// </summary>
    [GeneratedRegex(@"(?i)\b(?:pateikim|svarstym|priėmim)\w*(?:\s*,\s*(?:pateikim|svarstym|priėmim)\w*)*", RegexOptions.CultureInvariant)]
    private static partial Regex StagePattern();

    [GeneratedRegex(@"(?i)\b(?:pateikim|svarstym|priėmim)\w*", RegexOptions.CultureInvariant)]
    private static partial Regex StageWord();

    /// <summary>A4: rows are table rows or list items; a header outside them still starts a sitting.</summary>
    [GeneratedRegex(@"(?i)<(?:tr|li|h[1-6])\b", RegexOptions.CultureInvariant)]
    private static partial Regex RowStart();

    /// <summary>A5: an e-seimas link in the row is its source; otherwise the page itself.</summary>
    [GeneratedRegex(@"href\s*=\s*""(?<u>https?://e-seimas\.lrs\.lt/[^""]+)""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SourceLink();

    [GeneratedRegex(@"(?is)<(script|style)\b.*?</\1>", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();

    public static AgendaParse Parse(string html, string pageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        var cleaned = ScriptOrStyle().Replace(html, " ");
        var starts = RowStart().Matches(cleaned).Select(m => m.Index).Prepend(0).Append(cleaned.Length).Distinct().Order().ToList();
        var rows = new List<AgendaRow>();
        DateOnly? sitting = null;
        var sittings = 0;
        for (var i = 0; i + 1 < starts.Count; i++)
        {
            var segment = cleaned[starts[i]..starts[i + 1]];
            var text = Space().Replace(WebUtility.HtmlDecode(Tag().Replace(segment, " ")), " ").Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var bill = BillPattern().Match(text);
            if (!bill.Success && DateOf(text) is { } date)
            {
                sitting = date;
                sittings++;
                continue;
            }

            if (bill.Success && sitting is { } day)
            {
                var link = SourceLink().Match(segment);
                rows.Add(new AgendaRow(day, bill.Groups["n"].Value, StageOf(text), link.Success ? WebUtility.HtmlDecode(link.Groups["u"].Value) : pageUrl));
            }
        }

        if (sittings == 0)
        {
            return new AgendaParse([], "no sitting date found: the page layout may have changed");
        }

        // A bill twice on one sitting (two agenda items): keep the first.
        return new AgendaParse(rows.DistinctBy(r => (r.SittingDate, r.BillNumber)).ToList(), null);
    }

    internal static DateOnly? DateOf(string text)
    {
        var match = SittingDatePattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var month = Array.IndexOf(Months, match.Groups["m"].Value.ToLower(CultureInfo.GetCultureInfo("lt-LT"))) + 1;
        if (month == 0 || !int.TryParse(match.Groups["y"].Value, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(match.Groups["d"].Value, CultureInfo.InvariantCulture, out var day) || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// The stage found first in the row, each word read by the same rule as the open data's stages (<see cref="VoteRules.Stage"/>);
    /// a combined stage ("Pateikimas, svarstymas, priėmimas") is its furthest one, since the day may end in it (design 8.11.1,
    /// owner 2026-11-18: the order within a day ranks a bill by how far it may go that day).
    /// </summary>
    internal static string? StageOf(string text) => StagePattern().Match(text) is { Success: true } m
        ? Codes.Of(StageWord().Matches(m.Value).Select(w => VoteRules.Stage(w.Value)).Max())
        : null;

    /// <summary>The genitive month name, for pages and the stand-in.</summary>
    public static string MonthGenitive(int month) => Months[month - 1];
}
