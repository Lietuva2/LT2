using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Lt2.Web.Security;

/// <summary>
/// Design 6.8, "Forwarded addresses are believed only from the proxy itself": the <c>X-Forwarded-*</c> headers of a
/// request count only when the request comes from the proxy named in <c>ReverseProxy:Address</c>, a host name (resolved
/// through DNS, Docker's on a compose network, and resolved again when a request comes from an address it did not give
/// before, at most every few seconds, so a restarted proxy with a new address is followed) or an IP address. Not set:
/// no forwarding header is believed. On a shared Docker network the proxy's neighbours can reach the application too;
/// their headers are removed before <c>UseForwardedHeaders</c> reads them.
/// </summary>
internal sealed class TrustedProxy(IConfiguration configuration, TimeProvider clock, ILogger<TrustedProxy> logger) : IDisposable
{
    public const string AddressSetting = "ReverseProxy:Address";

    /// <summary>The least time between two lookups of the proxy's name.</summary>
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(5);

    private static readonly string[] Headers =
        [ForwardedHeadersDefaults.XForwardedForHeaderName, ForwardedHeadersDefaults.XForwardedProtoHeaderName,
         ForwardedHeadersDefaults.XForwardedHostHeaderName, ForwardedHeadersDefaults.XForwardedPrefixHeaderName];

    private readonly string? _name = configuration[AddressSetting]?.Trim() is { Length: > 0 } name ? name : null;
    private readonly SemaphoreSlim _lookup = new(1, 1);
    private IPAddress[] _addresses = [];
    private DateTimeOffset _resolvedAt = DateTimeOffset.MinValue;

    /// <summary>Whether <paramref name="remote"/> is the configured proxy.</summary>
    public async ValueTask<bool> IsProxyAsync(IPAddress? remote, CancellationToken cancellationToken)
    {
        if (_name is null || remote is null)
        {
            return false;
        }

        var address = Normalise(remote);
        if (IPAddress.TryParse(_name, out var literal))
        {
            return Normalise(literal).Equals(address);
        }

        if (_addresses.Contains(address))
        {
            return true;
        }

        if (clock.GetUtcNow() - _resolvedAt < Refresh)
        {
            return false;
        }

        await _lookup.WaitAsync(cancellationToken);
        try
        {
            if (clock.GetUtcNow() - _resolvedAt >= Refresh)
            {
                _resolvedAt = clock.GetUtcNow();
                try
                {
                    _addresses = (await Dns.GetHostAddressesAsync(_name, cancellationToken)).Select(Normalise).ToArray();
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    logger.LogWarning("The proxy's name {Proxy} could not be resolved ({Error}); forwarding headers are not believed", _name, ex.SocketErrorCode);
                    _addresses = [];
                }
            }

            return _addresses.Contains(address);
        }
        finally
        {
            _lookup.Release();
        }
    }

    public void Dispose() => _lookup.Dispose();

    /// <summary>Removes the forwarding headers of a request that does not come from the proxy; then <c>UseForwardedHeaders</c>.</summary>
    public static IApplicationBuilder UseTrustedForwardedHeaders(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var proxy = context.RequestServices.GetRequiredService<TrustedProxy>();
            if (!await proxy.IsProxyAsync(context.Connection.RemoteIpAddress, context.RequestAborted))
            {
                foreach (var header in Headers)
                {
                    context.Request.Headers.Remove(header);
                }
            }

            await next(context);
        });
        return app.UseForwardedHeaders();
    }

    /// <summary>The forwarding options: the proxy's own check is <see cref="TrustedProxy"/>, so the framework's lists are empty.</summary>
    public static IServiceCollection AddTrustedProxy(IServiceCollection services)
    {
        services.AddSingleton<TrustedProxy>();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
        return services;
    }

    private static IPAddress Normalise(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
