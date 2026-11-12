using System.Net;

namespace Lt2.SeimasStandIn;

/// <summary>
/// The stand-in in-process, for tests: answers what the container would answer, without a socket. <see cref="FailWhen"/>
/// simulates an outage (the request fails as a dropped connection), so a test can break the service mid-import.
/// </summary>
internal sealed class StandInHandler(StandInResponses responses) : HttpMessageHandler
{
    private int _count;

    /// <summary>Requests answered or refused so far.</summary>
    public int RequestCount => _count;

    /// <summary>Every path and query asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Return true to drop the connection for this request.</summary>
    public Func<HttpRequestMessage, bool>? FailWhen { get; set; }

    public StandInResponses Responses => responses;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Increment(ref _count);
        var uri = request.RequestUri ?? throw new InvalidOperationException("No request address.");
        lock (Requests)
        {
            Requests.Add(uri.PathAndQuery);
        }

        if (FailWhen?.Invoke(request) == true)
        {
            throw new HttpRequestException("Stand-in: connection dropped (simulated outage).");
        }

        var answer = Answer(uri, responses);
        var message = new HttpResponseMessage((HttpStatusCode)answer.Status) { Content = new ByteArrayContent(answer.Body), RequestMessage = request };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        message.Headers.Add(StandInResponses.SyntheticHeader, "1");
        return Task.FromResult(message);
    }

    /// <summary>Shared by the in-process handler and the HTTP host: only paths under <c>/sip/</c> exist.</summary>
    public static StandInResponse Answer(Uri uri, StandInResponses responses)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(responses);
        const string prefix = "/sip/";
        var path = uri.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length)
        {
            return new StandInResponse(404, "Not found."u8.ToArray());
        }

        var service = Uri.UnescapeDataString(path[prefix.Length..]);
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(p => new KeyValuePair<string, string>(Uri.UnescapeDataString(p[0]), p.Length > 1 ? Uri.UnescapeDataString(p[1]) : ""));
        return responses.Respond(service, query);
    }
}
