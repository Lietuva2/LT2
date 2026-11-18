namespace Lt2.Parliament;

/// <summary>What one import run did.</summary>
/// <param name="Status"><c>succeeded</c>, <c>failed</c> (the service failed; what was fetched is kept and the next run
/// resumes) or <c>skipped</c> (another import was running).</param>
/// <param name="Changes">Data rows added, changed or removed.</param>
/// <param name="Synthetic">At least one response came from the stand-in, so the data is invented.</param>
public sealed record ImportOutcome(Guid? RunId, string Status, int Requests, int Changes, bool Synthetic, string? Error);

/// <summary>
/// Parts of the import an administrator forces by hand (design 7.2, owner 2026-11-18). A forced part's requests are fetched
/// again however fresh they are; the rest of the walk keeps its own rules. A fetched body that is the one already applied
/// by this parser is still not applied again. The scheduled run forces <see cref="None"/>.
/// </summary>
[Flags]
public enum ImportParts
{
    None = 0,

    /// <summary>The terms, the MP list (members, memberships, contacts) and the sessions.</summary>
    Members = 1,

    /// <summary>Every session's list of sittings, and every sitting's agenda and proceedings, however old.</summary>
    Sittings = 2,

    /// <summary>Every recorded vote's per-MP result, however old. Ballots are matched to the MPs already imported.</summary>
    Votes = 4,

    /// <summary>All of them, in the walk's order (members, sittings, votes): a full re-read, as on an empty database.</summary>
    All = Members | Sittings | Votes,
}

/// <summary>Whether the scheduled import is due, and when the last run (of any kind) started.</summary>
public sealed record ImportDue(bool IsDue, DateTimeOffset? LastStart);

/// <summary>
/// The Seimas open-data import (design 7.2), run by the worker against whatever <c>Seimas:BaseAddress</c> is. Every
/// response is stored raw before it is parsed; upserts are keyed by Seimas identifiers, so running it again changes
/// nothing, and a run that failed half-way is resumed by the next.
/// </summary>
public interface IParliamentImport
{
    /// <summary>Runs the import now, forcing the given parts (<see cref="ImportParts.None"/> for the scheduled run).</summary>
    Task<ImportOutcome> RunAsync(ImportParts force, CancellationToken cancellationToken);

    /// <summary>Design 7.2: every 15 minutes on sitting days, hourly otherwise, at once if it never succeeded.</summary>
    Task<ImportDue> DueAsync(CancellationToken cancellationToken);
}

/// <summary>For the pages: whether the parliament data shown is invented (imported from the stand-in).</summary>
public interface IParliamentDataNotice
{
    /// <summary>True when any stored Seimas response came from the stand-in. Stays true until the parliament schema is
    /// emptied, because parsed rows may come from those responses.</summary>
    Task<bool> IsParliamentDataInventedAsync(CancellationToken cancellationToken);
}

/// <summary>What one agenda read did: <c>off</c> (not configured), <c>succeeded</c>, <c>refused</c> (layout changed) or <c>failed</c>.</summary>
public sealed record AgendaOutcome(string Status, int Rows, int Changes, string? Problem);

/// <summary>
/// The agenda watcher (design 8.4, 8.9): reads the upcoming plenary agenda from lrs.lt (or the local stand-in), keeps
/// the page raw, and updates the planned dates. A page it cannot read changes nothing and is logged as an error.
/// </summary>
public interface IAgendaWatch
{
    Task<AgendaOutcome> RunAsync(CancellationToken cancellationToken);
}