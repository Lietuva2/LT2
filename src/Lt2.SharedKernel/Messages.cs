namespace Lt2.SharedKernel;

/// <summary>Outbox message kinds and payloads shared between the module that writes them and the one that sends them.</summary>
public static class MessageKinds
{
    public const string SignInCodeEmail = "email.sign-in-code";

    public const string AccountNoticeEmail = "email.account-notice";

    /// <summary>The code to the address a member wants to move the account to (design 6.2).</summary>
    public const string EmailChangeCodeEmail = "email.email-change-code";

    /// <summary>To an address that already belongs to an account, when someone asked to move another account to it (design 6.2).</summary>
    public const string EmailChangeTakenEmail = "email.email-change-taken";

    /// <summary>To the old address once the account's address changed, with the link that puts it back (design 6.2).</summary>
    public const string EmailChangedEmail = "email.email-changed";

    /// <summary>To the address the account had moved to, once the old address was put back (design 6.2).</summary>
    public const string EmailRestoredEmail = "email.email-restored";

    public const string ModerationNoticeEmail = "email.moderation-notice";

    /// <summary>The decision on a member's report, to the member who reported (owner, 2026-11-17; DSA art. 16(5)).</summary>
    public const string ReportDecidedEmail = "email.report-decided";

    public const string WritingRestrictionEmail = "email.writing-restriction";

    public const string FeedbackReplyEmail = "email.feedback-reply";

    public const string FeedbackNoticeEmail = "email.feedback-notice";


    /// <summary>A followed bill has a planned date (design 8.11.2).</summary>
    public const string FollowPlannedEmail = "email.follow-planned";

    /// <summary>The Seimas held a stage vote on a followed bill (design 8.11.2).</summary>
    public const string FollowVotedEmail = "email.follow-voted";

    /// <summary>
    /// A followed bill's record was closed by a moderator because the Seimas rejected the bill or its initiators withdrew it
    /// (design 8.1 step 6, 8.6): the counterpart of the voted email where there is no vote. Under the "voted" switch.
    /// </summary>
    public const string FollowClosedEmail = "email.follow-closed";

    /// <summary>An organisation the person follows made a statement on a bill public (design 8.14.14, stage B).</summary>
    public const string FollowStatementEmail = "email.follow-statement";

    /// <summary>MPs answered on a followed bill (design 8.20.1): one part per bill, in the person's rhythm (design 8.24).</summary>
    public const string FollowResponsesEmail = "email.follow-responses";

    /// <summary>
    /// Several things for one member in one email (design 8.24): a run's events under "Iš karto", the daily or the weekly
    /// email. Each item is the payload of the email it would be alone, rendered as a line of its kind's part.
    /// </summary>
    public const string GatheredNoticesEmail = "email.gathered-notices";

    /// <summary>A code that confirms an organisation by its email domain (design 8.14.14).</summary>
    public const string OrganisationCodeEmail = "email.organisation-code";

    /// <summary>What a moderator did with an organisation or its statement, to its representatives (design 8.14.14).</summary>
    public const string OrganisationNoticeEmail = "email.organisation-notice";

    /// <summary>The link that finishes taking over an MP's profile, to the official address only (design 8.5.2).</summary>
    public const string MpTakeoverLinkEmail = "email.mp-takeover-link";

    /// <summary>An MP office member's invitation, sent by the site to the address the MP's account typed (design 8.5.3, owner 2026-11-17).</summary>
    public const string MpOfficeInvitationEmail = "email.mp-office-invitation";

    /// <summary>A moderator hid or restored an MP's response, to the MP's account's own address (design 8.5.5).</summary>
    public const string MpResponseNoticeEmail = "email.mp-response-notice";

    /// <summary>A moderator hid or restored a member's "Apie mane" (design 8.25.6), to the member's own address.</summary>
    public const string ProfileNoticeEmail = "email.profile-notice";

    /// <summary>A member the person follows published an argument (design 8.25.5).</summary>
    public const string FollowMemberEmail = "email.follow-member";

