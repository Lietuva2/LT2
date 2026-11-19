using Lt2.SharedKernel;
using Lt2.Notifications.MpMail;

namespace Lt2.Notifications.Outbox;

/// <summary>
/// The three email streams (design 8.19.3): each has its own sender address, may have its own SMTP account and has its own
/// daily ceiling, so a flood on one can never stop another. Which stream an outbox kind goes on is decided here, in one place,
/// beside the kinds: <see cref="Of"/> throws for a kind that is not in the table, and a unit test fails until a new kind is added.
/// </summary>
internal static class EmailStreams
{
    /// <summary>Every email that lets a person into their account or protects it: codes, links that restore an address, security notices.</summary>
    public const string Codes = "codes";

    /// <summary>Everything else to members and the team: follow emails, gathered emails, moderation and report notices, feedback, organisations.</summary>
    public const string Notices = "notices";

    /// <summary>MP mail (design 8.19).</summary>
    public const string Mp = MpMailKinds.Stream;

    /// <summary>The streams in the order the dispatcher serves them: codes first.</summary>
    public static readonly IReadOnlyList<string> All = [Codes, Notices, Mp];

    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        // codes: a person waits for these with the code in hand, or they protect the account.
        [MessageKinds.SignInCodeEmail] = Codes,
        [MessageKinds.AccountNoticeEmail] = Codes, // passkey added, role granted, account reset, address changed...
        [MessageKinds.EmailChangeCodeEmail] = Codes,
        [MessageKinds.EmailChangeTakenEmail] = Codes,
        [MessageKinds.EmailChangedEmail] = Codes,
        [MessageKinds.EmailRestoredEmail] = Codes,
        [MessageKinds.OrganisationCodeEmail] = Codes,
        [MessageKinds.MpTakeoverLinkEmail] = Codes, // the link that gives access to an MP's profile

        // notices: members and the team.
        [MessageKinds.ModerationNoticeEmail] = Notices,
        [MessageKinds.ReportDecidedEmail] = Notices,
        [MessageKinds.WritingRestrictionEmail] = Notices,
        [MessageKinds.FeedbackReplyEmail] = Notices,
        [MessageKinds.FeedbackNoticeEmail] = Notices,
        [MessageKinds.FollowPlannedEmail] = Notices,
        [MessageKinds.FollowVotedEmail] = Notices,
        [MessageKinds.FollowClosedEmail] = Notices,
        [MessageKinds.FollowStatementEmail] = Notices,
        [MessageKinds.FollowResponsesEmail] = Notices,
        [MessageKinds.FollowMemberEmail] = Notices,
        [MessageKinds.GatheredNoticesEmail] = Notices,
        [MessageKinds.CommentNoticeEmail] = Notices,
        [MessageKinds.OrganisationNoticeEmail] = Notices,
        [MessageKinds.MpOfficeInvitationEmail] = Notices,
        [MessageKinds.MpResponseNoticeEmail] = Notices,
        [MessageKinds.ProfileNoticeEmail] = Notices,
        [MessageKinds.DigestReviewEmail] = Notices,

        // mp: MP mail.
        [MpMailKinds.Digest] = Mp,
        [MpMailKinds.Introduction] = Mp,
        [MpMailKinds.Notice] = Mp,
    };

    /// <summary>Every kind that has a stream.</summary>
    public static IReadOnlyCollection<string> Kinds => Table.Keys;

    /// <summary>The stream an outbox kind is sent on.</summary>
    public static string Of(string kind) =>
        Table.TryGetValue(kind, out var stream) ? stream : throw new InvalidOperationException($"Outbox kind '{kind}' has no email stream; add it to EmailStreams.");

    /// <summary>The kinds on a stream.</summary>
    public static string[] KindsOf(string stream) => [.. Table.Where(e => e.Value == stream).Select(e => e.Key)];
}
