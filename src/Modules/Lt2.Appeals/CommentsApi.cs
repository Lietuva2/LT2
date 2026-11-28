using Lt2.SharedKernel;

namespace Lt2.Appeals;

// Design 8.23: comments ("Aptarimas") under arguments, organisations' statements and MPs' responses. Codes: kind reason |
// statement | response; voice person | organisation | mp; status published | hidden. No answer here carries an account's id:
// a person's comment carries the display name, an organisation's its name, an MP's the Seimas person number (the host joins
// the name). Nothing here touches a position, a count, a threshold or a summary.

/// <summary>
/// Whom the caller's account speaks for as an MP (design 8.5.4), as the host read it from Identity and Parliament on this
/// request: the MP, whether this session is a passkey session, and whether the MP's mandate has not ended.
/// </summary>
public sealed record CommentSpeaker(long SeimasPersonId, bool PasskeySession, bool Sitting);

/// <summary>
/// The name a comment is shown under (design 8.23.2): a person's display name (null: "Lietuva 2.0 naudotojas"); an
/// organisation's name, profile address and label with the mark "Organizacija"; or the MP (by Seimas person number) with the
/// mark "Seimo narys arba jo biuras". Never the person behind an organisation's or an MP's comment. <paramref name="ProfileId"/>:
/// a person's public profile id when they have one (design 8.25.4), never for the other voices.
/// </summary>
public sealed record CommentAuthor(string Voice, string? DisplayName, string? OrganisationName, string? OrganisationSlug, OrganisationLabel? Label, long? SeimasPersonId, string? ProfileId = null);

/// <summary>One who agreed with a comment (design 8.23.10): the display name (null: none given) and the public profile id when they have one (8.25.4).</summary>
public sealed record CommentAgreer(string? Name, string? ProfileId);

/// <summary>A shown comment: who, when, the text, and who agreed (oldest agreement first).</summary>
public sealed record CommentView(Guid Id, CommentAuthor Author, string Text, DateTimeOffset CreatedAt, IReadOnlyList<CommentAgreer> AgreedBy);

/// <summary>
/// The shown comments of one target, oldest first (design 8.23.6). <c>Count</c>: all shown; <c>Items</c>: the first
/// <c>Shown</c>. <c>Open</c>: a member may write here now (the bill open, the kind switched on, the target open to comments);
/// <c>BillOpen</c>: "Sutinku" and deleting still work (not on a closed bill). <c>Cards</c>: once per person with a public
/// profile among the names shown (authors and those who agreed, design 8.25.4).
/// </summary>
public sealed record CommentThread(
    string Kind, Guid TargetId, string BillNumber, int Count, bool Open, bool BillOpen, IReadOnlyList<CommentView> Items, IReadOnlyList<AuthorCard> Cards,
    string? NextCursor = null);

/// <summary>In whose name the caller writes under one target, where it is not as a person, or why they cannot write there.</summary>
public sealed record CommentTargetVoice(string Kind, Guid TargetId, string? Voice, string? Refusal);

/// <summary>
/// The caller's own answer about the comments on one bill (design 8.23.6, 8.23.10, never cached): which comments they may
/// delete, which they wrote (no "Sutinku" there), which they agreed with, whether they ever agreed anywhere (the first
/// "Sutinku" asks), their voice where it is not a person's, whether a writing restriction holds, whether this is an MP's own
/// account (no part as a person), and the ceiling per text with what is left per target when it is switched on.
/// </summary>
public sealed record MyComments(
    bool AgreedBefore,
    bool Restricted,
    bool MpAccount,
    IReadOnlyList<Guid> Deletable,
    IReadOnlyList<Guid> Written,
    IReadOnlyList<Guid> Agreed,
    IReadOnlyList<CommentTargetVoice> Voices,
    int PerTargetLimit,
    IReadOnlyDictionary<Guid, int> WrittenPerTarget)
{
    /// <summary>The caller's own comments they may edit now (design 8.23.3, owner 2026-11-17: until someone says "Sutinku").</summary>
    public IReadOnlyList<Guid> Editable { get; init; } = [];

    /// <summary>
    /// The comments that are the caller's own in any voice (written by them, in the name of an organisation they represent or of
    /// the MP they speak for): no "Pranešti" there (owner, 2026-11-17).
    /// </summary>
    public IReadOnlyList<Guid> Own { get; init; } = [];

    /// <summary>
    /// The caller's own comments a moderator hid, with the moderator's statement (owner, 2026-11-17: the author sees them in
    /// place, greyed); only in this never-cached answer.
    /// </summary>
    public IReadOnlyList<MyHiddenComment> Hidden { get; init; } = [];
}

/// <summary>One's own hidden comment: under which target, its text and time, and the moderator's statement when there is one.</summary>
public sealed record MyHiddenComment(Guid Id, string Kind, Guid TargetId, string Text, DateTimeOffset CreatedAt, string? Statement);

public sealed record CommentCreated(Guid Id);

/// <summary>A shown comment's place (design 8.23.6): its target's kind and id, its bill, and its position among the target's shown comments (1-based).</summary>
public sealed record CommentPlace(Guid CommentId, string Kind, Guid TargetId, string BillNumber, int Position);

