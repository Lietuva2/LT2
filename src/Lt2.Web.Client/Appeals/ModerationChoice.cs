namespace Lt2.Web.Client.Appeals;

/// <summary>
/// One moderator's action on an item, as <see cref="ModerationMenu"/> offers it (design 8.3, "The screen", owner 2026-11-17).
/// </summary>
/// <param name="Action">The action's name (<c>keep</c>, <c>hide</c>…), handed back when it is chosen.</param>
/// <param name="ElementId">The button's id, unique on the page (<c>action-hide-{id}</c>, <c>org-action-block-{id}</c>…).</param>
/// <param name="Label">The button's words.</param>
/// <param name="Group">The small heading it stands under in the menu (a bill's record: "Aprašymas", "Seime", "Suvestinės");
/// null for none. Headings show only when the menu holds two groups or more.</param>
/// <param name="Apart">Set apart at the end of the menu, after a rule ("Paslėpti", "Užblokuoti").</param>
/// <param name="Disabled">Offered but not possible now (a profile's text that was replaced cannot be restored).</param>
public sealed record ModerationChoice(string Action, string ElementId, string Label, string? Group = null, bool Apart = false, bool Disabled = false);
