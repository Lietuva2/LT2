using Lt2.SharedKernel;

namespace Lt2.Appeals.Ai;

/// <summary>
/// The only <see cref="IBillTextSource"/> so far (design 8.13): no reader of the bills' explanatory notes exists. e-seimas
/// has no API, was closed for maintenance until about 2026-11-16, and its page structure must be confirmed against live
/// responses before anything reads it (design 7.1). The summary job therefore makes nothing; the moderator's form, with
/// pasted official text (design 8.11.4), is the way to a description.
/// </summary>
internal sealed class UnavailableBillTextSource : IBillTextSource
{
    public bool CanRead => false;

    public Task<BillText?> ReadAsync(string billNumber, CancellationToken cancellationToken) => Task.FromResult<BillText?>(null);
}
