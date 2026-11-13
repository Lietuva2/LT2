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
}

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
/// Payload of <see cref="MessageKinds.ModerationNoticeEmail"/>: tells the author what a moderator did with their appeal
/// or argument, and why (design 8.3, the statement of reasons). Never carries a side or a position.
/// </summary>
/// <param name="To">The author's address.</param>
/// <param name="Notice">One of <see cref="ModerationNotices"/>.</param>
/// <param name="BillNumber">The bill the appeal is about, for example <c>XVP-0412</c>.</param>
/// <param name="BillTitle">The bill's official title.</param>
/// <param name="Statement">The moderator's statement of reasons; may be empty when something was published.</param>
/// <param name="Excerpt">For an argument: the beginning of its text, so the author knows which one.</param>
/// <param name="Path">The site path of the appeal the notice points to, for example <c>/seimas/XVP-0412/kreipimasis</c>.</param>
public sealed record ModerationNoticeEmail(string To, string Notice, string BillNumber, string BillTitle, string? Statement, string? Excerpt = null, string? Path = null);

/// <summary>The moderation outcomes an author is told about (design 8.3, 8.6).</summary>
public static class ModerationNotices
{
    public const string AppealPublished = "appeal-published";
    public const string AppealReturned = "appeal-returned";
    public const string AppealHidden = "appeal-hidden";

    /// <summary>Another proposal on the same bill was published, so this one closed (design 8.1). Names nobody.</summary>
    public const string AppealSuperseded = "appeal-superseded";

    public const string AppealRestored = "appeal-restored";
    public const string ThresholdChanged = "threshold-changed";
    public const string ReasonPublished = "reason-published";
    public const string ReasonHidden = "reason-hidden";
    public const string ReasonMerged = "reason-merged";
    public const string ReasonRestored = "reason-restored";

    /// <summary>The initiator's proposed description change was approved or returned (design 8.1).</summary>
    public const string DescriptionApproved = "description-approved";

    public const string DescriptionReturned = "description-returned";

    /// <summary>A moderator corrected the description of a published appeal directly (design 8.1).</summary>
    public const string DescriptionCorrected = "description-corrected";

    public static readonly IReadOnlyList<string> All =
    [
        AppealPublished, AppealReturned, AppealHidden, AppealSuperseded, AppealRestored, ThresholdChanged, ReasonPublished,
        ReasonHidden, ReasonMerged, ReasonRestored, DescriptionApproved, DescriptionReturned, DescriptionCorrected,
    ];
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
