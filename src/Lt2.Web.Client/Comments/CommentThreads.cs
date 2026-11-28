using Lt2.Api.Contracts.Comments;

namespace Lt2.Web.Client.Comments;

/// <summary>
/// The discussions a list of texts shows (design 8.23.6): the threads shown at their first page come in one request
/// (<see cref="CommentRoutes.Threads"/>, at most <see cref="CommentLimits.ThreadsAtOnce"/> a request); one the address asks to
/// show further (<c>?komentarai=</c>, a comment link) is read by itself, as far as it asks. A target not shown has none.
/// </summary>
public static class CommentThreads
{
    public static async Task<Dictionary<Guid, CommentThreadResponse>> ReadAsync(
        ICommentsApi comments, string kind, IEnumerable<Guid> targetIds, Func<Guid, int?> asked)
    {
        var threads = new Dictionary<Guid, CommentThreadResponse>();
        var ids = targetIds.Distinct().ToList();
        foreach (var chunk in ids.Where(id => asked(id) is null).Chunk(CommentLimits.ThreadsAtOnce))
        {
            foreach (var thread in (await comments.GetThreadsAsync(kind, chunk)).Value ?? [])
            {
                threads[thread.TargetId] = thread;
            }
        }

        foreach (var id in ids)
        {
            if (asked(id) is { } limit && (await comments.GetThreadAsync(kind, id, limit)).Value is { } thread)
            {
                threads[id] = thread;
            }
        }

        return threads;
    }
}