    /// <summary>
    /// New comments under the person's arguments (design 8.23.9), in the person's email rhythm (design 8.24): one notice per
    /// argument, made by <c>CommentNotices</c> (Notifications), alone or a part of the gathered email.
    /// </summary>
    public const string CommentNoticeEmail = "email.comment-notice";

    /// <summary>
    /// A summary to MPs was prepared and goes at its time unless a moderator acts (design 8.19.2, owner 2026-11-17): one to every
    /// moderator and administrator account.
    /// </summary>
    public const string DigestReviewEmail = "email.digest-review";
}

/// <summary>
/// Payload of <see cref="MessageKinds.DigestReviewEmail"/> (design 8.19.2): when the prepared summary goes, how many bills and
/// arguments it holds, and the moderators' summaries screen. Never an argument's text or anything about an MP.
/// </summary>
/// <param name="Kind"><c>weekly</c> or <c>before-vote</c>.</param>
public sealed record DigestReviewEmail(string To, string Kind, DateTimeOffset DueAt, int Bills, int Reasons, string Path);

/// <summary>
/// A count with its noun in Lithuanian: <paramref name="one"/> after 1, 21, 31 … (not 11), <paramref name="few"/> after 2–9,
/// 22–29 … (not 12–19), <paramref name="many"/> after 0, 10–20, 30 … ("1 projektas", "3 projektai", "12 projektų").
/// </summary>
public static class LithuanianCounts
{
    public static string Pick(int n, string one, string few, string many)
    {
        var tens = Math.Abs(n) % 100;
        var units = Math.Abs(n) % 10;
        return tens is >= 11 and <= 19 || units == 0 ? many : units == 1 ? one : few;
    }
}

/// <summary>One argument of the person's that got a comment (design 8.23.9): the bill and the link to the argument; never the comment.</summary>
public sealed record CommentedArgumentLine(string BillNumber, string BillTitle, string Path, DateOnly? Day = null);

/// <summary>
/// Payload of <see cref="MessageKinds.CommentNoticeEmail"/> (design 8.23.9): "there are new comments under your argument", one
/// line per argument. Never a comment's text, its writer, how many, or anything about "Sutinku".
/// </summary>
public sealed record CommentNoticeEmail(string To, IReadOnlyList<CommentedArgumentLine> Arguments, string AccountPath, string? StopToken = null);

/// <summary>
/// Payload of <see cref="MessageKinds.ProfileNoticeEmail"/> (design 8.25.6): what a moderator did with the person's "Apie mane"
/// and why. Never the text and never who reported.
/// </summary>
/// <param name="Notice">One of <see cref="ProfileNotices"/>.</param>
/// <param name="Path">The "Profilis" screen.</param>
public sealed record ProfileNoticeEmail(string To, string Notice, string? Statement, string? Contact, string Path);

/// <summary>The moderation outcomes a member is told about for "Apie mane" (design 8.25.6).</summary>
public static class ProfileNotices
{
    public const string AboutHidden = "about-hidden";
    public const string AboutRestored = "about-restored";

    public static readonly IReadOnlyList<string> All = [AboutHidden, AboutRestored];
}

/// <summary>
/// Payload of <see cref="MessageKinds.FollowMemberEmail"/> (design 8.25.5): a member the person follows published an argument
/// or a comment in their own voice (<paramref name="What"/>: <see cref="MemberTextKinds"/>). The member's display name, the
/// bill and the link to the text on the bill's page; never the text, a side, or any count.
/// </summary>
public sealed record FollowMemberEmail(string To, string MemberName, string BillNumber, string BillTitle, string TextPath, string ProfilePath, string AccountPath, string What = MemberTextKinds.Argument, DateOnly? Day = null, string? StopToken = null);

