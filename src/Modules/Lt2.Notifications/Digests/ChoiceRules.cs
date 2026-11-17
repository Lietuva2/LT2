using Lt2.SharedKernel;

namespace Lt2.Notifications.Digests;

/// <summary>An argument chosen for a summary, as the rules see it: its place on its side, how it came in, whether it left.</summary>
internal sealed record ChosenReason(Guid ReasonId, Guid RecordId, string Side, int Place, bool Replacement = false, bool Left = false);

/// <summary>What one look at a prepared summary changes in its chosen arguments: those that leave and those that come in.</summary>
internal sealed record ChoiceChange(IReadOnlyList<Guid> Leaving, IReadOnlyList<ChosenReason> Coming)
{
    public bool IsEmpty => Leaving.Count == 0 && Coming.Count == 0;
}

/// <summary>
/// Which arguments a summary holds (design 8.4, 8.19.2, owner 2026-11-17), as pure functions. The pool is the arguments published
/// and not hidden that were public when the summary was prepared; whether a moderator has seen one does not matter.
/// </summary>
internal static class ChoiceRules
{
    /// <summary>Most "Man svarbu" first, then the earliest public, then by id, so the order never depends on reading.</summary>
    public static IEnumerable<DigestPoolReason> ByMarks(IEnumerable<DigestPoolReason> pool) =>
        pool.OrderByDescending(r => r.Endorsements).ThenBy(r => r.PublishedAt).ThenBy(r => r.Id);

    /// <summary>At preparation: per record and side, the <paramref name="perSide"/> most endorsed, in that order.</summary>
    public static IReadOnlyList<ChosenReason> Choose(IEnumerable<DigestPoolReason> pool, int perSide) =>
        pool.GroupBy(r => (r.RecordId, r.Side))
            .SelectMany(g => ByMarks(g).Take(perSide).Select((r, i) => new ChosenReason(r.Id, r.RecordId, r.Side, i + 1)))
            .ToList();

    /// <summary>
    /// Hiding brings the next one in: each chosen argument no longer in the pool (hidden, merged, withdrawn) leaves, and for each
    /// one leaving its side takes the next by "Man svarbu" now from the pool, never one chosen before, placed after the others
    /// and marked as a replacement. A side whose pool has run out simply holds fewer. The rest keep their places.
    /// </summary>
    public static ChoiceChange Replace(IReadOnlyList<ChosenReason> chosen, IReadOnlyList<DigestPoolReason> pool)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(pool);
        var inPool = pool.Select(r => r.Id).ToHashSet();
        var everChosen = chosen.Select(c => c.ReasonId).ToHashSet();
        var leaving = new List<Guid>();
        var coming = new List<ChosenReason>();
        foreach (var side in chosen.GroupBy(c => (c.RecordId, c.Side)))
        {
            var gone = side.Where(c => !c.Left && !inPool.Contains(c.ReasonId)).ToList();
            if (gone.Count == 0)
            {
                continue;
            }

            leaving.AddRange(gone.Select(c => c.ReasonId));
            var place = side.Max(c => c.Place);
            foreach (var next in ByMarks(pool.Where(r => r.RecordId == side.Key.RecordId && r.Side == side.Key.Side && !everChosen.Contains(r.Id))).Take(gone.Count))
            {
                coming.Add(new ChosenReason(next.Id, next.RecordId, next.Side, ++place, Replacement: true));
            }
        }

        return new ChoiceChange(leaving, coming);
    }
}
