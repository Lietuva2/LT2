using Microsoft.Extensions.Localization;

namespace Lt2.Web.Client.Appeals;

/// <summary>Marker type for the Lithuanian texts of the record, organisation and staff components in AppealUi.resx.</summary>
public sealed class AppealUi;

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
