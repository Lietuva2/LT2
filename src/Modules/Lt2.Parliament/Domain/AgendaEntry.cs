namespace Lt2.Parliament.Domain;

/// <summary>
/// Where a row of <c>parliament.planned_item</c> comes from (design 5.1), as stored. Read and written only in this module;
/// the API says only whether a moderator set the date.
/// </summary>
internal static class PlannedSources
{
    /// <summary>Read from the lrs.lt agenda page by the watcher (design 8.4).</summary>
    public const string Agenda = "agenda";

    /// <summary>Entered by staff a day at a time from the day's agenda document (design 8.4, "Entering a day's agenda").</summary>
    public const string Entry = "entry";

    /// <summary>A moderator's correction of one bill's date; it takes precedence for that bill.</summary>
    public const string Moderator = "moderator";
}

/// <summary>
/// <c>parliament.agenda_entry</c> (design 5.1, 8.4): every saved day of the entered agenda, kept as sent, with who saved it,
/// when, and what the save did.
/// </summary>
internal sealed class AgendaEntry
{
    public Guid Id { get; set; }

    /// <summary>The sitting day the lines are for (Vilnius).</summary>
    public DateOnly PlannedDate { get; set; }

    /// <summary>The address of the day's agenda document.</summary>
    public string SourceUrl { get; set; } = "";

    /// <summary>The lines as sent.</summary>
    public string Body { get; set; } = "";

    public DateTimeOffset EnteredAt { get; set; }

    /// <summary>The staff member who saved it.</summary>
    public Guid EnteredBy { get; set; }

    /// <summary>The bills the day held after the save.</summary>
    public int Rows { get; set; }

    public int Added { get; set; }

    /// <summary>Bills whose stage the save changed.</summary>
    public int Changed { get; set; }

    /// <summary>Bills the save took off the day.</summary>
    public int Removed { get; set; }

    /// <summary>Bills the site did not know, created by the save.</summary>
    public int CreatedBills { get; set; }
}
