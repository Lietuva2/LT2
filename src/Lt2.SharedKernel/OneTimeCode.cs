// One source file, compiled twice, as AccountScreens.cs: into Lt2.SharedKernel for the modules and, linked by
// Lt2.Web.Client.csproj with LT2_CLIENT defined, into the browser's assembly for the pages a code is typed on.
#if LT2_CLIENT
namespace Lt2.Web.Client;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>
/// A typeable one-time code (design 6.8; owner, 2026-11-17: "one should be able to go on without using that url, as it's not
/// safe to click links in email"): the one secret of an MP office member's invitation (8.5.3), an MP's take-over link (8.5.2)
/// and an organisation representative's invitation (8.14.14). The same secret serves both ways in: after the <c>#</c> of the
/// link, and typed on the link's page opened without it. Twelve characters from an alphabet none of whose letters can be
/// mistaken for another (no 0, 1, I, L, O, U), shown in three groups of four: 30¹² ≈ 2⁵⁹ codes. Case, spaces and dashes do not
/// matter when it is typed. Only a hash of its one form (<see cref="Normalize"/>) is stored.
/// </summary>
public static class OneTimeCode
{
    /// <summary>The 30 characters a code is made of (the invitation codes' alphabet, design 6.2).</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Characters in a code, without the dashes.</summary>
    public const int Length = 12;

    /// <summary>Characters in a group.</summary>
    public const int Group = 4;

    /// <summary>The longest text a form accepts for a code (spaces and dashes included).</summary>
    public const int MaxTyped = 40;

#if !LT2_CLIENT
    /// <summary>A new code, <c>XXXX-XXXX-XXXX</c>.</summary>
    public static string New()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Grouped(new string(chars));
    }

    /// <summary>
    /// The stored hash of a code as typed or read from a link: SHA-256 of its one form; of nothing for a text that is no code,
    /// so an unknown and a malformed code are looked up, and answered, alike.
    /// </summary>
    public static byte[] Hash(string? typed) =>
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Normalize(typed) ?? ""));
#endif

    /// <summary>
    /// A code as typed or read from a link, in its one form (<c>XXXX-XXXX-XXXX</c>): case, spaces and dashes ignored; null when
    /// it is not twelve characters of <see cref="Alphabet"/>.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (typed is null || typed.Length > MaxTyped)
        {
            return null;
        }

        var bare = string.Concat(typed.Where(c => c is not ('-' or ' ' or '\t' or '‐' or '‑' or '–'))).ToUpperInvariant();
        return bare.Length == Length && bare.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? Grouped(bare) : null;
    }

    private static string Grouped(string bare) =>
        string.Join("-", Enumerable.Range(0, Length / Group).Select(g => bare.Substring(g * Group, Group)));
}