/// <summary>
/// Payload of <see cref="MessageKinds.MpResponseNoticeEmail"/> (design 8.5.5): what a moderator did with an MP's response on a
/// bill and why, to the MP's account's own address (the official address when the profile was taken over). Never names who
/// wrote the response.
/// </summary>
/// <param name="Notice">One of <see cref="MpResponseNotices"/>.</param>
/// <param name="MpName">The MP's first and last name.</param>
/// <param name="Path">The bill's page.</param>
/// <param name="Statement">The moderator's statement of reasons.</param>
/// <param name="Contact">The moderators' contact address, when configured.</param>
public sealed record MpResponseNoticeEmail(
    string To, string Notice, string MpName, string BillNumber, string BillTitle, string Path, string? Statement, string? Contact);

/// <summary>The moderation outcomes an MP is told about (design 8.5.5).</summary>
public static class MpResponseNotices
{
    public const string Hidden = "response-hidden";
    public const string Restored = "response-restored";

    /// <summary>A comment in the MP's name was hidden or restored (design 8.23.8).</summary>
    public const string CommentHidden = "comment-hidden";
    public const string CommentRestored = "comment-restored";

    public static readonly IReadOnlyList<string> All = [Hidden, Restored, CommentHidden, CommentRestored];
}

/// <summary>
/// Payload of <see cref="MessageKinds.MpTakeoverLinkEmail"/> (design 8.5.2): the one-time link, sent only to the account's
/// own address, which is the MP's official address. The token lives here, in the outbox row, until the email is sent, as
/// a sign-in code does; the Identity module keeps only its hash.
/// </summary>
/// <param name="To">The official address (the account's own).</param>
/// <param name="MpName">The MP's first and last name.</param>
/// <param name="Path">The site path with the code after the <c>#</c>.</param>
/// <param name="ValidHours">How long the link is good for.</param>
/// <param name="PagePath">The take-over page without the code, where the code can be typed instead (owner, 2026-11-17).</param>
/// <param name="Code">The link's one-time code, <c>XXXX-XXXX-XXXX</c>.</param>
public sealed record MpTakeoverLinkEmail(string To, string MpName, string Path, int ValidHours, string PagePath = "", string Code = "");

