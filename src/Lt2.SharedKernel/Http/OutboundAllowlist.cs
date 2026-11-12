using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lt2.SharedKernel.Http;

/// <summary>
/// Design 6.4: outbound requests go only to a fixed allowlist of hosts. Every <see cref="HttpClient"/> made by the
/// client factory passes through <see cref="OutboundAllowlistHandler"/>, which refuses any other host before a
/// connection is opened. The list is configuration (<c>Outbound:AllowedHosts</c>), exact host names, no wildcards.
/// </summary>
public static class OutboundAllowlist
{
    public const string Section = "Outbound:AllowedHosts";

    /// <summary>Applies the allowlist from configuration to every HTTP client the factory creates. Idempotent.</summary>
    public static IServiceCollection AddOutboundAllowlist(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(d => d.ServiceType == typeof(AllowedHosts)))
        {
            return services;
        }

        var hosts = Read(configuration);
        services.AddSingleton(new AllowedHosts(hosts));
        services.AddTransient<OutboundAllowlistHandler>();
        services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler<OutboundAllowlistHandler>());
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

/// <summary>Refuses a request to a host that is not on the allowlist.</summary>
public sealed class OutboundAllowlistHandler(AllowedHosts allowed, ILogger<OutboundAllowlistHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is null || !allowed.Allows(request.RequestUri))
        {
            var host = request.RequestUri?.IdnHost ?? "(none)";
            logger.LogError("Outbound request to {Host} refused: not on the allowlist", host);
            throw new OutboundHostNotAllowedException(host);
        }

        return base.SendAsync(request, cancellationToken);
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
