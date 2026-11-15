namespace Lt2.Api.Contracts.Me;

/// <summary>
/// The registration link with an invitation code (design 6.2). Design 6.8: the code is after the <c>#</c>
/// (<c>/prisijungti#kodas=…</c>, with <c>&amp;tikslas=…</c> beside it for a share link), so the browser never sends it
/// and no proxy or application log can hold it. The page reads it in the browser and sends it to the API in a request
/// body. A link in the old form (<c>/prisijungti?kodas=…</c>) still works.
/// </summary>
public static class InvitationLink
{
    /// <summary>The registration page.</summary>
    public const string Page = "/prisijungti";

    /// <summary>The parameter that holds the code, after the <c>#</c> (or, in an old link, in the query).</summary>
    public const string CodeParameter = "kodas";

    /// <summary>The registration form with the code filled in (a relative link; the page shows it in full).</summary>
    public static string For(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Page + "#" + CodeParameter + "=" + Uri.EscapeDataString(code);
    }

    /// <summary>A parameter of the part after the <c>#</c> of an address (<c>a=1&amp;b=2</c>), unescaped; null when absent or empty.</summary>
    public static string? FromFragment(string address, string parameter)
    {
        ArgumentNullException.ThrowIfNull(address);
        var hash = address.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            return null;
        }

        foreach (var pair in address[(hash + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && string.Equals(pair[..equals], parameter, StringComparison.Ordinal))
            {
                var value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' ')).Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>The whole part after the <c>#</c>, unescaped (a representative's token); null when there is none.</summary>
    public static string? Fragment(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var hash = address.IndexOf('#', StringComparison.Ordinal);
        var value = hash < 0 ? "" : Uri.UnescapeDataString(address[(hash + 1)..]).Trim();
        return value.Length == 0 ? null : value;
    }
}