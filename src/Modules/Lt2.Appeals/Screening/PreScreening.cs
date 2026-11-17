using System.Text.RegularExpressions;

namespace Lt2.Appeals.Screening;

/// <summary>What pre-screening found in a text, for the moderator. A flag never decides anything by itself.</summary>
internal static class ScreeningFlags
{
    public const string Email = "email";
    public const string Phone = "phone";
    public const string PersonalCode = "personal-code";
    public const string Link = "link";
}

/// <summary>
/// Moderation pre-screening (design 8.1, 8.3): flags texts a moderator should look at before they are published. The
/// design puts an AI assistant here (<c>IAiAssistant</c>, design 3), but its provider is an open decision (section 12),
/// so the only implementation today is <see cref="RuleBasedPreScreening"/>, which sends nothing anywhere.
/// </summary>
internal interface IPreScreening
{
    Task<IReadOnlyList<string>> ScreenAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Flags only what rules can catch: email addresses, phone numbers, Lithuanian personal codes and links. Insults and
/// spam wait for the AI provider.
/// </summary>
internal sealed partial class RuleBasedPreScreening : IPreScreening
{
    public Task<IReadOnlyList<string>> ScreenAsync(string text, CancellationToken cancellationToken) =>
        Task.FromResult(Screen(text));

    public static IReadOnlyList<string> Screen(string text)
    {
        var flags = new List<string>();
        if (EmailPattern().IsMatch(text))
        {
            flags.Add(ScreeningFlags.Email);
        }

        if (ContainsPersonalCode(text))
        {
            flags.Add(ScreeningFlags.PersonalCode);
        }
        else if (ContainsPhoneNumber(text))
        {
            flags.Add(ScreeningFlags.Phone);
        }

        if (LinkPattern().IsMatch(text))
        {
            flags.Add(ScreeningFlags.Link);
        }

        return flags;
    }

    /// <summary>A Lithuanian personal code (<see cref="Lt2.SharedKernel.PersonalCodes"/>, the one definition).</summary>
    public static bool ContainsPersonalCode(string text) => Lt2.SharedKernel.PersonalCodes.Contains(text);

    public static bool IsPersonalCode(string digits) => Lt2.SharedKernel.PersonalCodes.Is(digits);

    /// <summary>
    /// A Lithuanian phone number (owner, 2026-11-17): the 8-digit national number, which starts with 3–9 (a mobile 6xx xxxxx,
    /// Vilnius 5 xxx xxxx, another area's 2-digit code and 6 digits, the 7xx and 8xx services), written internationally after
    /// <c>+370</c> or <c>00370</c>, nationally after the trunk prefix <c>0</c> in use since 2025, or after the old prefix
    /// <c>8</c> people still write; spaces, hyphens and brackets may stand between the digits. Also (owner, 2026-11-17) a mobile
    /// number without any prefix: eight digits starting with 6, standing alone, written together (<c>61234567</c>) or grouped
    /// as people write them (<c>612 34567</c>, <c>6123 4567</c>, <c>6 1234 567</c>, with a space or a hyphen); a landline
    /// without a prefix stays unflagged. Nothing else: not a date, a year span, a sum (a thousands grouping such as
    /// <c>60 000 000</c> is none of those shapes), a law's or a document's number (after <c>Nr.</c> or a letter and a hyphen),
    /// a percentage, nor digits inside an <c>https://</c> or <c>http://</c> link.
    /// </summary>
    public static bool ContainsPhoneNumber(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var withoutLinks = LinkAddress().Replace(text, " ");
        return PhonePattern().IsMatch(withoutLinks) || MobileWithoutPrefixPattern().IsMatch(withoutLinks);
    }

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.\p{L}{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    // +370 / 00370, or the trunk prefix 0 or 8, then the national number's eight digits (the first 3–9); not inside a longer
    // run of digits on either side (a sum such as "25 000 000" or "1 234 567" starts after a digit).
    [GeneratedRegex(
        @"(?<![\p{N}][\s\-.,/]?)(?<![\p{L}])(?:(?:\+|00)\s*370|\(?[08])[\s\-()]*[3-9](?:[\s\-()]*\d){7}(?![\s\-.,/]?\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    // A mobile number without a prefix (owner, 2026-11-17): 6 and seven digits, together or as 3+5, 4+4 or 1+4+3 with one space
    // or hyphen between the groups. Not inside a longer run of digits on either side, not after a letter or a letter and a
    // hyphen or slash (XIV-61234567), not after "Nr." (a document's number) or a sign (+, €, $), not before a percent or currency sign.
    [GeneratedRegex(
        @"(?<![\p{N}][\s\-.,/]?)(?<![\p{L}][\-/]?)(?<![\p{L}])(?<![+€$])(?<![Nn]r\.\s?)6(?:\d{7}|\d{2}[ \-]\d{5}|\d{3}[ \-]\d{4}|[ \-]\d{4}[ \-]\d{3})(?![\s\-.,/]?\d)(?!\s?[%€$])",
        RegexOptions.CultureInvariant)]
    private static partial Regex MobileWithoutPrefixPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkAddress();

    [GeneratedRegex(@"(https?://|www\.)\S+|\b[\p{L}\p{N}-]+\.(lt|com|eu|org|net|info|io|de|ru|lv|ee|pl|uk|me|tv|co|app)\b(/\S*)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
