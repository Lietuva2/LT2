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
    /// code needs a use left; a personal or an organisation's code needs fewer completed registrations than its setting
    /// allows (<paramref name="perCodeLimit"/>, 0 = no limit; see <see cref="LimitFor"/>). An organisation's code works only
    /// while its organisation is active (<paramref name="organisationOff"/> null, design 8.14.14 stage B).
    /// </summary>
    public static bool Usable(
        string kind, int usesLeft, int usedCount, DateTimeOffset? expiresAt, DateTimeOffset? switchedOffAt, DateTimeOffset? replacedAt,
        int perCodeLimit, DateTimeOffset now, string? organisationOff = null)
    {
        if (switchedOffAt is not null || replacedAt is not null || organisationOff is not null || (expiresAt is { } end && end <= now))
        {
            return false;
        }

        return LimitedBySetting(kind)
            ? perCodeLimit == 0 || usedCount < perCodeLimit
            : usesLeft > 0;
    }

    /// <summary><see cref="Usable(string, int, int, DateTimeOffset?, DateTimeOffset?, DateTimeOffset?, int, DateTimeOffset, string?)"/> for a stored code.</summary>
    public static bool Usable(Invitation invitation, int perCodeLimit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        return Usable(
            invitation.Kind, invitation.UsesLeft, invitation.UsedCount, invitation.ExpiresAt, invitation.SwitchedOffAt, invitation.ReplacedAt,
            perCodeLimit, now, invitation.OrganisationOff);
    }

    /// <summary>Personal and organisation codes are limited by a uses setting; team codes count their own uses down.</summary>
    public static bool LimitedBySetting(string kind) => kind is InvitationKinds.Personal or InvitationKinds.Organisation;

    /// <summary>The uses setting that applies to a code of <paramref name="kind"/>: personal, organisation, or none (team codes count down).</summary>
    public static int LimitFor(string kind, int personalLimit, int organisationLimit) => kind switch
    {
        InvitationKinds.Personal => personalLimit,
        InvitationKinds.Organisation => organisationLimit,
        _ => 0,
    };

    /// <summary>
    /// An organisation's code brings people at the level a personal code of the representative who first asked for it
    /// would: that member's level plus one. Nothing of that person is kept on the code.
    /// </summary>
    public static int OrganisationCodeLevel(int requesterLevel) => PersonalCodeLevel(requesterLevel);

    /// <summary>
    /// What a profile keeps of the code its registration was completed with (design 6.2, 8.14.14): a team or personal code
    /// is kept with its level; an organisation's code only as its level, so nothing leads from the account to it.
    /// </summary>
    public static (Guid? InvitationId, string? Code, int? Level) KeptOnProfile(string? kind, Guid? id, string? code, int? level) =>
        kind switch
        {
            null => (null, null, null),
            InvitationKinds.Organisation => (null, null, level),
            _ => (id, code, level),
        };

    /// <summary>
    /// What a pending account holds of the code entered (design 6.2): a team or personal code until completion; never an
    /// organisation's code, which the browser keeps until the registration is completed (design 8.14.14, stage B).
    /// </summary>
    public static string? HeldWhilePending(string kind, string code) => kind == InvitationKinds.Organisation ? null : code;

    /// <summary>A member's level: that of the code they registered with, or 1 when they registered without one.</summary>
    public static int MemberLevel(int? levelOfCodeUsed) => levelOfCodeUsed ?? 1;

    /// <summary>A personal code brings people one level below its member.</summary>
    public static int PersonalCodeLevel(int memberLevel) => memberLevel + 1;

    /// <summary>What a member sees: everyone who completed registration with any of their codes, the replaced ones too.</summary>
    public static int MemberCount(IEnumerable<int> usedCountsOfTheirCodes) => usedCountsOfTheirCodes.Sum();
}
