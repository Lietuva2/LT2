using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Statistics;

/// <summary>Stores what the pure rules compute: faction positions per vote, and each MP's counts per term.</summary>
internal interface IParliamentStatistics
{
    /// <summary>Faction positions of one vote from its per-MP rows, for the labels that resolve to a faction body;
    /// tracked in the caller's unit of work.</summary>
    Task ApplyFactionPositionsAsync(long voteId, IEnumerable<(string? Label, BallotChoice Choice)> rows, IReadOnlyDictionary<string, Guid> factionByLabel, CancellationToken cancellationToken);

    /// <summary>Every MP's counts for the term, from the stored ballots and faction positions; saved at once.</summary>
    Task RecomputeAsync(int termId, CancellationToken cancellationToken);
}

internal sealed class ParliamentStatistics(ParliamentDb db, TimeProvider clock) : IParliamentStatistics
{
    public async Task ApplyFactionPositionsAsync(
        long voteId,
        IEnumerable<(string? Label, BallotChoice Choice)> rows,
        IReadOnlyDictionary<string, Guid> factionByLabel,
        CancellationToken cancellationToken)
    {
        var tallies = FactionRule.Tally(rows, factionByLabel.Keys.ToHashSet(StringComparer.Ordinal));
        var existing = await db.FactionPositions.Where(p => p.VoteId == voteId).ToDictionaryAsync(p => p.FactionLabel, StringComparer.Ordinal, cancellationToken);
        foreach (var tally in tallies)
        {
            if (!existing.Remove(tally.Label, out var row))
            {
                row = new FactionPositionRow { VoteId = voteId, FactionLabel = tally.Label };
                db.FactionPositions.Add(row);
            }

            row.FactionId = factionByLabel.TryGetValue(tally.Label, out var faction) ? faction : null;
            row.Position = tally.Position;
            row.For = tally.For;
            row.Against = tally.Against;
            row.Abstain = tally.Abstain;
            row.DidNotVote = tally.DidNotVote;
        }

        db.FactionPositions.RemoveRange(existing.Values);
    }

    public async Task RecomputeAsync(int termId, CancellationToken cancellationToken)
    {
        // Recorded votes of the term: votes with per-MP results (consensus votes have none).
        var votes = await (from v in db.Votes.AsNoTracking()
                           join i in db.AgendaItems on v.AgendaItemId equals i.Id
                           join s in db.Sittings on i.SittingId equals s.Id
                           join ses in db.Sessions on s.SessionId equals ses.Id
                           where ses.TermId == termId && v.HasResult && !v.IsConsensus
                           select new { v.Id, v.HeldAt }).ToListAsync(cancellationToken);
        var voteDays = votes.ToDictionary(v => v.Id, v => VilniusTime.DateOf(v.HeldAt));
        var voteIds = voteDays.Keys.ToList();
        var positions = (await db.FactionPositions.AsNoTracking().Where(p => voteIds.Contains(p.VoteId))
                .Select(p => new { p.VoteId, p.FactionLabel, p.Position }).ToListAsync(cancellationToken))
            .ToDictionary(p => (p.VoteId, p.FactionLabel), p => p.Position);

        var mandates = await db.Mandates.AsNoTracking().Where(m => m.TermId == termId).ToListAsync(cancellationToken);
        var stats = await db.MpStats.Where(s => s.TermId == termId).ToDictionaryAsync(s => s.PersonId, cancellationToken);

        // Every ballot of the term's recorded votes at once, grouped by person.
        var ballotsByPerson = (await (from b in db.Ballots.AsNoTracking()
                                      join v in db.Votes on b.VoteId equals v.Id
                                      join i in db.AgendaItems on v.AgendaItemId equals i.Id
                                      join s in db.Sittings on i.SittingId equals s.Id
                                      join ses in db.Sessions on s.SessionId equals ses.Id
                                      where ses.TermId == termId && v.HasResult && !v.IsConsensus
                                      select new { b.PersonId, b.VoteId, b.FactionLabel, b.Choice }).ToListAsync(cancellationToken))
            .ToLookup(b => b.PersonId);
        var now = clock.GetUtcNow();
        foreach (var mandate in mandates)
        {
            var ballots = ballotsByPerson[mandate.PersonId].ToDictionary(b => b.VoteId);

            var facts = voteDays
                .Where(v => v.Value >= mandate.From && (mandate.To is null || v.Value <= mandate.To))
                .Select(v =>
                {
                    if (!ballots.TryGetValue(v.Key, out var ballot))
                    {
                        return new MpVoteFact(BallotChoice.DidNotVote, null);
                    }

                    FactionPosition? position = string.IsNullOrWhiteSpace(ballot.FactionLabel) ? null
                        : positions.TryGetValue((v.Key, ballot.FactionLabel.Trim()), out var p) ? p : null;
                    return new MpVoteFact(ballot.Choice, position);
                });
            var computed = MpStatisticsRule.Compute(facts);

            if (!stats.Remove(mandate.PersonId, out var row))
            {
                row = new MpStat { PersonId = mandate.PersonId, TermId = termId };
                db.MpStats.Add(row);
            }

            if (row.VotesHeld != computed.VotesHeld || row.VotesTakenPart != computed.VotesTakenPart
                || row.VotesWithFactionPosition != computed.VotesWithFactionPosition || row.VotedWithFaction != computed.VotedWithFaction
                || row.FreeVotes != computed.FreeVotes || db.Entry(row).State == EntityState.Added)
            {
                row.VotesHeld = computed.VotesHeld;
                row.VotesTakenPart = computed.VotesTakenPart;
                row.VotesWithFactionPosition = computed.VotesWithFactionPosition;
                row.VotedWithFaction = computed.VotedWithFaction;
                row.FreeVotes = computed.FreeVotes;
                row.ComputedAt = now;
            }
        }

        db.MpStats.RemoveRange(stats.Values);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
