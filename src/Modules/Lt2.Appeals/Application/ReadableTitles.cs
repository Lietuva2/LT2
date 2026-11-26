using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IReadableTitles"/> for members' emails (design 8.13.1, "Off the site"): an open or closed record's readable title
/// whatever its source, left out while a report about its description is open.
/// </summary>
internal sealed class ReadableTitles(IDbContextFactory<AppealsDb> contexts) : IReadableTitles
{
    public async Task<IReadOnlyDictionary<string, string>> ShareableAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken)
    {
        if (billNumbers.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await db.BillRecords.AsNoTracking()
            .Where(r => billNumbers.Contains(r.BillNumber) && (r.Status == BillRecordStatuses.Open || r.Status == BillRecordStatuses.Closed)
                && r.PlainTitle != null && r.Description != ""
                && !db.Reports.Any(p => p.Status == "open" && p.TargetId == r.Id))
            .Select(r => new { r.BillNumber, r.PlainTitle })
            .ToListAsync(cancellationToken);
        return found.ToDictionary(r => r.BillNumber, r => r.PlainTitle!, StringComparer.OrdinalIgnoreCase);
    }
}
