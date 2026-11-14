namespace Lt2.SharedKernel;

/// <summary>Outbox message kinds and payloads shared between the module that writes them and the one that sends them.</summary>
public static class MessageKinds
{
    public const string SignInCodeEmail = "email.sign-in-code";

    public const string AccountNoticeEmail = "email.account-notice";

    public const string ModerationNoticeEmail = "email.moderation-notice";

    public const string WritingRestrictionEmail = "email.writing-restriction";

    public const string FeedbackReplyEmail = "email.feedback-reply";

    public const string FeedbackNoticeEmail = "email.feedback-notice";


    /// <summary>A followed bill has a planned date (design 8.11.2).</summary>
    public const string FollowPlannedEmail = "email.follow-planned";

    /// <summary>The Seimas held a stage vote on a followed bill (design 8.11.2).</summary>
    public const string FollowVotedEmail = "email.follow-voted";
}


/// <summary>
/// Payload of <see cref="MessageKinds.FollowPlannedEmail"/> (design 8.11.2): a followed bill's planned date. Nothing about
/// the person's side, the appeal's counts or other people.
/// </summary>
/// <param name="Stage"><c>presentation</c>, <c>consideration</c>, <c>adoption</c> or null.</param>
public sealed record FollowPlannedEmail(string To, string BillNumber, string BillTitle, string? Stage, DateOnly Date, string BillPath, string AccountPath);

/// <summary>How one MP the person follows voted: <c>for</c>, <c>against</c>, <c>abstain</c>, <c>did-not-vote</c>, or null (no per-MP votes).</summary>
public sealed record FollowedMpChoice(string Name, string? Choice);

/// <summary>
/// Payload of <see cref="MessageKinds.FollowVotedEmail"/> (design 8.11.2): a stage vote on a followed bill, with the result
/// and the official note, and how each MP the person follows voted. Nothing about the person's side or other people.
/// </summary>
public sealed record FollowVotedEmail(
    string To,
    string BillNumber,
    string BillTitle,
    string? Stage,
    DateOnly Day,
    bool IsConsensus,
    int? For,
    int? Against,
    int? Abstain,
    string? OfficialNote,
    string VotePath,
    IReadOnlyList<FollowedMpChoice> Mps,
    string AccountPath);

/// <summary>
/// Payload of <see cref="MessageKinds.WritingRestrictionEmail"/>: a moderator restricted the person from writing, or lifted
/// that (design 8.3). Never carries a side or a position.
/// </summary>
/// <param name="To">The address on the account.</param>
/// <param name="Notice">One of <see cref="WritingRestrictionNotices"/>.</param>
/// <param name="Until">The last day of a dated restriction; null = until lifted (and for a lifting).</param>
/// <param name="Statement">The moderator's statement of reasons.</param>
/// <param name="Contact">The moderators' address for objecting, when configured.</param>
public sealed record WritingRestrictionEmail(string To, string Notice, DateOnly? Until, string Statement, string? Contact);

/// <summary>What a person is told about a writing restriction.</summary>
public static class WritingRestrictionNotices
{
    public const string Restricted = "restricted";
    public const string Lifted = "lifted";

    public static readonly IReadOnlyList<string> All = [Restricted, Lifted];
}

/// <summary>
/// Payload of <see cref="MessageKinds.ModerationNoticeEmail"/>: tells the author what a moderator did with their argument,
/// and why (design 8.3, the statement of reasons). Never carries a side or a position.
/// </summary>
/// <param name="To">The author's address.</param>
/// <param name="Notice">One of <see cref="ModerationNotices"/>.</param>
/// <param name="BillNumber">The bill the argument is on, for example <c>XVP-0412</c>.</param>
/// <param name="BillTitle">The bill's official title.</param>
/// <param name="Statement">The moderator's statement of reasons; may be empty when something was published.</param>
/// <param name="Excerpt">For an argument: the beginning of its text, so the author knows which one.</param>
/// <param name="Path">The site path of the bill's page the notice points to, for example <c>/seimas/XVP-0412</c>.</param>
public sealed record ModerationNoticeEmail(string To, string Notice, string BillNumber, string BillTitle, string? Statement, string? Excerpt = null, string? Path = null);

/// <summary>
/// The moderation outcomes an argument's author is told about (design 8.3, 8.6). A bill's record has no author since
/// 2026-11-14 (design 8.12.4), so nothing done to a record is emailed.
/// </summary>
public static class ModerationNotices
{
    public const string ReasonPublished = "reason-published";
    public const string ReasonHidden = "reason-hidden";
    public const string ReasonMerged = "reason-merged";
    public const string ReasonRestored = "reason-restored";

    public static readonly IReadOnlyList<string> All = [ReasonPublished, ReasonHidden, ReasonMerged, ReasonRestored];
}

/// <summary>Payload of <see cref="MessageKinds.SignInCodeEmail"/>.</summary>
/// <param name="To">Recipient address as the person typed it.</param>
/// <param name="Code">The six-digit code.</param>
/// <param name="ValidMinutes">How long the code is valid, for the email text.</param>
public sealed record SignInCodeEmail(string To, string Code, int ValidMinutes);

/// <summary>
/// Payload of <see cref="MessageKinds.AccountNoticeEmail"/>: tells a person that something security-relevant changed on
/// their account (design 6.2). Never carries a code.
/// </summary>
/// <param name="To">The address on the account.</param>
/// <param name="Notice">One of <see cref="AccountNotices"/>.</param>
/// <param name="Role">The staff role concerned, for role notices.</param>
/// <param name="PasskeysRemoved">How many passkeys were removed, where that applies.</param>
public sealed record AccountNoticeEmail(string To, string Notice, string? Role = null, int PasskeysRemoved = 0);

/// <summary>The account changes a person is told about by email (design 6.2).</summary>
public static class AccountNotices
{
    public const string PasskeyAdded = "passkey-added";
    public const string RoleGranted = "role-granted";
    public const string RoleActivated = "role-activated";
    public const string AccountReset = "account-reset";

    public static readonly IReadOnlyList<string> All = [PasskeyAdded, RoleGranted, RoleActivated, AccountReset];
}

/// <summary>Payload of <see cref="MessageKinds.FeedbackReplyEmail"/>: the team's reply to a member's feedback (design 8.10).</summary>
/// <param name="To">The member's address; staff never see it.</param>
/// <param name="Reply">The team's reply.</param>
/// <param name="Excerpt">The beginning of the member's own text, so they know which item.</param>
public sealed record FeedbackReplyEmail(string To, string Reply, string Excerpt);

/// <summary>Payload of <see cref="MessageKinds.FeedbackNoticeEmail"/>: how many new feedback items wait, never their text.</summary>
public sealed record FeedbackNoticeEmail(string To, int Count, string StaffPath);
