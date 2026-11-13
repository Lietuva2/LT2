using System.Security.Cryptography;

namespace Lt2.Identity.Domain;

/// <summary>
/// Invitation codes (design 6.2, personal codes decided 2026-11-13) as pure functions: the personal code format, the
/// look-up form, whether a code lets a registration through, the counts and the levels.
/// </summary>
internal static class InvitationRules
{
    /// <summary>29 characters none of which can be mistaken for another: no 0, 1, I, L, O, U.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Eight characters, shown as two groups of four: about 5 × 10¹¹ codes.</summary>
    public const int Length = 8;

    /// <summary>A new personal code, <c>XXXX-XXXX</c>.</summary>
    public static string GeneratePersonal()
    {
        Span<char> chars = stackalloc char[Length + 1];
        for (var i = 0; i < Length; i++)
        {
            chars[i < 4 ? i : i + 1] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        chars[4] = '-';
        return new string(chars);
    }

    /// <summary>A code as typed, for comparison: case, dashes and spaces do not matter (the stored look-up column).</summary>
    public static string Lookup(string? typed) =>
        string.Concat((typed ?? "").Where(c => c is not ('-' or ' ' or '\t'))).ToUpperInvariant();

    /// <summary>
    /// Whether a code lets a registration through at <paramref name="now"/>. Switched off, replaced or expired: no. A team
    /// code needs a use left; a personal code needs fewer completed registrations than the setting allows (0 = no limit).
    /// </summary>
    public static bool Usable(
        string kind, int usesLeft, int usedCount, DateTimeOffset? expiresAt, DateTimeOffset? switchedOffAt, DateTimeOffset? replacedAt,
        int personalLimit, DateTimeOffset now)
    {
        if (switchedOffAt is not null || replacedAt is not null || (expiresAt is { } end && end <= now))
        {
            return false;
        }

        return kind == InvitationKinds.Personal
            ? personalLimit == 0 || usedCount < personalLimit
            : usesLeft > 0;
    }

    /// <summary>A member's level: that of the code they registered with, or 1 when they registered without one.</summary>
    public static int MemberLevel(int? levelOfCodeUsed) => levelOfCodeUsed ?? 1;

    /// <summary>A personal code brings people one level below its member.</summary>
    public static int PersonalCodeLevel(int memberLevel) => memberLevel + 1;

    /// <summary>What a member sees: everyone who completed registration with any of their codes, the replaced ones too.</summary>
    public static int MemberCount(IEnumerable<int> usedCountsOfTheirCodes) => usedCountsOfTheirCodes.Sum();
}
