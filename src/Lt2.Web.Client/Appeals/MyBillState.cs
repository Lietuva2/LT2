using Lt2.Api.Contracts.Appeals;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The caller's own answers about one bill, asked once per scope and shared by the bill page's islands (taking a side,
/// the arguments, the date flag, the moderators' panel): while the server prerenders the page, one request serves them
/// all; in the browser the scope lives as long as the page. A failed answer is not kept, so the next island asks again.
/// After an action on the bill the island that acted calls <see cref="Announce"/>, and every other island of the page reads
/// what it shows again (<see cref="Changed"/>), in place of reloading the page; the bill's public answer read then
/// (<see cref="RecordAsync"/>) is one request for all of them.
/// </summary>
public sealed class MyBillState(IAppealsApi appeals, IModerationApi moderation)
{
    private readonly Dictionary<string, Task<MyBillRecordResponse?>> mine = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<BillRecordResponse?>> records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ModerationBillResponse?>> moderated = new(StringComparer.OrdinalIgnoreCase);
    private Task<MyRestrictionResponse?>? restriction;

    /// <summary>
    /// Something on the bill changed by the caller's own action (a side taken, changed or withdrawn; a moderator's action):
    /// the bill's number and the island that acted, which reads its own part itself.
    /// </summary>
    public event Action<BillChange>? Changed;

    /// <summary>
    /// Forgets every answer about the bill and tells every other island of the page to read its part again; <paramref name="byModerator"/>:
    /// a moderator's action rather than the person's own side.
    /// </summary>
    public void Announce(string number, object from, bool byModerator = false)
    {
        Forget(number);
        moderated.Remove(number);
        restriction = null;
        Changed?.Invoke(new BillChange(number, from, byModerator));
    }

    /// <summary>The person's own side, arguments and marks on the bill (never cached anywhere else).</summary>
    public Task<MyBillRecordResponse?> MineAsync(string number) =>
        Cached(mine, number, async () => (await appeals.GetMineAsync(number)).Value);

    /// <summary>
    /// The bill's public answer (the counts, the threshold, the arguments), read in the browser after a change; the page's
    /// own copy is the server's until then.
    /// </summary>
    public Task<BillRecordResponse?> RecordAsync(string number) =>
        Cached(records, number, async () => (await appeals.GetRecordAsync(number)).Value);

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

    /// <summary>Forgets the person's answer and the bill's public answer after they changed something on it.</summary>
    public void Forget(string number)
    {
        mine.Remove(number);
        records.Remove(number);
    }

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

/// <summary>
/// A change on a bill (<see cref="MyBillState.Changed"/>): an island other than <c>From</c> showing the bill reads again.
/// <c>ByModerator</c>: a moderator's action (the moderators' panels and the organisations' statements read again); otherwise
/// the person's own side (the follow button and the date flag read again).
/// </summary>
public sealed record BillChange(string Number, object From, bool ByModerator)
{
    /// <summary>Whether the island <paramref name="island"/>, showing bill <paramref name="number"/>, should read its part again.</summary>
    public bool Concerns(string number, object island) =>
        !ReferenceEquals(From, island) && string.Equals(Number, number, StringComparison.OrdinalIgnoreCase);
}
