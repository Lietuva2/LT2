using System.Security.Cryptography;
using System.Text;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Lt2.Notifications.MpMail;

/// <summary>
/// The keyed hashes of MP mail (design 8.19.4, 8.19.6), handled as the audit log's IP hash is (<c>Security:IpHashKey</c>,
/// <c>IpHasher</c> in the web host): a secret file mounted into the container (<c>deploy/secrets/mail_hash_key</c>, read as
/// <c>Security:MailHashKey</c>) and HMAC-SHA-256. A key of its own, because the IP key may be rotated and a suppressed address
/// must stay suppressed. Used for the suppression list (never the address) and to derive the preferences links: the token
/// of a link is derived from the MP's person number and the link's generation, so every email carries the same link while the
/// database holds only its SHA-256.
/// </summary>
internal sealed class MailKeys(IConfiguration configuration)
{
    public const string ConfigurationKey = "Security:MailHashKey";

    private readonly byte[]? _key = configuration[ConfigurationKey] is { Length: > 0 } key ? Encoding.UTF8.GetBytes(key.Trim()) : null;

    private byte[] Key => _key ?? throw new InvalidOperationException(
        $"{ConfigurationKey} must be set (deploy/secrets/mail_hash_key) before MP mail can be sent or suppressed.");

    /// <summary>The keyed hash of an address as the suppression list keeps it: trimmed and in lower case, hex.</summary>
    public string AddressHash(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Convert.ToHexStringLower(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes("suppression\n" + address.Trim().ToLowerInvariant())));
    }

    /// <summary>
    /// The token of an MP's link of <paramref name="kind"/> in its <paramref name="generation"/>: 32 bytes, URL-safe. The same
    /// inputs give the same token; a new generation gives an unrelated one.
    /// </summary>
    public string DerivedToken(long seimasPersonId, string kind, int generation) =>
        Base64Url(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(FormattableString.Invariant($"token\n{kind}\n{seimasPersonId}\n{generation}"))));

    /// <summary>A one-time token of 32 random bytes, URL-safe (the extra recipient's confirmation).</summary>
    public static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>What the database keeps of a token: its SHA-256, hex.</summary>
    public static string TokenHash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Whether a string can be a token at all (43 URL-safe characters), before the database is asked.</summary>
    public static bool LooksLikeToken(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// The invented-data lock (design 8.19.1). The data is invented when the deployment says so (<c>Demo:Allowed</c>) or when the
/// parliament data itself holds anything synthetic (asked of Parliament through <see cref="IMpMailAddresses"/>), so the lock
/// does not depend on a flag someone can forget. <c>Email:Caught</c>: every email of this deployment lands in a catcher such as
/// Mailpit. Both flags are deployment configuration, not runtime settings: no administrator can switch them.
/// </summary>
internal sealed class MailLocks(IConfiguration configuration, IMpMailAddresses parliament)
{
    private bool? _invented;

    public bool DemoAllowed { get; } = configuration.GetValue<bool>("Demo:Allowed");

    public bool MailCaught { get; } = configuration.GetValue<bool>("Email:Caught");

    /// <summary>Whether the data is invented: <c>Demo:Allowed</c>, or any synthetic parliament data (asked once per scope).</summary>
    public async Task<bool> InventedDataAsync(CancellationToken ct) =>
        _invented ??= DemoAllowed || await parliament.HoldsInventedDataAsync(ct);

    /// <summary>MP mail may leave at all: not where the data is invented and the mail is not caught.</summary>
    public async Task<bool> MailMayLeaveAsync(CancellationToken ct) =>
        Digests.SendingRules.MailMayLeave(await InventedDataAsync(ct), MailCaught);
}
