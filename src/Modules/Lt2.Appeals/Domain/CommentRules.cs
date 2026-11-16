using Lt2.Appeals.Screening;

namespace Lt2.Appeals.Domain;

/// <summary>
/// The rules of comments (design 8.23) as pure functions: what may be written where and in whose name, who deletes, who agrees,
/// and what pre-screening refuses. Nothing here counts a comment towards anything: there is no function that adds comments or
/// their "Sutinku" to a bill's numbers, on purpose (design 8.23.1).
/// </summary>
internal static class CommentRules
{
    /// <summary>What pre-screening refuses in a comment (design 8.23.3, owner 2026-11-16): personal data; a link is allowed.</summary>
    public static readonly IReadOnlyList<string> RefusedFlags = [ScreeningFlags.Email, ScreeningFlags.Phone, ScreeningFlags.PersonalCode];

    /// <summary>The text: not blank and no longer than <c>Texts:MaxLength</c> allows (<paramref name="maxLength"/>, 0 = no limit).</summary>
    public static string? CheckText(string? text, int maxLength)
    {
        var normalised = AppealRules.NormaliseText(text);
        return normalised.Length == 0 ? "comment.empty" : AppealRules.TooLong(normalised, maxLength) ? "comment.too-long" : null;
    }

    /// <summary>The flags that refuse the comment, in the order found; empty when it may be published (a link alone passes).</summary>
    public static IReadOnlyList<string> RefusedBy(IEnumerable<string> flags) => flags.Where(RefusedFlags.Contains).ToList();

    /// <summary>A kind switched off takes no new comments; what was written stays readable (design 8.23.5).</summary>
    public static string? CheckSwitch(bool kindOn) => kindOn ? null : "comments.off";

    /// <summary>
    /// The bill takes comments while it takes part (design 8.23.3, 8.23.7): active (no final vote) and its record, if any, open.
    /// A hidden record shows nothing of the bill's texts, so nothing is written there either.
    /// </summary>
    public static string? CheckBillOpen(string? recordStatus, bool billFinished) =>
        RecordRules.CheckParticipation(recordStatus, billFinished) is null ? null : "comment.bill-not-open";

    /// <summary>
    /// In whose name the caller writes under a target (design 8.23.2), decided by the server: a current representative of the
    /// organisation whose statement it is speaks as the organisation; the MP's account or an office member under that MP's own
    /// response speaks as the MP; everyone else as a person. The MP's own account comments nowhere else
    /// (<c>mp.takes-no-part</c>, design 8.5.4).
    /// </summary>
    /// <param name="kind">The target's kind.</param>
    /// <param name="representsTargetOrganisation">The caller represents the organisation whose statement the target is.</param>
    /// <param name="targetMp">For a response: the MP who gave it.</param>
    /// <param name="speaksForMp">The MP the caller's account is linked to (the MP's own or an office member), if any.</param>
    /// <param name="isMpAccount">The caller is an MP's own linked account.</param>
    /// <returns>The voice, or the refusal's code.</returns>
    public static (string? Voice, string? Refusal) Voice(string kind, bool representsTargetOrganisation, long? targetMp, long? speaksForMp, bool isMpAccount)
    {
        if (kind == CommentKinds.Statement && representsTargetOrganisation)
        {
            return (CommentVoices.Organisation, null);
        }

        if (kind == CommentKinds.Response && targetMp is { } mp && speaksForMp == mp)
        {
            return (CommentVoices.Mp, null);
        }

        return isMpAccount ? (null, "mp.takes-no-part") : (CommentVoices.Person, null);
    }

    /// <summary>
    /// Speaking as the MP needs a passkey session, as writing the response does (design 8.23.2, 8.5.4), and an MP whose mandate
    /// has not ended (8.23.14, proposed).
    /// </summary>
    public static string? CheckMpVoice(bool passkeySession, bool sitting) =>
        !passkeySession ? "mp.passkey-required" : !sitting ? "response.mandate-ended" : null;

    /// <summary>
    /// The ceiling per person per text (design 8.23.4): <c>Comments:PerPersonPerTarget</c>, 0 = no limit; counts only shown
    /// comments, so switching it on later removes nothing.
    /// </summary>
    public static string? CheckCeiling(int shownByPerson, int limit) => limit > 0 && shownByPerson >= limit ? "comment.limit" : null;

    /// <summary>
    /// Who may delete a comment (design 8.23.3): its own author for a person's comment; any current representative for one in an
    /// organisation's name; the MP's account or an office member, in a passkey session, for one in the MP's name.
    /// </summary>
    public static bool MayDelete(string voice, bool isAuthor, bool representsOrganisation, bool speaksForTheMp, bool passkeySession) => voice switch
    {
        CommentVoices.Person => isAuthor,
        CommentVoices.Organisation => representsOrganisation,
        CommentVoices.Mp => speaksForTheMp && passkeySession,
        _ => false,
    };

    /// <summary>
    /// "Sutinku" (design 8.23.10): any member but the MP's own account, never on a comment the caller wrote (in any voice), only
    /// while the bill is open. Taking it back is refused only on a closed bill.
    /// </summary>
    public static string? CheckAgreement(bool wroteIt, bool isMpAccount, bool billOpen) =>
        isMpAccount ? "mp.takes-no-part" : wroteIt ? "agreement.own-comment" : !billOpen ? "comment.bill-not-open" : null;

    /// <summary>
    /// Whether a comment is shown on its bill's page (design 8.23.7): it is published, its target is shown and the bill's record is
    /// not hidden. Deleted ones are not rows any more.
    /// </summary>
    public static bool IsShown(string status, bool targetShown, bool recordHidden) =>
        status == CommentStatuses.Published && targetShown && !recordHidden;

    /// <summary>A moderator hides a published comment and restores a hidden one; "Palikti" answers reports on a published one.</summary>
    public static bool MayHide(string status) => status == CommentStatuses.Published;

    public static bool MayRestore(string status) => status == CommentStatuses.Hidden;

    public static bool MayKeep(string status) => status == CommentStatuses.Published;

    /// <summary>How many comments a thread shows (design 8.23.6): 10 at first, 10 more each time, never fewer than 10.</summary>
    public static int Shown(int? asked, int total) => Math.Min(Math.Max(asked ?? First, First), Math.Max(total, First));

    /// <summary>The comments a thread shows first.</summary>
    public const int First = 10;

    /// <summary>The comments "Rodyti daugiau komentarų" adds.</summary>
    public const int Step = 10;
}
