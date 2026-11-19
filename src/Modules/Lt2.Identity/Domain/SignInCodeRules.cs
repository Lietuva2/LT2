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

/// <summary>How many codes one address may be sent (design 6.2; both are settings).</summary>
internal sealed record CodeLimits(int PerHour, int PerDay);

/// <summary>
/// The sign-in code rules (design 6.2): six digits, valid for ten minutes, five attempts, single use. A code belongs to
/// the address, not to a browser, and a new code replaces the earlier one. Codes are stored only as a hash salted with
/// the code's own id.
/// </summary>
internal static class SignInCodeRules
{
    public const int Digits = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>The two windows the limits count over.</summary>
    public static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static byte[] Hash(Guid codeId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{codeId:N}:{code}"));

    /// <summary>
    /// Whether another code may be emailed to the address, given how many were issued in the last hour and the last day.
    /// Beyond either limit nothing is sent, and nothing in the answer says so. The daily limit is what bounds guessing:
    /// with five attempts per code, at most <c>5 × PerDay</c> tries a day against one account.
    /// </summary>
    public static bool MayIssue(int issuedInLastHour, int issuedInLastDay, CodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return issuedInLastHour < limits.PerHour && issuedInLastDay < limits.PerDay;
    }

    /// <summary>The most guesses a day the limits allow against one account.</summary>
    public static int MaxGuessesPerDay(CodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return MaxAttempts * limits.PerDay;
    }

    /// <summary>
    /// Checks <paramref name="input"/> against <paramref name="code"/> at <paramref name="now"/>. A mismatch counts as an
    /// attempt (the caller increments <see cref="SignInCode.FailedAttempts"/>); a used, expired or exhausted code never
    /// matches, even with the right digits.
    /// </summary>
    public static CodeCheck Check(SignInCode code, string input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Check(code.Id, code.CodeHash, code.ExpiresAt, code.FailedAttempts, code.ConsumedAt, input, now);
    }

    /// <summary>
    /// The same check for any code kept as these rules keep one (design 6.2: a code to a new address follows the rules of a
    /// sign-in code): a code with no hash never matches.
    /// </summary>
    public static CodeCheck Check(Guid id, byte[]? hash, DateTimeOffset expiresAt, int failedAttempts, DateTimeOffset? consumedAt, string input, DateTimeOffset now)
    {
        if (consumedAt is not null || hash is null)
        {
            return CodeCheck.AlreadyUsed;
        }

        if (now >= expiresAt)
        {
            return CodeCheck.Expired;
        }

        if (failedAttempts >= MaxAttempts)
        {
            return CodeCheck.TooManyAttempts;
        }

        var actual = Hash(id, input ?? "");
        return CryptographicOperations.FixedTimeEquals(hash, actual) ? CodeCheck.Valid : CodeCheck.Mismatch;
    }
}
