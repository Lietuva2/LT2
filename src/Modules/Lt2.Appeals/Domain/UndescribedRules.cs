using Lt2.SharedKernel;

namespace Lt2.Appeals.Domain;

/// <summary>
/// The moderators' list "Projektai be aprašymo" (design 8.3, owner 2026-11-25) and the members' "Prašau aprašymo" (design
/// 8.13.2, owner 2026-11-27) as pure functions. A bill is on the list when it has no description, or an AI summary nobody
/// has read (8.13), and either members asked for one (any bill, a finished one too) or, while it is active, people already
/// pay attention to it: some activity (<see cref="BillListRules.Activity"/>: participants, published arguments and
/// followers, a plain sum). An active bill nobody touched or asked about is not on it, whatever its planned date. The order:
/// the most asks first; then the activity order of every list of bills (<see cref="BillListRules.Order"/>): the most
/// activity first, then the soonest planned date, bills without one last, then the number.
/// </summary>
internal static class UndescribedRules
{
    /// <summary>Whether the bill still wants a description a person wrote or read: none, or an <c>ai</c> one.</summary>
    public static bool WantsDescription(string description, string? source) =>
        description.Length == 0 || source == DescriptionSources.Ai;

    /// <summary>
    /// Whether a bill that wants a description is on the list: members asked for one, or it is active and anyone pays
    /// attention to it.
    /// </summary>
    public static bool IsListed(int asks, bool active, int participants, int arguments, int followers) =>
        asks > 0 || (active && BillListRules.Activity(participants, arguments, followers) > 0);

    /// <summary>
    /// Why a member may not ask for the bill's description now, or null: the bill shows no description (none at all, not even
    /// an AI summary) and its record, if it has one, is not hidden. Open and closed bills alike (owner, 2026-11-27).
    /// </summary>
    public static string? CheckAsk(string? recordStatus, string? description) =>
        recordStatus == BillRecordStatuses.Rejected || description is { Length: > 0 } ? "description-ask.not-wanted" : null;

    /// <summary>
    /// The listed bills in the list's order: the most asks first, then by activity, as <c>/projektai</c> orders by default.
    /// </summary>
    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> items, Func<T, int> asks, Func<T, int> participants, Func<T, int> arguments, Func<T, int> followers, Func<T, DateOnly?> planned,
        Func<T, string> number) =>
        BillListRules.Order(items, BillSorts.Activity, participants, arguments, followers, planned, number)
            .OrderByDescending(asks) // stable: equal asks keep the activity order
            .ToList();
}
