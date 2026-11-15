using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lt2.SharedKernel.Http;

/// <summary>
/// Design 6.4: outbound requests go only to a fixed allowlist of hosts. Every <see cref="HttpClient"/> made by the
/// client factory passes through <see cref="OutboundAllowlistHandler"/>, which refuses any other host before a
/// connection is opened, and follows redirects itself so that a redirect to another host is refused too (design 6.8).
/// The list is configuration (<c>Outbound:AllowedHosts</c>), exact host names, no wildcards, plus the hosts a module
/// adds with <see cref="AddOutboundHost"/> for a feature it switched on (the AI provider).
/// </summary>
public static class OutboundAllowlist
{
    public const string Section = "Outbound:AllowedHosts";

    /// <summary>Applies the allowlist to every HTTP client the factory creates. Idempotent; the hosts are read when first needed.</summary>
    public static IServiceCollection AddOutboundAllowlist(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(d => d.ServiceType == typeof(AllowedHosts)))
        {
            return services;
        }

        services.AddSingleton(sp => new AllowedHosts(
            Read(configuration).Concat(sp.GetServices<OutboundHost>().Select(h => h.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        services.AddTransient<OutboundAllowlistHandler>();
        services.ConfigureHttpClientDefaults(client => client
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<OutboundAllowlistHandler>());
        return services;
    }

    /// <summary>One more allowed host, for a feature a module switched on in configuration. Works in any order with <see cref="AddOutboundAllowlist"/>.</summary>
    public static IServiceCollection AddOutboundHost(this IServiceCollection services, string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        services.AddSingleton(new OutboundHost(host.Trim()));
        return services;
    }

    /// <summary>The configured hosts; empty entries (an unset optional variable) are ignored.</summary>
    public static IReadOnlySet<string> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(Section).GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>A host a module added to the allowlist (<see cref="OutboundAllowlist.AddOutboundHost"/>).</summary>
public sealed record OutboundHost(string Name);

/// <summary>The hosts outbound requests may go to.</summary>
public sealed class AllowedHosts(IReadOnlySet<string> hosts)
{
    public IReadOnlySet<string> Hosts { get; } = hosts;

    public bool Allows(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && Hosts.Contains(uri.IdnHost);
    }
}

/// <summary>
/// Refuses a request to a host that is not on the allowlist, and follows redirects itself (the primary handler does
/// not), refusing a redirect to such a host the same way. A redirect from HTTPS to HTTP, and one that would have to
/// send a request body again, is not followed: the caller gets the redirect answer.
/// </summary>
public sealed class OutboundAllowlistHandler(AllowedHosts allowed, ILogger<OutboundAllowlistHandler> logger) : DelegatingHandler
{
    /// <summary>The most redirects one request follows.</summary>
    public const int MaxRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Check(request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        for (var hop = 0; hop < MaxRedirects && Next(request, response) is { } next; hop++)
        {
            response.Dispose();
            Check(next.RequestUri);
            request = next;
            response = await base.SendAsync(request, cancellationToken);
        }

        return response;
    }

    private void Check(Uri? uri)
    {
        if (uri is null || !allowed.Allows(uri))
        {
            var host = uri?.IdnHost ?? "(none)";
            logger.LogError("Outbound request to {Host} refused: not on the allowlist", host);
            throw new OutboundHostNotAllowedException(host);
        }
    }

    /// <summary>The request a redirect answer asks for, as the framework's own redirect handling would make it; or null.</summary>
    private static HttpRequestMessage? Next(HttpRequestMessage request, HttpResponseMessage response)
    {
        var status = response.StatusCode;
        if (status is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            || response.Headers.Location is not { } location || request.RequestUri is not { } from)
        {
            return null;
        }

        var target = location.IsAbsoluteUri ? location : new Uri(from, location);
        if (from.Scheme == Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var keepsMethod = status is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
        if (keepsMethod && request.Content is not null)
        {
            return null;
        }

        var method = keepsMethod || request.Method == HttpMethod.Head ? request.Method
            : status == HttpStatusCode.SeeOther || request.Method == HttpMethod.Post ? HttpMethod.Get
            : request.Method;
        var next = new HttpRequestMessage(method, target) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        var sameHost = string.Equals(from.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            // Credentials (Authorization, an API key header) go only to the host they were meant for.
            if (sameHost || header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                || header.Key.StartsWith("Accept", StringComparison.OrdinalIgnoreCase))
            {
                next.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return next;
    }
}

/// <summary>A request to a host outside <c>Outbound:AllowedHosts</c>. Not transient: retrying cannot help.</summary>
public sealed class OutboundHostNotAllowedException : InvalidOperationException
{
    public OutboundHostNotAllowedException()
    {
    }

    public OutboundHostNotAllowedException(string host)
        : base($"Outbound requests to '{host}' are not allowed (Outbound:AllowedHosts).")
    {
        Host = host;
    }

    public OutboundHostNotAllowedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Host { get; }
}
