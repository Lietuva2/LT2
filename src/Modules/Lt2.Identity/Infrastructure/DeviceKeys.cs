using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// The keyed hash of a known browser's identifier (design 6.2, 8.20.2), handled as the other keyed hashes of the system are
/// (the audit log's IP hash, <c>Security:IpHashKey</c>; MP mail's, <c>Security:MailHashKey</c>): a secret file mounted into the
/// container (<c>deploy/secrets/device_hash_key</c>, read as <c>Security:DeviceHashKey</c>) and HMAC-SHA-256. A key of its own,
/// because the IP key may be rotated (design 6.5), which would make every known browser a stranger. Without a key no browser is
/// known: every sign-in works as for a stranger, and the web host says so once at start.
/// </summary>
internal sealed class DeviceKeys(IConfiguration configuration)
{
    public const string ConfigurationKey = "Security:DeviceHashKey";

    private readonly byte[]? _key = configuration[ConfigurationKey] is { Length: > 0 } key ? Encoding.UTF8.GetBytes(key.Trim()) : null;

    public bool Configured => _key is not null;

    /// <summary>The hash kept in <c>identity.known_device</c>; null without a key.</summary>
    public byte[]? Hash(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return _key is null ? null : HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes("device\n" + identifier));
    }

    /// <summary>A new identifier: 32 random bytes, URL-safe, 43 characters.</summary>
    public static string NewIdentifier() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Whether a cookie's value can be an identifier at all, before anything is hashed or asked.</summary>
    public static bool LooksLikeIdentifier(string? value) =>
        value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
