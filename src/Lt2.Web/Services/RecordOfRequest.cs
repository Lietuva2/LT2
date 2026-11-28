using Lt2.Appeals;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// A bill's public record (<see cref="IAppealsModule.GetAsync"/>) as one request reads it (design 9.5): while the server
/// renders a bill's page, the page, its islands and the summaries' lines each ask for the same record, and the first read
/// serves the others. Scoped, so it lives for one request and never between requests: the bill's page stays live. Only a
/// GET or HEAD request shares its reads; any other request (a form, a write) reads afresh each time, so nothing it changed
/// is hidden from what it renders after.
/// </summary>
internal sealed class RecordOfRequest(IAppealsModule appeals, IHttpContextAccessor accessor)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Task<Result<BillRecordInfo>>> reads = new(StringComparer.OrdinalIgnoreCase);

    public Task<Result<BillRecordInfo>> GetAsync(string number, CancellationToken cancellationToken)
    {
        if (accessor.HttpContext?.Request.Method is not { } method || !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method)))
        {
            return appeals.GetAsync(number, cancellationToken);
        }

        lock (gate)
        {
            // A read that failed or was cancelled is not kept: the next asker reads again.
            if (!reads.TryGetValue(number, out var read) || read.IsFaulted || read.IsCanceled)
            {
                read = appeals.GetAsync(number, cancellationToken);
                reads[number] = read;
            }

            return read;
        }
    }
}
