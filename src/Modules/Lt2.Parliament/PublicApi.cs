namespace Lt2.Parliament;

/// <summary>What one import run did.</summary>
/// <param name="Status"><c>succeeded</c>, <c>failed</c> (the service failed; what was fetched is kept and the next run
/// resumes) or <c>skipped</c> (another import was running).</param>
/// <param name="Changes">Data rows added, changed or removed.</param>
/// <param name="Synthetic">At least one response came from the stand-in, so the data is invented.</param>
public sealed record ImportOutcome(Guid? RunId, string Status, int Requests, int Changes, bool Synthetic, string? Error);

/// <summary>
/// The Seimas open-data import (design 7.2), run by the worker against whatever <c>Seimas:BaseAddress</c> is. Every
/// response is stored raw before it is parsed; upserts are keyed by Seimas identifiers, so running it again changes
/// nothing, and a run that failed half-way is resumed by the next.
/// </summary>
public interface IParliamentImport
{
    /// <summary>Runs the import now.</summary>
    Task<ImportOutcome> RunAsync(CancellationToken cancellationToken);

    /// <summary>Design 7.2: every 15 minutes on sitting days, hourly otherwise, at once if it never succeeded.</summary>
    Task<bool> IsDueAsync(CancellationToken cancellationToken);
}

/// <summary>For the pages: whether the parliament data shown is invented (imported from the stand-in).</summary>
public interface IParliamentDataNotice
{
    /// <summary>True when any stored Seimas response came from the stand-in. Stays true until the parliament schema is
    /// emptied, because parsed rows may come from those responses.</summary>
    Task<bool> IsParliamentDataInventedAsync(CancellationToken cancellationToken);
}
