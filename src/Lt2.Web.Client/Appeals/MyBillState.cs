using Lt2.Api.Contracts.Appeals;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The caller's own answers about one bill, asked once per scope and shared by the bill page's islands (taking a side,
/// the arguments, the date flag, the moderators' panel): while the server prerenders the page, one request serves them
/// all; in the browser the scope lives as long as the page. A failed answer is not kept, so the next island asks again.
/// </summary>
public sealed class MyBillState(IAppealsApi appeals, IModerationApi moderation)
{
    private readonly Dictionary<string, Task<MyAppealResponse?>> mine = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ModerationBillResponse?>> moderated = new(StringComparer.OrdinalIgnoreCase);
    private Task<MyRestrictionResponse?>? restriction;

    /// <summary>The person's own side, arguments and marks on the bill (never cached anywhere else).</summary>
    public Task<MyAppealResponse?> MineAsync(string number) =>
        Cached(mine, number, async () => (await appeals.GetMineAsync(number)).Value);

    /// <summary>The person's own writing restriction (design 8.3).</summary>
    public Task<MyRestrictionResponse?> RestrictionAsync()
    {
        if (restriction is null || restriction.IsFaulted || (restriction.IsCompletedSuccessfully && restriction.Result is null))
        {
            restriction = Fetch(async () => (await appeals.GetMyRestrictionAsync()).Value);
        }

        return restriction;
    }

    /// <summary>The moderators' answer for the bill; null for anyone else (the server refuses it).</summary>
    public Task<ModerationBillResponse?> ModerationAsync(string number) =>
        Cached(moderated, number, async () => (await moderation.GetBillAsync(number)).Value);

    /// <summary>Keeps a moderators' answer another island brought from the server, so the browser need not ask again.</summary>
    public void Remember(string number, ModerationBillResponse answer) => moderated[number] = Task.FromResult<ModerationBillResponse?>(answer);

    /// <summary>Forgets the person's answer about a bill after they changed something on it.</summary>
    public void Forget(string number) => mine.Remove(number);

    private static Task<T?> Cached<T>(Dictionary<string, Task<T?>> cache, string key, Func<Task<T?>> fetch)
        where T : class
    {
        if (cache.TryGetValue(key, out var task) && !task.IsFaulted && !(task.IsCompletedSuccessfully && task.Result is null))
        {
            return task;
        }

        return cache[key] = Fetch(fetch);
    }

    private static Task<T?> Fetch<T>(Func<Task<T?>> fetch)
        where T : class => fetch();
}
