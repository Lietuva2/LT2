namespace Lt2.SharedKernel;

/// <summary>
/// An argument that got a comment from someone else (design 8.23.9): whose argument, which, and on which bill. Never the
/// comment's text, its writer, how many, or anything about "Sutinku".
/// </summary>
public sealed record CommentedArgument(Guid AuthorUserId, Guid ReasonId, string BillNumber);

/// <summary>One shown comment by someone else under an argument, with when it was written (design 8.23.9, 8.24): no text, no writer.</summary>
public sealed record CommentedArgumentAt(Guid AuthorUserId, Guid ReasonId, string BillNumber, DateTimeOffset At);

/// <summary>The arguments commented on, for the author's notice (design 8.23.9). Implemented by the Appeals module.</summary>
public interface ICommentFeed
{
    /// <summary>
    /// The published arguments (on a record that is not hidden) with at least one comment written in [<paramref name="since"/>,
    /// <paramref name="until"/>) that is shown now and was not written by the argument's own author.
    /// </summary>
    Task<IReadOnlyList<CommentedArgument>> CommentedArgumentsAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken);

    /// <summary>
    /// The same comments one by one with the moment each was written, in [<paramref name="since"/>, <paramref name="until"/>): so
    /// one read serves the windows of many runs.
    /// </summary>
    Task<IReadOnlyList<CommentedArgumentAt>> CommentTimesAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken);
}
