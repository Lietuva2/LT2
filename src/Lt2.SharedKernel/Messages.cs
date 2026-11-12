namespace Lt2.SharedKernel;

/// <summary>Outbox message kinds and payloads shared between the module that writes them and the one that sends them.</summary>
public static class MessageKinds
{
    public const string SignInCodeEmail = "email.sign-in-code";

    public const string AccountNoticeEmail = "email.account-notice";

    public const string ModerationNoticeEmail = "email.moderation-notice";
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
public sealed record ModerationNoticeEmail(string To, string Notice, string BillNumber, string BillTitle, string? Statement, string? Excerpt = null);

/// <summary>The moderation outcomes an author is told about (design 8.3, 8.6).</summary>
public static class ModerationNotices
{
    public const string AppealPublished = "appeal-published";
    public const string AppealReturned = "appeal-returned";
    public const string AppealHidden = "appeal-hidden";
    public const string ThresholdChanged = "threshold-changed";
    public const string ReasonPublished = "reason-published";
    public const string ReasonHidden = "reason-hidden";
    public const string ReasonMerged = "reason-merged";

    public static readonly IReadOnlyList<string> All =
        [AppealPublished, AppealReturned, AppealHidden, ThresholdChanged, ReasonPublished, ReasonHidden, ReasonMerged];
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
