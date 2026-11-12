using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Lt2.Parliament.Import.Client;

/// <summary>
/// Every call of the Seimas open-data chain (design 7.1). Returns raw bodies: storing and parsing are the import's job.
/// Fails only with <see cref="SeimasUnavailableException"/> (after retries) or cancellation.
/// </summary>
internal interface ISeimasClient
{
    /// <summary><c>p2b.ad_seimo_kadencijos</c>: all terms.</summary>
    Task<SeimasResponse> GetTermsAsync(CancellationToken cancellationToken);

    /// <summary><c>p2b.ad_seimo_nariai?kadencijos_id=</c>: the MPs of a term with memberships and contacts.</summary>
    Task<SeimasResponse> GetMembersAsync(int termId, CancellationToken cancellationToken);

    /// <summary><c>p2b.ad_seimo_sesijos?kadencijos_id=</c>: the sessions of a term.</summary>
    Task<SeimasResponse> GetSessionsAsync(int termId, CancellationToken cancellationToken);

    /// <summary><c>p2b.ad_seimo_posedziai?sesijos_id=</c>: the sittings of a session that have started.</summary>
    Task<SeimasResponse> GetSittingsAsync(long sessionId, CancellationToken cancellationToken);

    /// <summary><c>p2b.ad_sp_darbotvarke?posedzio_id=</c>: the agenda items of a sitting.</summary>
    Task<SeimasResponse> GetAgendaAsync(long sittingId, CancellationToken cancellationToken);

    /// <summary><c>p2b.ad_sp_klausimo_svarstymo_eiga</c>: how a sitting's items were considered, with their votes.</summary>
    Task<SeimasResponse> GetProceedingsAsync(long sittingId, CancellationToken cancellationToken);

    /// <summary><c>p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=</c>: totals and each MP's vote with faction.</summary>
    Task<SeimasResponse> GetVoteResultsAsync(long voteId, CancellationToken cancellationToken);

    /// <summary>Any request of the chain.</summary>
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

    public Task<SeimasResponse> GetTermsAsync(CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.Terms(), cancellationToken);

    public Task<SeimasResponse> GetMembersAsync(int termId, CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.Members(termId), cancellationToken);

    public Task<SeimasResponse> GetSessionsAsync(int termId, CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.Sessions(termId), cancellationToken);

    public Task<SeimasResponse> GetSittingsAsync(long sessionId, CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.Sittings(sessionId), cancellationToken);

    public Task<SeimasResponse> GetAgendaAsync(long sittingId, CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.Agenda(sittingId), cancellationToken);

    public Task<SeimasResponse> GetProceedingsAsync(long sittingId, CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.Proceedings(options.Value.ProceedingsParameter, sittingId), cancellationToken);

    public Task<SeimasResponse> GetVoteResultsAsync(long voteId, CancellationToken cancellationToken) =>
        GetAsync(SeimasRequest.VoteResults(voteId), cancellationToken);

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
            catch (Exception ex) when (IsOutage(ex, cancellationToken))
            {
                throw new SeimasUnavailableException(request, ex.GetType().Name, ex);
            }

            if (IsWellFormedXml(body))
            {
                synthetic |= ContainsMarker(body);
                return new SeimasResponse(request, url, body, synthetic, clock.GetUtcNow());
            }

            if (attempt >= attempts)
            {
                throw new SeimasUnavailableException(request, "the response is not well-formed XML");
            }

            logger.LogWarning("Seimas response for {Request} is not well-formed XML; asking again ({Attempt}/{Attempts})", request.Service, attempt, attempts);
        }
    }

    private static bool IsOutage(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or TimeoutRejectedException or BrokenCircuitException => true,
        TaskCanceledException or OperationCanceledException => !cancellationToken.IsCancellationRequested,
        IOException => true,
        _ => false,
    };

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

    private static bool ContainsMarker(byte[] body) =>
        body.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(SyntheticMarker.Comment)) >= 0;
}
