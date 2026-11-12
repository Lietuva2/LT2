using System.Security.Cryptography;
using System.Text;

namespace Lt2.Identity.Domain;

/// <summary>Outcome of checking a role activation code.</summary>
internal enum ActivationCheck
{
    Valid,
    Mismatch,
    Expired,
    AlreadyUsed,
}

/// <summary>
/// Staff rules (design 6.2, decided 2026-11-12): a staff role works only in a session signed in with the account's staff
/// passkey. An administrator's grant creates a one-time activation code, shown once and passed on directly (never by
/// email), valid for 24 hours; using it in a passkey session makes that passkey the staff passkey. Afterwards, changing
/// passkeys and sensitive actions need a fresh passkey confirmation, within the last ten minutes.
/// </summary>
internal static class StaffRules
{
    public static readonly TimeSpan ActivationLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan FreshConfirmation = TimeSpan.FromMinutes(10);

    /// <summary>Crockford's base 32: no I, L, O or U, so the code survives being read out or copied by hand.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Sixteen characters (80 bits) in groups of four, for example <c>7KQ2-M9XD-4TRB-HC0P</c>.</summary>
    public const int ActivationCodeLength = 16;

    public static string GenerateActivationCode()
    {
        var chars = new char[ActivationCodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var raw = new string(chars);
        return string.Join('-', Enumerable.Range(0, ActivationCodeLength / 4).Select(g => raw.Substring(g * 4, 4)));
    }

    /// <summary>Upper case, without spaces or dashes, with the letters people confuse mapped as Crockford does.</summary>
    public static string NormalizeActivationCode(string? input) =>
        new((input ?? "")
            .ToUpperInvariant()
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })
            .ToArray());

    public static byte[] HashActivationCode(Guid activationId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{activationId:N}:{NormalizeActivationCode(code)}"));

    public static ActivationCheck Check(RoleActivation activation, string input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activation);
        var matches = CryptographicOperations.FixedTimeEquals(activation.CodeHash, HashActivationCode(activation.Id, input));
        if (!matches)
        {
            return ActivationCheck.Mismatch;
        }

        if (activation.UsedAt is not null)
        {
            return ActivationCheck.AlreadyUsed;
        }

        return now >= activation.ExpiresAt ? ActivationCheck.Expired : ActivationCheck.Valid;
    }

    /// <summary>Whether a passkey confirmation at <paramref name="confirmedAt"/> still counts as fresh at <paramref name="now"/>.</summary>
    public static bool IsFresh(DateTimeOffset? confirmedAt, DateTimeOffset now) =>
        confirmedAt is { } at && now - at < FreshConfirmation && at <= now;
}