/// <summary>What a reported or hidden comment is about, for the moderators (design 8.23.8): the target's text and its own name.</summary>
public sealed record CommentContext(string Kind, Guid TargetId, string? Text, string? AuthorName, string? OrganisationName, long? SeimasPersonId, bool Shown);

/// <summary>A comment as a moderator sees it: in the list (reported), on the bill's page, or among the hidden ones. Never who reported.</summary>
public sealed record QueueComment(
    Guid Id, BillFacts Bill, CommentAuthor Author, string Text, DateTimeOffset CreatedAt, string Status, IReadOnlyList<QueueReport> Reports,
    CommentContext Target, DateTimeOffset? HiddenAt, string? HiddenBy, string? HideStatement);

public sealed record CommentModerationItems(IReadOnlyList<QueueComment> Reported, IReadOnlyList<QueueComment> Hidden)
{
    /// <summary>How many reported comments wait in all; the list holds at most a hundred.</summary>
    public int ReportedTotal { get; init; }

    public int HiddenTotal { get; init; }
}

/// <summary>How many comments the moderators' lists hold (design 8.3, the tabs' counts), each by its own query with its list's filter.</summary>
public sealed record CommentModerationCounts(int Reported, int Hidden);

/// <summary>
/// Comments (design 8.23): read in public, written, deleted and agreed with by members. Rules are checked here, where the data
/// is written (design 6.8): the switches, the bill open, the target shown, the voice, the writing restriction, pre-screening,
/// the length and the ceiling per text. A comment never creates a bill's record and is never counted.
/// </summary>
public interface IComments
{
    /// <summary>
    /// The shown comments of a target, oldest first, <paramref name="limit"/> of them from <paramref name="cursor"/> on (the first
    /// page at least 10, design 8.23.6); not found when the target is not shown.
    /// </summary>
    Task<Result<CommentThread>> ThreadAsync(string kind, Guid targetId, string? cursor, int? limit, CancellationToken cancellationToken);

    /// <summary>
    /// The first page of each of several targets' threads (as <see cref="ThreadAsync"/> with no cursor and no limit), in the
    /// order asked: a bill's page reads all its shown arguments' threads in one call. A target that is not shown is left out.
    /// </summary>
    Task<IReadOnlyList<CommentThread>> ThreadsAsync(string kind, IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken);

    /// <summary>
    /// Where a shown comment stands, for a link to it (design 8.23.6): its target, its bill and its place among the target's
    /// shown comments, oldest first (1-based). <c>comment.not-found</c> for a hidden, deleted, merged-away or unknown comment and
    /// for one whose target or bill's record is not shown: the same answer, so nothing tells a hidden comment from none.
    /// </summary>
    Task<Result<CommentPlace>> PlaceAsync(Guid commentId, CancellationToken cancellationToken);

    /// <summary>How many comments each target shows, for the public answers (design 8.23.11); a target without any is missing.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsAsync(string kind, IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken);

    Task<Result<MyComments>> MineAsync(Guid userId, string billNumber, CommentSpeaker? speaker, CancellationToken cancellationToken);

    Task<Result<CommentCreated>> WriteAsync(Guid userId, string kind, Guid targetId, string text, CommentSpeaker? speaker, CancellationToken cancellationToken);

    Task<Result<Unit>> DeleteAsync(Guid userId, Guid commentId, CommentSpeaker? speaker, CancellationToken cancellationToken);

    /// <summary>
    /// The author edits their comment while nobody has said "Sutinku" (design 8.23.3, owner 2026-11-17), by the rules of writing
    /// it: <c>comment.reacted</c> after a "Sutinku"; <c>comment.edit-author-only</c> for someone else's.
    /// </summary>
    Task<Result<Unit>> EditAsync(Guid userId, Guid commentId, string text, CommentSpeaker? speaker, CancellationToken cancellationToken);

    Task<Result<Unit>> AgreeAsync(Guid userId, Guid commentId, CancellationToken cancellationToken);

    Task<Result<Unit>> RemoveAgreementAsync(Guid userId, Guid commentId, CancellationToken cancellationToken);
}

/// <summary>
/// Moderation of comments after the fact (design 8.23.8): the reported ones and the hidden ones, the bill page's answer, hiding
/// with a statement (sent to the author, the organisation's representatives or the MP's account), restoring, "Palikti", and
/// restricting the writer. Each recorded in <c>moderation_action</c> (target <c>comment</c>) and audited without the text.
/// </summary>
public interface ICommentModeration
{
    Task<CommentModerationItems> ItemsAsync(CancellationToken cancellationToken);

    /// <summary>How many comments are reported and hidden (design 8.3), each by its own query, not by reading the lists.</summary>
    Task<CommentModerationCounts> CountsAsync(CancellationToken cancellationToken);

    /// <summary>The bill's published comments with their open reports, for the moderators' controls on the bill's page.</summary>
    Task<Result<IReadOnlyList<QueueComment>>> OnBillAsync(string billNumber, CancellationToken cancellationToken);

    Task<Result<Unit>> HideAsync(Guid commentId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    Task<Result<Unit>> RestoreAsync(Guid commentId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    Task<Result<Unit>> KeepAsync(Guid commentId, string? note, StaffActor moderator, CancellationToken cancellationToken);

    Task<Result<AuthorView>> WriterAsync(Guid commentId, CancellationToken cancellationToken);

    Task<Result<Guid>> RestrictWriterAsync(Guid commentId, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);
}
