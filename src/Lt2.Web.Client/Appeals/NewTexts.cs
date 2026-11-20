using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The texts on one bill new to the person (design 8.11.8), as the visit's write answered them: decided by the server (the
/// rule, the person's own texts left out), so every island asks the same question here and marks the same ones.
/// </summary>
public sealed class NewTexts
{
    private readonly HashSet<Guid> texts;
    private readonly HashSet<Guid> comments;
    private readonly HashSet<Guid> discussions;

    public NewTexts(BillSeenResponse answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        Since = answer.Since;
        texts = [.. answer.Reasons, .. answer.Statements, .. answer.Responses];
        comments = answer.Comments.Select(c => c.Id).ToHashSet();
        discussions = answer.Comments.Select(c => c.TargetId).ToHashSet();
        CommentsByTarget = answer.Comments.GroupBy(c => c.TargetId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)g.Select(c => c.Id).ToList());
        Reasons = answer.Reasons.Concat(answer.Comments.Where(c => c.TargetKind == CommentKinds.Reason).Select(c => c.TargetId)).Distinct().ToList();
    }

    /// <summary>
    /// The arguments "Rodyti tik naujus" keeps: new ones and those whose discussion holds a new comment. The page reads those its
    /// pages have not brought yet (owner, 2026-11-19: the arguments come a page at a time).
    /// </summary>
    public IReadOnlyList<Guid> Reasons { get; }

    /// <summary>The moment the person last opened the page before this visit.</summary>
    public DateTimeOffset? Since { get; }

    /// <summary>Anything new at all: only then is "Rodyti tik naujus" offered.</summary>
    public bool Any => texts.Count > 0 || comments.Count > 0;

    /// <summary>The new comments under each text.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> CommentsByTarget { get; }

    /// <summary>An argument, a statement, a response or a comment written since the last visit, not the person's own.</summary>
    public bool IsNew(Guid id) => texts.Contains(id) || comments.Contains(id);

    /// <summary>Whether the discussion under a text holds a new comment.</summary>
    public bool HasNewComments(Guid targetId) => discussions.Contains(targetId);

    /// <summary>Under "Rodyti tik naujus" a text stands when it is new itself or its discussion holds a new comment.</summary>
    public bool Keeps(Guid id) => texts.Contains(id) || discussions.Contains(id);
}
