using Lt2.SharedKernel;

namespace Lt2.Appeals.Domain;

/// <summary>
/// The moderators' list "Projektai be aprašymo" (design 8.3, owner 2026-11-25) as pure functions. An active bill is on it
/// when it has no description, or an AI summary nobody has read (8.13), and people already pay attention to it: at least
/// one participant, published argument or follower. A bill nobody touched is not on it, whatever its planned date. The
/// order is the attention (participants, published arguments and followers, a plain sum), the most first; then the soonest
/// planned date, bills without one last; then the number.
/// </summary>
internal static class UndescribedRules
{
    /// <summary>Whether the bill still wants a description a person wrote or read: none, or an <c>ai</c> one.</summary>
    public static bool WantsDescription(string description, string? source) =>
        description.Length == 0 || source == DescriptionSources.Ai;

    public static int Attention(int participants, int arguments, int followers) => participants + arguments + followers;

    /// <summary>The bills on the list, in its order: those with any attention, the most first.</summary>
    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> items, Func<T, int> participants, Func<T, int> arguments, Func<T, int> followers, Func<T, DateOnly?> planned, Func<T, string> number)
    {
        int AttentionOf(T i) => Attention(participants(i), arguments(i), followers(i));
        return items
            .Where(i => AttentionOf(i) > 0)
            .OrderByDescending(AttentionOf)
            .ThenBy(i => planned(i) ?? DateOnly.MaxValue)
            .ThenBy(number, StringComparer.Ordinal)
            .ToList();
    }
}