/// <summary>
/// Payload of <see cref="MessageKinds.MpOfficeInvitationEmail"/> (design 8.5.3, owner 2026-11-17): an invitation to an MP's
/// office, to the address the MP's account typed. The address and the code live here, in the outbox row, until the email is
/// sent, as a sign-in code does; the Identity module keeps only the code's hash and never the address.
/// </summary>
/// <param name="To">The address the MP's account typed.</param>
/// <param name="MpName">The MP's first and last name.</param>
/// <param name="Path">The invitation's page with the code after the <c>#</c>.</param>
/// <param name="PagePath">The invitation's page without it, where the code can be typed instead.</param>
/// <param name="Code">The code, <c>XXXX-XXXX-XXXX</c>.</param>
/// <param name="ExpiresAt">When the invitation stops working.</param>
public sealed record MpOfficeInvitationEmail(string To, string MpName, string Path, string PagePath, string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Payload of <see cref="MessageKinds.OrganisationCodeEmail"/> (design 8.14.14): the code that confirms an organisation by
/// the domain of its website. The address exists only here, in the outbox row, until the email is sent; the Appeals module
/// keeps only the domain.
/// </summary>
/// <param name="To">The address on the organisation's domain that a representative gave.</param>
/// <param name="Code">The six-digit code.</param>
/// <param name="ValidMinutes">How long the code is valid.</param>
/// <param name="OrganisationName">The organisation's name, so the reader knows what is being confirmed.</param>
/// <param name="Domain">The domain being confirmed, for example <c>lgs.lt</c>.</param>
public sealed record OrganisationCodeEmail(string To, string Code, int ValidMinutes, string OrganisationName, string Domain);

/// <summary>
/// Payload of <see cref="MessageKinds.OrganisationNoticeEmail"/> (design 8.14.14): what a moderator did with an organisation
/// or one of its statements, and why, to each of its representatives. Never names another representative or a side of a
/// person.
/// </summary>
/// <param name="To">One representative's address.</param>
/// <param name="Notice">One of <see cref="OrganisationNotices"/>.</param>
/// <param name="OrganisationName">The organisation's name.</param>
/// <param name="Path">The site path the notice points to: the organisation's profile, or the bill's page for a statement.</param>
/// <param name="Statement">The moderator's statement of reasons, or the note; may be empty.</param>
/// <param name="Contact">The moderators' address for objecting, when configured.</param>
/// <param name="BillNumber">For a statement: the bill it is on.</param>
/// <param name="BillTitle">For a statement: the bill's official title.</param>
public sealed record OrganisationNoticeEmail(
    string To, string Notice, string OrganisationName, string Path, string? Statement, string? Contact, string? BillNumber = null, string? BillTitle = null);

/// <summary>The moderation outcomes an organisation's representatives are told about (design 8.14.14).</summary>
public static class OrganisationNotices
{
    public const string Blocked = "blocked";
    public const string Unblocked = "unblocked";
    public const string Kept = "kept";
    public const string Confirmed = "confirmed";
    public const string ConfirmationWithdrawn = "confirmation-withdrawn";
    public const string StatementPublished = "statement-published";
    public const string StatementKept = "statement-kept";
    public const string StatementHidden = "statement-hidden";
    public const string StatementRestored = "statement-restored";

    /// <summary>A comment in the organisation's name was hidden or restored (design 8.23.8).</summary>
    public const string CommentHidden = "comment-hidden";
    public const string CommentRestored = "comment-restored";

    public static readonly IReadOnlyList<string> All =
        [Blocked, Unblocked, Kept, Confirmed, ConfirmationWithdrawn, StatementPublished, StatementKept, StatementHidden, StatementRestored, CommentHidden, CommentRestored];
}


/// <summary>
/// Payload of <see cref="MessageKinds.FollowPlannedEmail"/> (design 8.11.2): a followed bill's planned date. Nothing about
/// the person's side, the record's counts or other people.
/// </summary>
/// <param name="Stage"><c>presentation</c>, <c>consideration</c>, <c>adoption</c> or null.</param>
public sealed record FollowPlannedEmail(string To, string BillNumber, string BillTitle, string? Stage, DateOnly Date, string BillPath, string AccountPath, string? StopToken = null);

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
    string AccountPath,
    string? StopToken = null);

/// <summary>
/// Payload of <see cref="MessageKinds.FollowClosedEmail"/> (design 8.1 step 6, 8.6): a followed bill was rejected by the Seimas
/// (<paramref name="Reason"/> <c>rejected</c>) or withdrawn by its initiators (<c>withdrawn</c>) on <paramref name="Day"/>, as
/// the source on lrs.lt says; taking part on it has closed. Nothing about the person's side, the counts or other people.
/// </summary>
public sealed record FollowClosedEmail(
    string To,
    string BillNumber,
    string BillTitle,
    string Reason,
    DateOnly Day,
    string SourceUrl,
    string BillPath,
    string AccountPath,
    string? StopToken = null);

/// <summary>
/// Payload of <see cref="MessageKinds.FollowStatementEmail"/> (design 8.14.14, stage B): an organisation the person follows
/// made its statement on a bill public. The organisation, its label, the bill and the stance; never the statement's text,
/// the person's side or any count.
/// </summary>
/// <param name="ConfirmedBy"><c>domain</c>, <c>moderator</c> or null ("Nepatvirtinta").</param>
/// <param name="ConfirmedDomain">With <c>domain</c>: the domain shown as "@domenas".</param>
/// <param name="Stance"><c>for</c>, <c>against</c> or <c>remarks</c>.</param>
/// <param name="Day">The Vilnius day it became public, shown in a gathered email (design 8.24).</param>
public sealed record FollowStatementEmail(
    string To,
    string OrganisationName,
    string? ConfirmedBy,
    string? ConfirmedDomain,
    string BillNumber,
    string BillTitle,
    string Stance,
    string BillPath,
    string AccountPath,
    DateOnly? Day = null,
    string? StopToken = null);

