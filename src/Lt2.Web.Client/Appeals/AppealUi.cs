using Microsoft.Extensions.Localization;

namespace Lt2.Web.Client.Appeals;

/// <summary>Marker type for the Lithuanian texts of the record, organisation and staff components in AppealUi.resx.</summary>
public sealed class AppealUi;

/// <summary>
/// Pressing a side's button (design 8.7, owner 2026-11-18): the pressed one takes the side back, so an accidental double
/// press must not take a side and give it back at once. The buttons are disabled while a request runs; this is the rest.
/// </summary>
public static class SidePresses
{
    /// <summary>
    /// The second (or later) click of a double click, by the click count the browser reports (<c>MouseEvent.detail</c>), is
    /// ignored; a single click and a key press (count 0) act.
    /// </summary>
    public static bool Ignored(long clickCount) => clickCount > 1;
}

/// <summary>Words the moderators' screens share (design 8.3, 8.12.3, 8.14.14).</summary>
public static class ModerationWords
{
    /// <summary>The tag of a queued argument or statement: waiting, seen or not yet seen.</summary>
    public static string StatusKey(string status, bool seen) => status == "pending" ? "Mod.Status.pending" : seen ? "Mod.Seen" : "Mod.NotSeen";

    /// <summary>"Paslėpta {when} · paslėpė {who}" for a hidden item, or that it is not known when.</summary>
    public static string Hidden(IStringLocalizer<AppealUi> l, DateTimeOffset? at, string? by)
    {
        ArgumentNullException.ThrowIfNull(l);
        return at is { } when ? l.Format("Hidden.When", Display.DayTime(when), by ?? l["Hidden.Moderator"]) : l["Hidden.WhenUnknown"];
    }
}
