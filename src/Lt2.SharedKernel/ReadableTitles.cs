namespace Lt2.SharedKernel;

/// <summary>
/// The readable titles (design 8.13.1) that may go off the site, for members' emails: an open or closed record's title whatever
/// its source, an <c>ai</c> one included, but none while a report about the description is open (owner, 2026-11-26).
/// Implemented by the Appeals module. Summaries to MPs and emails to MPs keep the official title (8.4).
/// </summary>
public interface IReadableTitles
{
    /// <summary>By bill number; a bill without such a title is not in the answer.</summary>
    Task<IReadOnlyDictionary<string, string>> ShareableAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);
}

/// <summary>A bill as members' emails name it: the readable title in the official title's place where one may go off the site.</summary>
public static class EmailBillTitles
{
    public static async Task<BillFacts?> FindAsync(this IBillCatalogue bills, IReadableTitles titles, string number, CancellationToken cancellationToken) =>
        await bills.FindAsync(number, cancellationToken) is { } bill
            ? (await titles.ShareableAsync([bill.Number], cancellationToken)).TryGetValue(bill.Number, out var title) ? bill with { Title = title } : bill
            : null;

    public static async Task<IReadOnlyDictionary<string, BillFacts>> FindManyAsync(
        this IBillCatalogue bills, IReadableTitles titles, IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        var found = await bills.FindManyAsync(numbers, cancellationToken);
        if (found.Count == 0)
        {
            return found;
        }

        var readable = await titles.ShareableAsync([.. found.Keys], cancellationToken);
        return readable.Count == 0
            ? found
            : found.ToDictionary(f => f.Key, f => readable.TryGetValue(f.Key, out var title) ? f.Value with { Title = title } : f.Value, StringComparer.OrdinalIgnoreCase);
    }
}
