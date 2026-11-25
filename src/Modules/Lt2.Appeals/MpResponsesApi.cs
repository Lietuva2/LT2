using Lt2.SharedKernel;

namespace Lt2.Appeals;

// Design 8.5.5 to 8.5.7: MPs' responses on bills. The module keys a response by the Seimas's person number and the bill's
// number and knows nothing of the MP's link but the account that last wrote it, which no answer here carries. Names and
// factions are joined by the host from the Parliament module. Codes: stance for | against | undecided; status published |
// withdrawn | hidden; ballot for | against | abstain | did-not-vote | consensus.

/// <summary>An earlier public version of a response: what it said, when it was written and when it was replaced or withdrawn.</summary>
public sealed record ResponseVersion(string Stance, string Text, DateTimeOffset WrittenAt, DateTimeOffset ReplacedAt, bool Withdrawn);

/// <summary>
/// After the adoption vote (design 8.5.5): how the MP voted, beside the response, as a fact of its own. <c>consensus</c> for a vote
/// approved by consensus (no per-MP ballots); an MP without a ballot did not vote.
/// </summary>
public sealed record ResponseVote(long VoteId, DateTimeOffset HeldAt, string Ballot);

/// <summary>
/// A response as the public sees it. While hidden, its stance, text and versions are not given, only the moderator's statement;
/// a withdrawn one keeps its versions (the last text among them).
/// </summary>
public sealed record ResponseView(
    Guid Id,
    long SeimasPersonId,
    string Status,
    string? Stance,
    string? Text,
    DateTimeOffset PublishedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? WithdrawnAt,
    string? HideStatement,
    IReadOnlyList<ResponseVersion> Earlier,
    ResponseVote? Vote)
{
    /// <summary>How many comments it shows on the bill's page (design 8.23.6); set only in the bill's answer.</summary>
    public int Comments { get; init; }
}

/// <summary>The responses on one bill. <c>Open</c>: they can still be written; <c>TakingPartClosed</c>: a moderator hid the bill's record.</summary>
public sealed record BillResponses(string BillNumber, bool Open, bool TakingPartClosed, IReadOnlyList<ResponseView> Items);

/// <summary>One MP's response on a bill, for the MP's profile.</summary>
public sealed record PersonResponse(BillFacts Bill, bool BillOpen, ResponseView Response);

/// <summary>
/// What the linked account's form on a bill's page needs (design 8.5.6): whether the bill still takes responses, why not
/// (a code: <c>response.bill-not-open</c>, <c>response.hidden</c>), and the MP's response, if any.
/// </summary>
public sealed record OwnResponse(string BillNumber, bool Open, string? Refusal, ResponseView? Response);

public sealed record NewResponse(string Stance, string Text);

public sealed record ResponseSaved(Guid Id, string Status);

/// <summary>A response in the moderators' queue: flagged by pre-screening and not yet seen, or reported. Never who wrote it.</summary>
public sealed record QueueResponse(
    Guid Id, BillFacts Bill, long SeimasPersonId, string Status, string Stance, string Text, IReadOnlyList<string> Flags, IReadOnlyList<QueueReport> Reports,
    DateTimeOffset PublishedAt, DateTimeOffset? EditedAt, bool BillOpen);

/// <summary>A hidden response on the moderators' list: who hid it, when and why, and whether it can come back (the bill is open).</summary>
public sealed record HiddenResponse(
    Guid Id, BillFacts Bill, long SeimasPersonId, string Stance, string Text, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

public sealed record ResponseModerationItems(IReadOnlyList<QueueResponse> Queue, IReadOnlyList<HiddenResponse> Hidden)
{
    /// <summary>How many responses wait in all; the list holds at most a hundred.</summary>
    public int QueueTotal { get; init; }

    public int HiddenTotal { get; init; }
}

/// <summary>How many MPs' responses the moderators' lists hold (design 8.3, the tabs' counts), each by its own query with its list's filter.</summary>
public sealed record ResponseModerationCounts(int Queue, int Hidden);

/// <summary>
/// MPs' responses on bills (design 8.5.5, 8.5.6). The caller (the host) has checked who may speak for which MP: a linked
/// account in a passkey session, the MP's mandate not ended. Rules about the bill are checked here, where the data is
/// written: only while the bill is open, nothing over a response a moderator hid, nothing from an account restricted from
/// writing. A response is never counted: nothing here touches a position, a count or a summary.
/// </summary>
public interface IMpResponses
{
    /// <summary>The public responses on a bill: every response with a status, with the ballot at the adoption vote once it is held.</summary>
    Task<Result<BillResponses>> OnBillAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>The MP's responses, the most recently written first.</summary>
    Task<IReadOnlyList<PersonResponse>> OfPersonAsync(long seimasPersonId, CancellationToken cancellationToken);

    /// <summary>The MP's own response on a bill and whether it can be written, for the linked account's form.</summary>
    Task<Result<OwnResponse>> OwnAsync(string billNumber, long seimasPersonId, CancellationToken cancellationToken);

    /// <summary>The MP's responses on the given bills (any status), by bill number, for the page of 8.5.7.</summary>
    Task<IReadOnlyDictionary<string, ResponseView>> OnBillsAsync(long seimasPersonId, IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>Publishes or changes the MP's response; public at once. A change keeps the earlier text as a public version.</summary>
    Task<Result<ResponseSaved>> SaveAsync(string billNumber, long seimasPersonId, Guid writerUserId, NewResponse request, CancellationToken cancellationToken);

    /// <summary>Withdraws the MP's response: it stays visible as withdrawn with its versions.</summary>
    Task<Result<Unit>> WithdrawAsync(string billNumber, long seimasPersonId, Guid writerUserId, CancellationToken cancellationToken);
}

/// <summary>
/// Moderation of MPs' responses after the fact (design 8.5.5, as for statements, 8.14.9): the queue (flagged and not yet
/// seen, or reported), the hidden ones, "Palikti", hiding with a statement shown in the response's place and sent to the MP's
/// account's address, and restoring while the bill is open. Each recorded and audited.
/// </summary>
public interface IMpResponseModeration
{
    Task<ResponseModerationItems> ItemsAsync(CancellationToken cancellationToken);

    /// <summary>How many responses wait and are hidden (design 8.3), each by its own query, not by reading the lists.</summary>
    Task<ResponseModerationCounts> CountsAsync(CancellationToken cancellationToken);

    Task<Result<Unit>> KeepAsync(Guid responseId, string? note, StaffActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> HideAsync(Guid responseId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    Task<Result<Unit>> RestoreAsync(Guid responseId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);
}