/// <summary>One MP's answer as the follow email lists it (design 8.20.1): the name, the faction if any, the stance; never the text.</summary>
/// <param name="Stance"><c>for</c>, <c>against</c> or <c>undecided</c>.</param>
public sealed record FollowedResponse(string Name, string? Faction, string Stance);

/// <summary>
/// Payload of <see cref="MessageKinds.FollowResponsesEmail"/> (design 8.20.1): the MPs whose responses on a followed bill became
/// public, or changed stance, on <paramref name="Day"/> and are still public, each with name, faction and stance, and the link
/// to the bill's part "Seimo narių atsakymai". Never an explanation's text, the person's side, a count or other people.
/// </summary>
public sealed record FollowResponsesEmail(
    string To,
    string BillNumber,
    string BillTitle,
    DateOnly Day,
    IReadOnlyList<FollowedResponse> Responses,
    string BillPath,
    string AccountPath,
    string? StopToken = null);

/// <summary>
/// Payload of <see cref="MessageKinds.GatheredNoticesEmail"/> (design 8.24): what one member is told in one email. Each
/// notice is the payload of the email it would be alone (its <see cref="GatheredNotice.Kind"/> says which), so a gathered
/// email holds nothing a single one would not: nothing about the person's side, a bill's counts or other people.
/// </summary>
/// <param name="To">The member's address.</param>
/// <param name="Rhythm"><c>immediate</c>, <c>daily</c> or <c>weekly</c>: which email it is.</param>
/// <param name="Notices">The things told, in the order they were found; the email orders them by part.</param>
/// <param name="AccountPath">
/// The choice of how often on the follows screen, linked as "Keisti periodiškumą" from the footer (design 8.24.1), as on every
/// email of the rhythm.
/// </param>
/// <param name="StopToken">
/// The account's stop link token (design 8.24.1), as on every email of the rhythm (each of their payloads has the same field):
/// it lives here, in the outbox row, until the email is sent, as an MP link's token does; Notifications keeps only its hash.
/// Null only where no link is made (a test).
/// </param>
public sealed record GatheredNoticesEmail(string To, string Rhythm, IReadOnlyList<GatheredNotice> Notices, string AccountPath, string? StopToken = null);

/// <summary>One thing in a gathered email: the kind of the email it would be alone, and that email's payload.</summary>
public sealed record GatheredNotice(string Kind, System.Text.Json.JsonElement Payload);

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
/// <param name="Contact">For a comment (design 8.23.8): the moderators' contact address, when configured.</param>
public sealed record ModerationNoticeEmail(string To, string Notice, string BillNumber, string BillTitle, string? Statement, string? Excerpt = null, string? Path = null, string? Contact = null);

/// <summary>
/// The decision on a member's report (owner, 2026-11-17; Digital Services Act art. 16(5): the notifier is told the decision), one
/// per member whose report on the target was open. Never the moderator, never other reporters, never more of the author than is
/// public.
/// </summary>
/// <param name="To">The reporter's own address.</param>
/// <param name="Target">The report's target kind: <c>reason</c>, <c>comment</c>, <c>statement</c>, <c>response</c>, <c>record</c> (a
/// bill's description), <c>organisation</c> or <c>profile</c>.</param>
/// <param name="Outcome">One of <see cref="ReportOutcomes"/>.</param>
/// <param name="BillNumber">The bill the text is on, when it is on one.</param>
/// <param name="BillTitle">The bill's official title.</param>
/// <param name="Name">For an organisation or a profile: its public name.</param>
/// <param name="Path">The site path where the text still stands; null when it is not shown any more.</param>
/// <param name="Statement">The moderator's explanation: the statement given with a hiding, or the optional note with "Palikti".</param>
public sealed record ReportDecidedEmail(string To, string Target, string Outcome, string? BillNumber, string? BillTitle, string? Name, string? Path, string? Statement);

