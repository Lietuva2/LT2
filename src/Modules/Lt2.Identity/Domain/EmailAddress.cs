using System.Globalization;

namespace Lt2.Identity.Domain;

/// <summary>
/// Normalisation for the one-account-per-address rule (design 6.2): lower case, the <c>+tag</c> removed, and dots
/// ignored where the provider ignores them. The result is only a key; mail is always sent to the address as typed.
/// </summary>
internal static class EmailAddress
{
    private static readonly IdnMapping Idn = new();

    /// <summary>Providers known to ignore dots in the local part, mapped to their canonical domain.</summary>
    private static readonly Dictionary<string, string> DotlessProviders = new(StringComparer.Ordinal)
    {
        ["gmail.com"] = "gmail.com",
        ["googlemail.com"] = "gmail.com",
    };

    /// <summary>Returns the normalised key, or <c>null</c> when the input is not a usable address.</summary>
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
        {
            return null;
        }

        var local = trimmed[..at].ToLowerInvariant();
        var domain = trimmed[(at + 1)..].TrimEnd('.').ToLowerInvariant();
        try
        {
            domain = Idn.GetAscii(domain);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var plus = local.IndexOf('+', StringComparison.Ordinal);
        if (plus == 0)
        {
            return null;
        }

        if (plus > 0)
        {
            local = local[..plus];
        }

        if (DotlessProviders.TryGetValue(domain, out var canonical))
        {
            local = local.Replace(".", "", StringComparison.Ordinal);
            domain = canonical;
        }

        return local.Length == 0 ? null : $"{local}@{domain}";
    }

    /// <summary>The domain part of a normalised address.</summary>
    public static string Domain(string normalized) => normalized[(normalized.LastIndexOf('@') + 1)..];
}

/// <summary>Domains of disposable mailbox services, refused at registration (design 6.2). A starting list.</summary>
internal static class DisposableDomains
{
    private static readonly HashSet<string> Domains = new(StringComparer.Ordinal)
    {
        "10minutemail.com",
        "discard.email",
        "dispostable.com",
        "emailondeck.com",
        "fakeinbox.com",
        "getnada.com",
        "guerrillamail.com",
        "guerrillamail.net",
        "mailinator.com",
        "maildrop.cc",
        "mailnesia.com",
        "mintemail.com",
        "mohmal.com",
        "sharklasers.com",
        "spamgourmet.com",
        "temp-mail.org",
        "tempmail.com",
        "throwawaymail.com",
        "trashmail.com",
        "yopmail.com",
    };

    public static bool Contains(string normalizedEmail)
    {
        var domain = EmailAddress.Domain(normalizedEmail);
        return Domains.Contains(domain) || Domains.Any(d => domain.EndsWith("." + d, StringComparison.Ordinal));
    }
}
