namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The moderators' tabs (design 8.3) cascade this to their lists: an action on a list's item that succeeded calls
/// <see cref="ChangedAsync"/>, and the page reads its counts again, so a tab's count goes down without a reload. Elsewhere
/// (a bill's "Moderavimas" panel) nothing cascades it and the components skip it.
/// </summary>
public sealed class QueueChanges(Func<Task> changed)
{
    public Task ChangedAsync() => changed();
}
