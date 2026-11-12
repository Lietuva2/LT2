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
        else if (PhonePattern().Matches(text).Any(m => DigitCount(m.Value) is >= 8 and <= 12))
        {
            flags.Add(ScreeningFlags.Phone);
        }

        if (LinkPattern().IsMatch(text))
        {
            flags.Add(ScreeningFlags.Link);
        }

        return flags;
    }

    /// <summary>
    /// A Lithuanian personal code (asmens kodas): 11 digits, the first 1–6 (century and sex), a valid birth date, and the
    /// official check digit.
    /// </summary>
    public static bool ContainsPersonalCode(string text) =>
        ElevenDigits().Matches(text).Any(m => IsPersonalCode(m.Value));

    public static bool IsPersonalCode(string digits)
    {
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit) || digits[0] is < '1' or > '6')
        {
            return false;
        }

        var d = digits.Select(c => c - '0').ToArray();
        var month = d[3] * 10 + d[4];
        var day = d[5] * 10 + d[6];
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += d[i] * (i % 9 + 1);
        }

        var check = sum % 11;
        if (check == 10)
        {
            sum = 0;
            for (var i = 0; i < 10; i++)
            {
                sum += d[i] * ((i + 2) % 9 + 1);
            }

            check = sum % 11;
            if (check == 10)
            {
                check = 0;
            }
        }

        return check == d[10];
    }

    private static int DigitCount(string value) => value.Count(char.IsAsciiDigit);

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.\p{L}{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    // A run of digits with the separators people put in phone numbers, optionally with a leading +.
    [GeneratedRegex(@"(?<![\p{N}])\+?\d[\d\s().-]{6,16}\d(?![\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ElevenDigits();

    [GeneratedRegex(@"(https?://|www\.)\S+|\b[\p{L}\p{N}-]+\.(lt|com|eu|org|net|info|io|de|ru|lv|ee|pl|uk|me|tv|co|app)\b(/\S*)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
