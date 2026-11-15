using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lt2.Parliament.Import.Client;

/// <summary>
/// Every call of the Seimas open-data chain (design 7.1), built with <see cref="SeimasRequest"/>. Returns raw bodies:
/// storing and parsing are the import's job. Fails with <see cref="SeimasUnavailableException"/> (after retries), with
/// <c>OutboundHostNotAllowedException</c> when the configured address is not on the allowlist, or on cancellation.
/// </summary>
internal interface ISeimasClient
{
    Task<SeimasResponse> GetAsync(SeimasRequest request, CancellationToken cancellationToken);
}

/// <summary>The service could not be reached or gave no usable answer, after all retries.</summary>
internal sealed class SeimasUnavailableException(SeimasRequest request, string reason, Exception? inner = null)
    : Exception($"Seimas open data unavailable for {request}: {reason}", inner)
{
    public SeimasRequest Request { get; } = request;
}

/// <summary>Keeps requests at least <see cref="SeimasOptions.MinimumIntervalMilliseconds"/> apart, process-wide.</summary>
internal sealed class SeimasThrottle(IOptions<SeimasOptions> options, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var interval = TimeSpan.FromMilliseconds(options.Value.MinimumIntervalMilliseconds);
            var wait = _last + interval - clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, clock, cancellationToken);
            }

            _last = clock.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

internal sealed class SeimasClient(
    IHttpClientFactory httpClients,
    IOptions<SeimasOptions> options,
    SeimasThrottle throttle,
    TimeProvider clock,
    ILogger<SeimasClient> logger) : ISeimasClient
{
    public const string HttpClientName = "seimas";

    public async Task<SeimasResponse> GetAsync(SeimasRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var http = httpClients.CreateClient(HttpClientName);
        var url = new Uri(http.BaseAddress ?? throw new InvalidOperationException("Seimas:BaseAddress is not set."), request.RelativeUri);
        var attempts = 1 + Math.Max(0, options.Value.MalformedRetries);
        for (var attempt = 1; ; attempt++)
        {
            await throttle.WaitAsync(cancellationToken);
            byte[] body;
            bool synthetic;
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new SeimasUnavailableException(request, $"HTTP {(int)response.StatusCode}");
                }

                body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                synthetic = response.Headers.Contains(SyntheticMarker.Header);
            }
            catch (Exception ex) when (HttpOutage.Is(ex, cancellationToken))
            {
                throw new SeimasUnavailableException(request, ex.GetType().Name, ex);
            }

            if (IsWellFormedXml(body))
            {
                synthetic |= SyntheticMarker.IsIn(body);
                return new SeimasResponse(request, url, body, synthetic, clock.GetUtcNow());
            }

            if (attempt >= attempts)
            {
                throw new SeimasUnavailableException(request, "the response is not well-formed XML");
            }

            logger.LogWarning("Seimas response for {Request} is not well-formed XML; asking again ({Attempt}/{Attempts})", request.Service, attempt, attempts);
        }
    }


    internal static bool IsWellFormedXml(byte[] body)
    {
        if (body.Length == 0)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(body, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
