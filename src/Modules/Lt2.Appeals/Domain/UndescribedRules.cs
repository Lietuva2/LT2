using Lt2.SharedKernel;

namespace Lt2.Appeals.Domain;

/// <summary>
/// The moderators' list "Projektai be aprašymo" (design 8.3, owner 2026-11-25) as pure functions. An active bill is on it
/// when it has no description, or an AI summary nobody has read (8.13), and people already pay attention to it: some
/// activity (<see cref="BillListRules.Activity"/>: participants, published arguments and followers, a plain sum). A bill
/// nobody touched is not on it, whatever its planned date. The order is the activity order of every list of bills
/// (<see cref="BillListRules.Order"/>): the most first; then the soonest planned date, bills without one last; then the number.
/// </summary>
internal static class UndescribedRules
{
    /// <summary>Whether the bill still wants a description a person wrote or read: none, or an <c>ai</c> one.</summary>
    public static bool WantsDescription(string description, string? source) =>
        description.Length == 0 || source == DescriptionSources.Ai;

    /// <summary>Whether a bill that wants a description is on the list: anyone pays attention to it.</summary>
    public static bool IsListed(int participants, int arguments, int followers) => BillListRules.Activity(participants, arguments, followers) > 0;

    /// <summary>The listed bills in the list's order: by activity, as <c>/projektai</c> orders by default.</summary>
    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> items, Func<T, int> participants, Func<T, int> arguments, Func<T, int> followers, Func<T, DateOnly?> planned, Func<T, string> number) =>
        BillListRules.Order(items, BillSorts.Activity, participants, arguments, followers, planned, number);
}