/// <summary>What became of a reported text (owner, 2026-11-17), for the reporter's email.</summary>
public static class ReportOutcomes
{
    /// <summary>"Palikti": nothing against the rules was found (also publishing, confirming, restoring).</summary>
    public const string Kept = "kept";
    public const string Hidden = "hidden";

    /// <summary>An organisation blocked.</summary>
    public const string Blocked = "blocked";

    /// <summary>An organisation's confirmation withdrawn.</summary>
    public const string Unconfirmed = "unconfirmed";

    /// <summary>An argument merged into a similar one.</summary>
    public const string Merged = "merged";

    /// <summary>A description corrected by a moderator.</summary>
    public const string Corrected = "corrected";

    /// <summary>A description removed by a moderator.</summary>
    public const string Removed = "removed";

    /// <summary>Its author withdrew or deleted it before a moderator decided.</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>A comment that went with the text it stood under (withdrawn or merged).</summary>
    public const string Gone = "gone";

    public static readonly IReadOnlyList<string> All = [Kept, Hidden, Blocked, Unconfirmed, Merged, Corrected, Removed, Withdrawn, Gone];

    /// <summary>The outcome a moderator's action on a reported target means for its reporters.</summary>
    public static string Of(string action) => action switch
    {
        "hide" => Hidden,
        "block" => Blocked,
        "unconfirm" => Unconfirmed,
        "merge" => Merged,
        "describe" => Corrected,
        "undescribe" => Removed,
        _ => Kept,
    };

    /// <summary>The text still stands where it was, so the email links to it.</summary>
    public static bool StillShown(string outcome) => outcome is Kept or Corrected or Unconfirmed;
}

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

    /// <summary>A person's own comment was hidden or restored (design 8.23.8).</summary>
    public const string CommentHidden = "comment-hidden";
    public const string CommentRestored = "comment-restored";

    public static readonly IReadOnlyList<string> All = [ReasonPublished, ReasonHidden, ReasonMerged, ReasonRestored, CommentHidden, CommentRestored];
}

/// <summary>Payload of <see cref="MessageKinds.SignInCodeEmail"/>.</summary>
/// <param name="To">Recipient address as the person typed it.</param>
/// <param name="Code">The six-digit code.</param>
/// <param name="ValidMinutes">How long the code is valid, for the email text.</param>
/// <param name="Purpose">
/// Null for signing in and registering; <see cref="SignInCodePurposes.EmailChange"/> when a member asked for it on the account page to
/// prove the address in use before changing it (design 6.2): the same code, other words.
/// </param>
public sealed record SignInCodeEmail(string To, string Code, int ValidMinutes, string? Purpose = null);

/// <summary>Why a member asked for a code to the account's own address, when not to sign in (design 6.2).</summary>
public static class SignInCodePurposes
{
    /// <summary>To prove the address in use before the account's address changes.</summary>
    public const string EmailChange = "email-change";
}

/// <summary>
/// Payload of <see cref="MessageKinds.EmailChangeCodeEmail"/> (design 6.2): the code that proves the address a member wants to move
/// the account to. The address and the code live here, in the outbox row, until the email is sent; Identity keeps the code's hash.
/// </summary>
public sealed record EmailChangeCodeEmail(string To, string Code, int ValidMinutes);

/// <summary>
/// Payload of <see cref="MessageKinds.EmailChangeTakenEmail"/> (design 6.2): someone asked to move an account to this address, which
/// already belongs to an account (<paramref name="To"/> is that account's address), and nothing was changed. No code, no link.
/// </summary>
public sealed record EmailChangeTakenEmail(string To);

