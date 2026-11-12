using System.Security.Cryptography;
using System.Text;

namespace Lt2.Web.Security;

/// <summary>
/// Keyed hash of the client IP address for the audit log (design 6.5: IPs are hashed with a key, never stored).
/// Without a configured key nothing is recorded.
/// </summary>
internal sealed class IpHasher(IConfiguration configuration)
{
    private readonly byte[]? _key = configuration["Security:IpHashKey"] is { Length: > 0 } key ? Encoding.UTF8.GetBytes(key.Trim()) : null;

    public string? Hash(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var address = context.Connection.RemoteIpAddress?.ToString();
        if (_key is null || address is null)
        {
            return null;
        }

        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(address)))[..32];
    }
}
