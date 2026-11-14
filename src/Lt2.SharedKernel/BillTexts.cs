namespace Lt2.SharedKernel;

/// <summary>
/// A bill's official text for its AI summary (design 8.13): the explanatory note (aiškinamasis raštas) as plain text and
/// the address it was read from, shown beside the description. Official text only: nothing users wrote, no personal data.
/// </summary>
public sealed record BillText(string Text, string Address);

/// <summary>
/// The one way the summary job gets a bill's official text (design 8.13). <b>No reader exists yet</b>: e-seimas has no API
/// and its page structure must be confirmed against live responses first (design 7.1), so the only implementation is the
/// "unavailable" one and the job makes nothing.
/// </summary>
public interface IBillTextSource
{
    /// <summary>Whether a reader exists at all; false means the job has nothing to read and makes nothing.</summary>
    bool CanRead { get; }

    /// <summary>The bill's official text, or null when it cannot be read now (the job tries the bill again after seven days).</summary>
    Task<BillText?> ReadAsync(string billNumber, CancellationToken cancellationToken);
}
