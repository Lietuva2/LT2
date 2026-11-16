using Lt2.Api.Contracts.Comments;

namespace Lt2.Web.Client.Comments;

/// <summary>
/// The caller's own answer about a bill's comments (design 8.23.6), asked once per page in the browser and shared by every
/// thread on it, as <c>MyBillState</c> is for sides; and the moderators' answer for the bill. Never asked while the server
/// prerenders, so the page's HTML is the same for every reader. <see cref="Changed"/> tells the other threads after a member's
/// action (the first "Sutinku" anywhere changes how every "Sutinku" asks).
/// </summary>
public sealed class CommentState(ICommentsApi comments, ICommentModerationApi moderation)
{
    private readonly Dictionary<string, Task<MyCommentsResponse?>> mine = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<IReadOnlyList<QueueCommentResponse>?>> moderated = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after the caller wrote, deleted, agreed or took an agreement back, with the bill's number.</summary>
    public event Action<string>? Changed;

    public Task<MyCommentsResponse?> MineAsync(string number) =>
        Cached(mine, number, async () => (await comments.GetMineAsync(number)).Value);

    /// <summary>The moderators' answer for the bill; null for anyone else (the server refuses it).</summary>
    public Task<IReadOnlyList<QueueCommentResponse>?> ModerationAsync(string number) =>
        Cached(moderated, number, async () => (await moderation.GetOnBillAsync(number)).Value);

    /// <summary>Forgets the caller's answers about the bill and tells the other threads.</summary>
    public void Forget(string number)
    {
        mine.Remove(number);
        moderated.Remove(number);
        Changed?.Invoke(number);
    }

    private static Task<T?> Cached<T>(Dictionary<string, Task<T?>> cache, string key, Func<Task<T?>> fetch)
        where T : class
    {
        if (cache.TryGetValue(key, out var task) && !task.IsFaulted && !(task.IsCompletedSuccessfully && task.Result is null))
        {
            return task;
        }

        return cache[key] = fetch();
    }
}
