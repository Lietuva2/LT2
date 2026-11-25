using Lt2.Parliament.Import.Agenda;
using Lt2.Parliament.Import.Parsing;
using Lt2.SharedKernel;

namespace Lt2.Parliament.Domain;

/// <summary>One accepted line of an entered day: its 1-based line number, the bill, its stage code (or none) and title.</summary>
internal sealed record EntryLine(int Line, string Number, string? Stage, string Title);

/// <summary>A refused line: its 1-based line number and why (<see cref="AgendaEntryRefusals"/>).</summary>
internal sealed record EntryRefusal(int Line, string Reason);

/// <summary>The lines of a day as read: the accepted ones in their order (a number's first line only) and the refused ones.</summary>
internal sealed record EntryParse(IReadOnlyList<EntryLine> Lines, IReadOnlyList<EntryRefusal> Refused);

/// <summary>A bill's entered row on the day now (the row in force: <c>gone_at</c> empty).</summary>
internal sealed record EnteredRow(string Number, string? Stage);

/// <summary>What a line does to the day (<see cref="AgendaEntryChanges"/>).</summary>
internal sealed record EntryChange(EntryLine Line, string Change);

/// <summary>The day's changes: each line with what it does, and the numbers taken off the day.</summary>
internal sealed record EntryDiff(IReadOnlyList<EntryChange> Bills, IReadOnlyList<string> Removed)
{
    public int Count(string change) => Bills.Count(b => b.Change == change);
}

/// <summary>
/// Design 8.4, "Entering a day's agenda" (owner, 2026-11-25): how staff's lines for one sitting day are read, which day and
/// document address are accepted, and what saving the list does to the day's entered rows. Pure; the module's service
/// (<c>Application/AgendaEntries</c>) applies it for every caller.
/// </summary>
internal static class AgendaEntryRules
{
    /// <summary>The longest title a line may carry.</summary>
    public const int TitleMaxLength = 2000;

    /// <summary>The longest text a day may be sent as (the lines together): far above a real day's ~50 bills.</summary>
    public const int TextMaxLength = 100_000;

    /// <summary>The longest document address accepted.</summary>
    public const int SourceMaxLength = 2000;

    /// <summary>The furthest day ahead that can be entered, counted from today (Vilnius).</summary>
    public const int DaysAhead = 31;

    /// <summary>The hosts of the Seimas's own agenda documents.</summary>
    public static readonly IReadOnlyList<string> SourceHosts = ["e-seimas.lrs.lt", "www.lrs.lt"];

    /// <summary>A sitting day from today up to <see cref="DaysAhead"/> days ahead.</summary>
    public static bool DayAccepted(DateOnly day, DateOnly today) => day >= today && day <= today.AddDays(DaysAhead);

    /// <summary>
    /// The document's address, trimmed, when it is an https address on one of <see cref="SourceHosts"/> (default port, no
    /// user name); null otherwise.
    /// </summary>
    public static string? Source(string? address)
    {
        var trimmed = address?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > SourceMaxLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || !SourceHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Reads the day's lines: <c>number | stage | title</c>, exactly three fields, each trimmed; blank lines are ignored. The
    /// number as everywhere (<see cref="BillNumbers"/>: letters kept, revision dropped); the stage by the agenda watcher's rule
    /// (the first stage word in any form, a combined stage its furthest; none when the field names none); the title as written,
    /// non-empty, at most <see cref="TitleMaxLength"/> characters. A number listed twice keeps its first line.
    /// </summary>
    public static EntryParse Parse(string? text)
    {
        var lines = new List<EntryLine>();
        var refused = new List<EntryRefusal>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var raw = (text ?? "").ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < raw.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(raw[i]))
            {
                continue;
            }

            var line = i + 1;
            var fields = raw[i].Split('|');
            if (fields.Length != 3)
            {
                refused.Add(new EntryRefusal(line, AgendaEntryRefusals.Fields));
                continue;
            }

            if (BillNumbers.First(fields[0].Trim()) is not { } number)
            {
                refused.Add(new EntryRefusal(line, AgendaEntryRefusals.Number));
                continue;
            }

            var title = fields[2].Trim();
            if (title.Length == 0)
            {
                refused.Add(new EntryRefusal(line, AgendaEntryRefusals.TitleEmpty));
                continue;
            }

            if (title.Length > TitleMaxLength)
            {
                refused.Add(new EntryRefusal(line, AgendaEntryRefusals.TitleTooLong));
                continue;
            }

            if (seen.Add(number))
            {
                lines.Add(new EntryLine(line, number, StageOf(fields[1]), title));
            }
        }

        return new EntryParse(lines, refused);
    }

    /// <summary>The watcher's own rule for a stage (<see cref="AgendaParser.StageOf"/>), on the stage field alone.</summary>
    public static string? StageOf(string field) => AgendaParser.StageOf(field.Trim());

    /// <summary>
    /// What saving <paramref name="lines"/> does to a day whose entered rows are <paramref name="current"/>: the list is the whole
    /// day, so a bill not on the day is added, one with another stage has its stage changed, one with the same stage is
    /// unchanged, and an entered bill not in the list is taken off the day (numbers in order).
    /// </summary>
    public static EntryDiff Diff(IReadOnlyCollection<EnteredRow> current, IReadOnlyList<EntryLine> lines)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(lines);
        var now = current.GroupBy(r => r.Number, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var listed = lines.Select(l => l.Number).ToHashSet(StringComparer.Ordinal);
        var bills = lines
            .Select(l => new EntryChange(
                l,
                !now.TryGetValue(l.Number, out var row) ? AgendaEntryChanges.Added
                : row.Stage == l.Stage ? AgendaEntryChanges.Unchanged
                : AgendaEntryChanges.StageChanged))
            .ToList();
        var removed = now.Keys.Where(n => !listed.Contains(n)).Order(StringComparer.Ordinal).ToList();
        return new EntryDiff(bills, removed);
    }
}
