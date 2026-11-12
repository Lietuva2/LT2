using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lt2.Identity.Domain;

/// <summary>Outcome of checking a sign-in code.</summary>
internal enum CodeCheck
{
    Valid,
    Mismatch,
    Expired,
    AlreadyUsed,
    TooManyAttempts,
}

/// <summary>
/// The sign-in code rules (design 6.2): six digits, valid for ten minutes, five attempts, single use. Codes are stored
/// only as a hash salted with the code's own id.
/// </summary>
internal static class SignInCodeRules
{
    public const int Digits = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>At most this many codes are emailed to one account per <see cref="IssueWindow"/>; more are silently dropped.</summary>
    public const int MaxCodesPerWindow = 5;
    public static readonly TimeSpan IssueWindow = TimeSpan.FromHours(1);

    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static byte[] Hash(Guid codeId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{codeId:N}:{code}"));

    public static bool MayIssue(int codesIssuedInWindow) => codesIssuedInWindow < MaxCodesPerWindow;

    /// <summary>
    /// Checks <paramref name="input"/> against <paramref name="code"/> at <paramref name="now"/>. A mismatch counts as an
    /// attempt (the caller increments <see cref="SignInCode.FailedAttempts"/>); a used, expired or exhausted code never
    /// matches, even with the right digits.
    /// </summary>
    public static CodeCheck Check(SignInCode code, string input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.ConsumedAt is not null)
        {
            return CodeCheck.AlreadyUsed;
        }

        if (now >= code.ExpiresAt)
        {
            return CodeCheck.Expired;
        }

        if (code.FailedAttempts >= MaxAttempts)
        {
            return CodeCheck.TooManyAttempts;
        }

        var expected = code.CodeHash;
        var actual = Hash(code.Id, input ?? "");
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? CodeCheck.Valid : CodeCheck.Mismatch;
    }
}
