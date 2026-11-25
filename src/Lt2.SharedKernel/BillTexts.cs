namespace Lt2.SharedKernel;

/// <summary>
/// What an official document is to the AI (design 8.11.4, 8.13): the bill's own text, its explanatory note (aiškinamasis
/// raštas), or official text a moderator pasted, whose document and address the assistant does not know.
/// </summary>
public static class BillDocumentKinds
{
    public const string ExplanatoryNote = "explanatory-note";
    public const string BillText = "bill-text";
    public const string Pasted = "pasted";
}

/// <summary>
/// One official document a description may be made from (design 8.13): its kind (<see cref="BillDocumentKinds"/>), its
/// plain text and the address it was read from (null for pasted text). Official text only: nothing users wrote, no
/// personal data.
/// </summary>
public sealed record BillDocument(string Kind, string Text, string? Address);

/// <summary>
/// The one way to read a bill's official documents for its AI description (design 8.13): the bill's text and its
/// explanatory note, each with its address, so the assistant can say which one the description rests on and that
/// address is shown beside it. <b>No reader exists yet</b>: e-seimas has no API and its page structure must be confirmed
/// against live responses first (design 7.1), so the only implementation is the "unavailable" one and the job makes
/// nothing.
/// </summary>
public interface IBillTextSource
{
    /// <summary>Whether a reader exists at all; false means the job has nothing to read and makes nothing.</summary>
    bool CanRead { get; }

    /// <summary>
    /// The bill's official documents that could be read now, the explanatory note first; empty or null when none could (the
    /// job tries the bill again after seven days).
    /// </summary>
    Task<IReadOnlyList<BillDocument>?> ReadAsync(string billNumber, CancellationToken cancellationToken);
}
