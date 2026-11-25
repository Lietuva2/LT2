using Lt2.SharedKernel;

namespace Lt2.Parliament;

/// <summary>A day of the entered agenda from today on: how many bills it holds, and its last save (when, the document).</summary>
public sealed record EnteredDay(DateOnly Day, int Bills, DateTimeOffset? LastSaved, string? SourceUrl);

/// <summary>
/// One bill of an entered day: its line, number, stage code (<see cref="BillStages"/>, or none), the title as written,
/// whether the site did not know it (<paramref name="Created"/>: saving creates it, or this save did) and what the save does to
/// it (<see cref="AgendaEntryChanges"/>).
/// </summary>
public sealed record EnteredBill(int Line, string Number, string? Stage, string Title, bool Created, string Change);

/// <summary>A refused line: its 1-based line number and why (<see cref="AgendaEntryRefusals"/>).</summary>
public sealed record EnteredLineRefusal(int Line, string Reason);

/// <summary>
/// What saving a day does (a preview, <paramref name="Saved"/> false) or did: each bill, the numbers taken off the day, and the
/// refused lines (a preview only: a save with a refused line is refused whole).
/// </summary>
public sealed record AgendaEntryOutcome(
    DateOnly Day, string SourceUrl, bool Saved, IReadOnlyList<EnteredBill> Bills, IReadOnlyList<string> Removed, IReadOnlyList<EnteredLineRefusal> Refused)
{
    public int Count(string change) => Bills.Count(b => b.Change == change);

    public int Created => Bills.Count(b => b.Created);
}

/// <summary>The refusals of <see cref="IAgendaEntries"/> (validation errors, each naming its field).</summary>
public static class AgendaEntryProblems
{
    /// <summary>The day is before today or more than 31 days ahead (field <c>day</c>).</summary>
    public const string Day = "agenda-entry.day";

    /// <summary>The address is not an https address of e-seimas.lrs.lt or www.lrs.lt (field <c>sourceUrl</c>).</summary>
    public const string Source = "agenda-entry.source";

    /// <summary>A line is refused, so nothing is applied (field <c>lines</c>).</summary>
    public const string Lines = "agenda-entry.lines";

    /// <summary>No bill listed for a day that has entered rows, as the watcher refuses an empty page (design 6.8; field <c>lines</c>).</summary>
    public const string Empty = "agenda-entry.empty";

    /// <summary>The text is longer than a day can be (field <c>lines</c>).</summary>
    public const string TooLong = "agenda-entry.too-long";
}

/// <summary>
/// Design 8.4, "Entering a day's agenda" (owner, 2026-11-25): until the Seimas gives programmatic access, staff enter each
/// sitting day's draft agenda from its e-seimas document, one line a bill (<c>number | stage | title</c>). The list is the whole
/// day: the day's entered rows (<c>planned_item.source = entry</c>) follow it, the watcher's and the moderators' rows and other
/// days are never touched, and a bill the site does not know is created. The rules are checked here for every caller; a save is
/// kept as sent (<c>agenda_entry</c>) and audited (<c>agenda.entered</c>). For moderators and administrators in a staff-passkey
/// session; the host checks that.
/// </summary>
public interface IAgendaEntries
{
    /// <summary>The days from today (Vilnius) that have entered rows, earliest first.</summary>
    Task<IReadOnlyList<EnteredDay>> DaysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What saving would do; stores nothing. Refused lines are listed in the outcome. Refusals: <c>agenda-entry.day</c>,
    /// <c>agenda-entry.source</c>, <c>agenda-entry.too-long</c>, <c>agenda-entry.empty</c>.
    /// </summary>
    Task<Result<AgendaEntryOutcome>> PreviewAsync(DateOnly day, string sourceUrl, string lines, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the lines again and applies them in one transaction, saves of the same day one at a time. Refusals as
    /// <see cref="PreviewAsync"/>, and <c>agenda-entry.lines</c> when any line is refused.
    /// </summary>
    Task<Result<AgendaEntryOutcome>> SaveAsync(DateOnly day, string sourceUrl, string lines, StaffActor staff, CancellationToken cancellationToken);
}
