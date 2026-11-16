namespace Lt2.Identity.Domain;

/// <summary>What a profile may hold (design 6.2), checked where the profile is written (design 6.8).</summary>
internal static class ProfileRules
{
    /// <summary>The longest display name; the registration form says the same (<c>AuthRules.DisplayNameMaxLength</c>).</summary>
    public const int DisplayNameMaxLength = 60;

    /// <summary>The display name as stored: trimmed, null when empty.</summary>
    public static string? DisplayName(string? typed) => string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

    /// <summary><c>display-name.too-long</c>, or null when the name may be stored.</summary>
    public static string? CheckDisplayName(string? typed) =>
        DisplayName(typed) is { Length: > DisplayNameMaxLength } ? "display-name.too-long" : null;

    /// <summary>Only a different name is a change (design 6.2): the same name again (or empty again) changes and audits nothing.</summary>
    public static bool IsChange(string? current, string? typed) =>
        !string.Equals(DisplayName(current), DisplayName(typed), StringComparison.Ordinal);
}
