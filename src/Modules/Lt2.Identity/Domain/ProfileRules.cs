using System.Security.Cryptography;
using Lt2.SharedKernel;

namespace Lt2.Identity.Domain;

/// <summary>What a profile may hold (design 6.2, 8.25), checked where the profile is written (design 6.8).</summary>
internal static class ProfileRules
{
    /// <summary>The longest display name; the registration form says the same (<c>AuthRules.DisplayNameMaxLength</c>).</summary>
    public const int DisplayNameMaxLength = 60;

    /// <summary>A public profile's id (design 8.25.2): ten lower-case letters and digits, random, never the name.</summary>
    public const int IdLength = 10;

    private const string IdAlphabet = "abcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>The display name as stored: trimmed, null when empty.</summary>
    public static string? DisplayName(string? typed) => string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

    /// <summary><c>display-name.too-long</c>, or null when the name may be stored.</summary>
    public static string? CheckDisplayName(string? typed) =>
        DisplayName(typed) is { Length: > DisplayNameMaxLength } ? "display-name.too-long" : null;

    /// <summary>Only a different name is a change (design 6.2): the same name again (or empty again) changes and audits nothing.</summary>
    public static bool IsChange(string? current, string? typed) =>
        !string.Equals(DisplayName(current), DisplayName(typed), StringComparison.Ordinal);

    /// <summary>
    /// A public profile needs a display name (design 8.25.6, the designer's proposal): switching it on without one, and emptying
    /// the name while it is on, are refused with <c>profile.name-required</c>.
    /// </summary>
    public static string? CheckNameWhilePublic(bool isPublic, string? name) =>
        isPublic && DisplayName(name) is null ? "profile.name-required" : null;

    /// <summary>A new random profile id (design 8.25.2): ten characters of an alphabet without look-alikes.</summary>
    public static string NewId()
    {
        Span<char> id = stackalloc char[IdLength];
        for (var i = 0; i < IdLength; i++)
        {
            id[i] = IdAlphabet[RandomNumberGenerator.GetInt32(IdAlphabet.Length)];
        }

        return new string(id);
    }

    /// <summary>Whether a text could be a profile id at all; anything else is unknown without reading the database.</summary>
    public static bool IsWellFormedId(string? id) =>
        id is { Length: IdLength } && id.All(c => IdAlphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>"Apie mane" as stored: line breaks unified, control characters dropped, trimmed; null when empty.</summary>
    public static string? About(string? typed)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return null;
        }

        var unified = typed.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var kept = new string(unified.Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
        return kept.Length == 0 ? null : kept;
    }

    /// <summary>
    /// "Apie mane" (design 8.25.3): up to <c>Texts:MaxLength</c> (<paramref name="maxLength"/>, 0 = only the ceiling)
    /// (<c>about.too-long</c>); a personal code is refused (<c>about.personal-code</c>, the designer's proposal); links, the
    /// person's own email address and phone number are allowed (owner, 2026-11-16). Null when it may be stored.
    /// </summary>
    public static string? CheckAbout(string? typed, int maxLength)
    {
        if (About(typed) is not { } about)
        {
            return null;
        }

        if (about.Length > MemberTextLength.Allowed(maxLength))
        {
            return "about.too-long";
        }

        return PersonalCodes.Contains(about) ? "about.personal-code" : null;
    }

    /// <summary>Only a different text is a change: the same text again changes and audits nothing.</summary>
    public static bool IsAboutChange(string? current, string? typed) => !string.Equals(About(current), About(typed), StringComparison.Ordinal);

    /// <summary>
    /// What a public page shows of "Apie mane" (design 8.25.1, 8.25.6): the text only while the profile is public and no
    /// moderator has hidden it.
    /// </summary>
    public static string? ShownAbout(string? about, DateTimeOffset? hiddenAt) => hiddenAt is null ? About(about) : null;

    /// <summary>
    /// "Atkurti" a hidden text (design 8.25.6): only while the text the moderator hid is still there, not replaced by the
    /// person since (a new text shows at once and is not hidden).
    /// </summary>
    public static bool MayRestore(DateTimeOffset? hiddenAt, DateTimeOffset? changedAt) =>
        hiddenAt is { } hidden && (changedAt is null || changedAt <= hidden);
}
