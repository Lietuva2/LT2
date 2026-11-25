namespace Lt2.SharedKernel;

/// <summary>
/// Which public prompt a request is sent with (design 8.13, decided by the owner on 2026-11-14): the moderator's draft,
/// which a person edits before saving (<c>description-draft.prompt.md</c>), or the summary job's text, shown at once
/// without a person reading it (<c>description-summary.prompt.md</c>).
/// </summary>
public enum DraftPurpose
{
    ModeratorDraft,
    Summary,
}

/// <summary>
/// What a description draft is made from (design 8.11.4, 8.13): the bill's number and title and the official documents,
/// numbered from 1 in this order: the text a moderator pasted, or the bill's explanatory note and its text as the reader
/// gives them. The answer names the one the description rests on by that number. No personal data.
/// </summary>
public sealed record DraftSource(string BillNumber, string BillTitle, IReadOnlyList<BillDocument> Documents, DraftPurpose Purpose = DraftPurpose.ModeratorDraft)
{
    /// <summary>One pasted official text, as the moderator's form sends it.</summary>
    public static DraftSource Pasted(string billNumber, string billTitle, string text) =>
        new(billNumber, billTitle, [new BillDocument(BillDocumentKinds.Pasted, text, null)]);
}

/// <summary>
/// The one interface to an AI provider (design 3, 8.11.4): the Claude API (decided by the owner on 2026-11-13), on only with
/// <c>Ai:Provider</c> = <c>claude</c> and a key; otherwise the "not configured" implementation answers, and nothing in the
/// product offers a draft.
/// </summary>
public interface IAiAssistant
{
    /// <summary>Whether a provider is configured; false means the draft button is not shown.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The provider's answer in the public prompt's fixed form (a title, two to four plain Lithuanian sentences on what the
    /// source says the bill changes, the document they rest on and a public statement), read by the module;
    /// <c>draft.not-configured</c> without a provider.
    /// </summary>
    Task<Result<string>> DraftDescriptionAsync(DraftSource request, CancellationToken cancellationToken);
}
