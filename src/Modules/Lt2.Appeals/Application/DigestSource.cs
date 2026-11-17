using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IDigestSource"/>: every open bill record with the numbers a summary would carry, and the published arguments a
/// summary chooses from (design 8.4, 8.19.2); no author, no person.
/// </summary>
internal sealed class DigestSource(
    IDbContextFactory<AppealsDb> contexts, AppealReads reads, IBillCatalogue bills, IPlannedAgenda agenda, IPartySupporters supporters, ISettings settings) : IDigestSource
{
    public async Task<IReadOnlyList<MpStance>> PublishedResponsesAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(billNumbers);
        if (billNumbers.Count == 0)
        {
            return [];
        }

        var numbers = billNumbers.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.MpResponses.AsNoTracking()
                .Where(r => numbers.Contains(r.BillNumber) && r.Status == ResponseStatuses.Published)
                .Select(r => new { r.SeimasPersonId, r.BillNumber, r.Stance, r.PublishedAt, r.EditedAt })
                .ToListAsync(cancellationToken))
            .Select(r => new MpStance(r.SeimasPersonId, r.BillNumber, r.Stance, r.EditedAt ?? r.PublishedAt))
            .ToList();
    }

    /// <summary>Sets <c>first_summarised_at</c> once on each record, in the sender's transaction (design 8.12.2, 8.19.1).</summary>
    public async Task MarkSummarisedAsync(IReadOnlyCollection<Guid> recordIds, DateTimeOffset at, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        if (recordIds.Count == 0)
        {
            return;
        }

        if (transaction is not Npgsql.NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Marking records summarised needs a PostgreSQL transaction.", nameof(transaction));
        }

        await using var command = new Npgsql.NpgsqlCommand(
            "UPDATE appeals.bill_record SET first_summarised_at = @at WHERE id = ANY(@ids) AND first_summarised_at IS NULL", connection, npgsql);
        command.Parameters.AddWithValue("at", at.ToUniversalTime());
        command.Parameters.AddWithValue("ids", recordIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DigestCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeals = await db.BillRecords.AsNoTracking().Where(a => a.Status == BillRecordStatuses.Open).ToListAsync(cancellationToken);
        if (appeals.Count == 0)
        {
            return [];
        }

        var ids = appeals.Select(a => a.Id).ToList();
        var counts = await reads.CountsAsync(db, ids, cancellationToken);
        var thresholdSettings = await reads.ThresholdSettingsAsync(cancellationToken);
        var titles = await bills.FindManyAsync(appeals.Select(a => a.BillNumber).ToList(), cancellationToken);
        var planned = await agenda.ForBillsAsync(appeals.Select(a => a.BillNumber).ToList(), cancellationToken);
        var surges = await reads.SurgesAsync(db, ids, cancellationToken);

        var result = new List<DigestCandidate>();
        foreach (var record in appeals)
        {
            var c = counts[record.Id];
            var threshold = AppealReads.Threshold(record, c, thresholdSettings);
            var surge = surges.GetValueOrDefault(record.Id);
            result.Add(new DigestCandidate(
                record.Id,
                record.BillNumber,
                titles.GetValueOrDefault(record.BillNumber)?.Title ?? record.BillNumber,
                c.For,
                c.ForOlderThan30Days,
                c.Against,
                c.AgainstOlderThan30Days,
                threshold.Value,
                threshold.Reached,
                record.Sending == "held",
                surge?.SharePercent,
                surge?.WindowHours,
                planned.GetValueOrDefault(record.BillNumber)));
        }

        return result;
    }

    public async Task<IReadOnlyList<DigestPoolReason>> ReasonPoolAsync(IReadOnlyCollection<Guid> recordIds, DateTimeOffset publishedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        if (recordIds.Count == 0)
        {
            return [];
        }

        var ids = recordIds.ToList();
        var by = publishedBy.ToUniversalTime();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var published = await db.Reasons.AsNoTracking()
            .Where(r => ids.Contains(r.RecordId) && r.Status == ReasonStatuses.Published && r.FirstPublicAt != null && r.FirstPublicAt <= by)
            .Select(r => new { r.Id, r.RecordId, r.Side, r.Text, PublishedAt = r.FirstPublicAt!.Value })
            .ToListAsync(cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, published.Select(r => r.Id).ToList(), cancellationToken);
        return published
            .Select(r => new DigestPoolReason(r.Id, r.RecordId, r.Side, r.Text, endorsements.GetValueOrDefault(r.Id), r.PublishedAt))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PartyNumbers>>> PartyNumbersAsync(IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken)
    {
        var byParty = await supporters.SupportersByPartyAsync(cancellationToken);
        var result = new Dictionary<Guid, IReadOnlyList<PartyNumbers>>();
        if (byParty.Count == 0 || recordIds.Count == 0)
        {
            return result;
        }

        var ids = recordIds.ToList();
        var minimum = await settings.GetAsync(SettingKeys.DigestPartyGroupMinimum, cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var totals = await reads.CountsAsync(db, ids, cancellationToken);

        // One query per party for all the records, not one per record and party.
        var sidesByParty = new Dictionary<string, List<(Guid RecordId, string Side, int Count)>>();
        foreach (var (party, users) in byParty)
        {
            var userList = users.ToList();
            sidesByParty[party] = (await db.Positions.AsNoTracking()
                    .Where(p => ids.Contains(p.RecordId) && userList.Contains(p.UserId))
                    .GroupBy(p => new { p.RecordId, p.Side })
                    .Select(g => new { g.Key.RecordId, g.Key.Side, Count = g.Count() })
                    .ToListAsync(cancellationToken))
                .Select(r => (r.RecordId, r.Side, r.Count))
                .ToList();
        }

        foreach (var recordId in ids)
        {
            var numbers = new List<PartyNumbers>();
            foreach (var (party, _) in byParty)
            {
                var sides = sidesByParty[party].Where(s => s.RecordId == recordId).ToList();
                var forCount = sides.FirstOrDefault(s => s.Side == Sides.For).Count;
                var againstCount = sides.FirstOrDefault(s => s.Side == Sides.Against).Count;

                // Each side's group is published only from Digest:PartyGroupMinimum people (design 6.3, 8.4), and only when
                // the rest of that side is not a smaller group that subtraction from the total shown beside it would reveal.
                var published = new PartyNumbers(
                    party,
                    GroupRules.PublishableBeside(forCount, totals[recordId].For, minimum),
                    GroupRules.PublishableBeside(againstCount, totals[recordId].Against, minimum));
                if (published.For is not null || published.Against is not null)
                {
                    numbers.Add(published);
                }
            }

            result[recordId] = numbers;
        }

        return result;
    }
}
