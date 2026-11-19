using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lt2.Identity.Domain;

/// <summary>Why a solution was refused; <see cref="Accepted"/> is the only one that lets the request go on.</summary>
internal enum BotCheckOutcome
{
    Accepted,
    Malformed,
    BadSignature,
    Expired,
    WrongNumber,
}

/// <summary>
/// The proof of work of registration and code requests (design 6.4), in the ALTCHA protocol and written here: a signed
/// challenge, a number found by trying, and the checks. All pure; the one thing that needs the database, that a challenge is
/// used once, is the service's (<c>BotCheck</c>).
/// A challenge is a random salt with its expiry, <c>SHA-256(salt + number)</c> for a number up to the setting's maximum, and the
/// server's HMAC of that hash, so the server keeps nothing for a challenge it has handed out and still knows it is its own.
/// </summary>
internal static class BotCheckRules
{
    public const string Algorithm = "SHA-256";

    /// <summary>The longest solution the server reads (its base64 text): a real one is about 250 characters.</summary>
    public const int MaxSolutionLength = 1024;

    /// <summary>A new challenge for <paramref name="number"/> (the secret, 0 to <paramref name="maxNumber"/>) that is good until <paramref name="expires"/>.</summary>
    public static BotChallenge NewChallenge(byte[] key, int maxNumber, DateTimeOffset expires, int number, byte[] saltBytes)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(saltBytes);
        var salt = Convert.ToHexStringLower(saltBytes) + "?expires=" + expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var challenge = ChallengeOf(salt, number);
        return new BotChallenge(Algorithm, challenge, maxNumber, salt, SignatureOf(key, challenge));
    }

    /// <summary>A new challenge with a random number and salt.</summary>
    public static BotChallenge NewChallenge(byte[] key, int maxNumber, DateTimeOffset expires) =>
        NewChallenge(key, maxNumber, expires, RandomNumberGenerator.GetInt32(maxNumber + 1), RandomNumberGenerator.GetBytes(12));

    /// <summary>What the browser sends: the challenge's fields and the number found, as base64 of JSON (ALTCHA's payload).</summary>
    public static string Payload(BotChallenge challenge, int number)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var json = JsonSerializer.Serialize(new Solution
        {
            Algorithm = challenge.Algorithm,
            Challenge = challenge.Challenge,
            Number = number,
            Salt = challenge.Salt,
            Signature = challenge.Signature,
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Finds the number the way a browser does (tests, and a measure of the work); null when there is none up to the challenge's maximum.</summary>
    public static int? Solve(BotChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        for (var n = 0; n <= challenge.MaxNumber; n++)
        {
            if (string.Equals(ChallengeOf(challenge.Salt, n), challenge.Challenge, StringComparison.Ordinal))
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a solution: well formed and no longer than <see cref="MaxSolutionLength"/>, the signature is the server's over the
    /// challenge, the expiry in the salt has not passed, and the number solves it. The signature is checked before the rest, so a
    /// forged challenge costs one HMAC. <paramref name="signature"/> is the challenge's, to be kept until <paramref name="expires"/> so that it is used once.
    /// </summary>
    public static BotCheckOutcome Check(string? payload, byte[] key, DateTimeOffset now, out string? signature, out DateTimeOffset expires)
    {
        ArgumentNullException.ThrowIfNull(key);
        signature = null;
        expires = default;
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxSolutionLength)
        {
            return BotCheckOutcome.Malformed;
        }

        Solution? solution;
        try
        {
            solution = JsonSerializer.Deserialize<Solution>(Convert.FromBase64String(payload.Trim()));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return BotCheckOutcome.Malformed;
        }

        if (solution is not { Algorithm: Algorithm, Challenge: { Length: 64 }, Signature: { Length: 64 }, Salt: { Length: > 0 and <= 128 }, Number: >= 0 }
            || ExpiryOf(solution.Salt) is not { } expiry)
        {
            return BotCheckOutcome.Malformed;
        }

        var number = solution.Number.Value;

        // Compared as bytes in constant time; a signature that is not hex is no signature.
        if (!TryFromHex(solution.Signature, out var given)
            || !CryptographicOperations.FixedTimeEquals(given, HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(solution.Challenge))))
        {
            return BotCheckOutcome.BadSignature;
        }

        if (expiry <= now)
        {
            return BotCheckOutcome.Expired;
        }

        if (!string.Equals(ChallengeOf(solution.Salt, number), solution.Challenge, StringComparison.Ordinal))
        {
            return BotCheckOutcome.WrongNumber;
        }

        signature = solution.Signature.ToLowerInvariant();
        expires = expiry;
        return BotCheckOutcome.Accepted;
    }

    /// <summary>The expiry written in a salt (<c>?expires=</c>, Unix seconds), or null when there is none.</summary>
    public static DateTimeOffset? ExpiryOf(string salt)
    {
        ArgumentNullException.ThrowIfNull(salt);
        const string marker = "?expires=";
        var at = salt.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var rest = salt[(at + marker.Length)..];
        var end = rest.IndexOf('&', StringComparison.Ordinal);
        var seconds = end < 0 ? rest : rest[..end];
        return long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) && unix is > 0 and < 253_402_300_799
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;
    }

    private static string ChallengeOf(string salt, int number) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + number.ToString(CultureInfo.InvariantCulture))));

    private static string SignatureOf(byte[] key, string challenge) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(challenge)));

    private static bool TryFromHex(string hex, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Solution
    {
        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; init; }

        [JsonPropertyName("challenge")]
        public string? Challenge { get; init; }

        [JsonPropertyName("number")]
        public int? Number { get; init; }

        [JsonPropertyName("salt")]
        public string? Salt { get; init; }

        [JsonPropertyName("signature")]
        public string? Signature { get; init; }
    }
}
