using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Lt2.Parliament.Import.Client;

/// <summary>
/// What counts as the other side being unreachable, for every outbound client of the module (the open-data service and
/// the lrs.lt agenda page): a dropped connection, a time-out of the resilience pipeline, an open circuit, or a time-out of
/// the request itself. Cancellation by the caller is not an outage.
/// </summary>
internal static class HttpOutage
{
    public static bool Is(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException or TimeoutRejectedException or BrokenCircuitException or IOException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}