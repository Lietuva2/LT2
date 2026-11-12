using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;

namespace Lt2.Web.Client.Api;

/// <summary>Holds the antiforgery request token for the current page. A full page load after sign-in starts afresh.</summary>
internal sealed class AntiforgeryTokenCache
{
    public string? Token { get; set; }
}

/// <summary>Adds the antiforgery header to every state-changing call (design 6.4), fetching the token once.</summary>
internal sealed class AntiforgeryHandler(AntiforgeryTokenCache cache) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            if (cache.Token is null)
            {
                var tokenUri = new Uri(request.RequestUri!, ApiRoutes.Auth.Antiforgery);
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, tokenUri);
                using var tokenResponse = await base.SendAsync(tokenRequest, cancellationToken);
                tokenResponse.EnsureSuccessStatusCode();
                var token = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>(cancellationToken);
                cache.Token = token?.Token;
            }

            request.Headers.Remove(ApiHeaders.Antiforgery);
            request.Headers.Add(ApiHeaders.Antiforgery, cache.Token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
