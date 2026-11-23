using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.Appeals;
using Lt2.Notifications;
using Lt2.Web.Client.Agenda;

namespace Lt2.Web.Services;

/// <summary>
/// The summary each list row names for a bill whose threshold is reached (design 8.12.2, owner 2026-11-21: "suvestinė
/// planuojama …" or "suvestinė išsiųsta …" in place of "riba pasiekta"), composed by the host: the records and their
/// thresholds are the Appeals module's, the summaries the Notifications module's. One read of the summaries per list,
/// whatever its length, and none when no row shown has reached its threshold; the row's text is <see cref="RowSummaryRules"/>.
/// </summary>
internal sealed class RowSummaries(IDigests digests)
{
    /// <summary>The summary of each given row that names one, by record id.</summary>
    public async Task<IReadOnlyDictionary<Guid, RecordSummaryResponse>> OfAsync(IEnumerable<(Guid? Id, string Status, bool Reached)> rows, CancellationToken ct)
    {
        var reached = rows
            .Where(r => r is { Reached: true, Id: not null, Status: BillRecordStatuses.Open or BillRecordStatuses.Closed })
            .GroupBy(r => r.Id!.Value)
            .Select(g => g.First())
            .ToList();
        var result = new Dictionary<Guid, RecordSummaryResponse>();
        if (reached.Count == 0)
        {
            return result;
        }

        var days = await digests.SummaryDaysAsync(reached.Select(r => r.Id!.Value).ToList(), ct);
        foreach (var row in reached)
        {
            var id = row.Id!.Value;
            var known = days.GetValueOrDefault(id);
            if (RowSummaryRules.Of(row.Status, row.Reached, known?.Planned, known?.LastSent) is { } summary)
            {
                result[id] = summary;
            }
        }

        return result;
    }
}
