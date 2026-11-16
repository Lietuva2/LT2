namespace Lt2.Parliament.Infrastructure;

/// <summary>
/// The one definition of an MP's official address (design 8.5.1): an email contact in open data ("El. p.") on the Seimas's
/// own domain, <c>lrs.lt</c>, compared ignoring case. Only MPs whose mandate has not ended are passed in; an address listed
/// for more than one of them belongs to nobody. The summaries' recipients (design 8.4) and "whose official address is this"
/// both use it.
/// </summary>
internal static class OfficialAddresses
{
    /// <summary>The contact kind open data gives an email address.</summary>
    public const string EmailKind = "El. p.";

    /// <summary>The Seimas's own domain; a sub-domain is not it.</summary>
    public const string Domain = "lrs.lt";

    /// <summary>The address as compared: trimmed and in lower case; null when it is not an address on <see cref="Domain"/>.</summary>
    public static string? Normalise(string? address)
    {
        var value = address?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var at = value.LastIndexOf('@');
        return at > 0 && value.IndexOf('@', StringComparison.Ordinal) == at && value[(at + 1)..] == Domain && !value.Any(char.IsWhiteSpace)
            ? value
            : null;
    }

    /// <summary>Whether a contact of open data is an official address (before uniqueness is known).</summary>
    public static bool IsOfficial(string kind, string value) => kind == EmailKind && Normalise(value) is not null;

    /// <summary>
    /// The official addresses among the contacts of MPs whose mandate has not ended, each with its one owner (a Seimas person
    /// id); an address listed for more than one person is left out.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Owned(IEnumerable<(long PersonId, string Kind, string Value)> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        return contacts
            .Where(c => IsOfficial(c.Kind, c.Value))
            .GroupBy(c => Normalise(c.Value)!, StringComparer.Ordinal)
            .Select(g => (Address: g.Key, People: g.Select(c => c.PersonId).Distinct().ToList()))
            .Where(g => g.People.Count == 1)
            .ToDictionary(g => g.Address, g => g.People[0], StringComparer.Ordinal);
    }

    /// <summary>The person whose official address <paramref name="email"/> is, or null.</summary>
    public static long? Owner(IEnumerable<(long PersonId, string Kind, string Value)> contacts, string? email) =>
        Normalise(email) is { } address && Owned(contacts).TryGetValue(address, out var owner) ? owner : null;
}