/// <summary>
/// Payload of <see cref="MessageKinds.EmailChangedEmail"/> (design 6.2): to the old address, that the account's address was
/// changed, when, and to which address in part (<paramref name="NewAddress"/>, <c>r***@pavyzdys.test</c>), with the one-time link
/// that puts it back. The code lives here, in the outbox row, until the email is sent; Identity keeps its hash.
/// </summary>
/// <param name="To">The old address.</param>
/// <param name="NewAddress">The new address in part.</param>
/// <param name="ChangedAt">When it changed.</param>
/// <param name="Path">The page that puts the address back, with the code after the <c>#</c>.</param>
/// <param name="PagePath">The same page without it, where the code can be typed instead.</param>
/// <param name="Code">The link's one-time code, <c>XXXX-XXXX-XXXX</c>.</param>
/// <param name="ExpiresAt">When the link stops working.</param>
public sealed record EmailChangedEmail(string To, string NewAddress, DateTimeOffset ChangedAt, string Path, string PagePath, string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Payload of <see cref="MessageKinds.EmailRestoredEmail"/> (design 6.2): to the address the account had moved to, that the earlier
/// address was put back with the old address's link and every session ended.
/// </summary>
public sealed record EmailRestoredEmail(string To, DateTimeOffset RestoredAt);

/// <summary>
/// Payload of <see cref="MessageKinds.AccountNoticeEmail"/>: tells a person that something security-relevant changed on
/// their account (design 6.2). Never carries a code.
/// </summary>
/// <param name="To">The address on the account.</param>
/// <param name="Notice">One of <see cref="AccountNotices"/>.</param>
/// <param name="Role">The staff role concerned, for role notices.</param>
/// <param name="PasskeysRemoved">How many passkeys were removed, where that applies.</param>
/// <param name="MpName">For the notices about an MP's profile (design 8.5.2, 8.5.3): the MP's first and last name.</param>
/// <param name="Statement">An administrator's statement of reasons, where one was given (ending a link, design 8.5.2).</param>
public sealed record AccountNoticeEmail(string To, string Notice, string? Role = null, int PasskeysRemoved = 0, string? MpName = null, string? Statement = null);

/// <summary>
/// The account changes a person is told about by email (design 6.2), and what happens to an MP's profile, told to the
/// official address (design 8.5.2, 8.5.3: the MP's account's own address). Never names an office member.
/// </summary>
public static class AccountNotices
{
    public const string PasskeyAdded = "passkey-added";
    public const string RoleGranted = "role-granted";
    public const string RoleActivated = "role-activated";
    public const string AccountReset = "account-reset";

    /// <summary>The profile was taken over (or a lost passkey replaced) with the link and a new passkey.</summary>
    public const string MpLinked = "mp-linked";

    /// <summary>The MP's account gave the profile up, or was deleted.</summary>
    public const string MpGivenUp = "mp-given-up";

    /// <summary>An administrator ended the link, with a statement.</summary>
    public const string MpLinkEnded = "mp-link-ended";

    public const string MpOfficeAdded = "mp-office-added";

    /// <summary>The MP's account removed an office member.</summary>
    public const string MpOfficeRemoved = "mp-office-removed";

    /// <summary>An office member left (or deleted their account).</summary>
    public const string MpOfficeLeft = "mp-office-left";

    public static readonly IReadOnlyList<string> All =
        [PasskeyAdded, RoleGranted, RoleActivated, AccountReset, MpLinked, MpGivenUp, MpLinkEnded, MpOfficeAdded, MpOfficeRemoved, MpOfficeLeft];
}

/// <summary>Payload of <see cref="MessageKinds.FeedbackReplyEmail"/>: the team's reply to a member's feedback (design 8.10).</summary>
/// <param name="To">The member's address; staff never see it.</param>
/// <param name="Reply">The team's reply.</param>
/// <param name="Excerpt">The beginning of the member's own text, so they know which item.</param>
public sealed record FeedbackReplyEmail(string To, string Reply, string Excerpt);

/// <summary>Payload of <see cref="MessageKinds.FeedbackNoticeEmail"/>: how many new feedback items wait, never their text.</summary>
public sealed record FeedbackNoticeEmail(string To, int Count, string StaffPath);
