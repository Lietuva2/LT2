using System.Security.Cryptography;
using System.Text;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application;

/// <summary>
/// The server's key of the proof of work's signatures (design 6.4): a secret file mounted into the web container
/// (<c>deploy/secrets/bot_check_key</c>, read as <c>Security:BotCheckKey</c>). Without one the host makes a random key of its own
/// for its lifetime and says so at start: a challenge then holds only on this instance and until it restarts, which a browser
/// meets as a refusal and answers with a new challenge, so one instance works; several need the shared file.
/// </summary>
internal sealed class BotCheckKey(IConfiguration configuration)
{
    public const string ConfigurationKey = "Security:BotCheckKey";

    private readonly byte[]? _configured = configuration[ConfigurationKey] is { Length: > 0 } key && key.Trim() is { Length: > 0 } trimmed
        ? Encoding.UTF8.GetBytes(trimmed)
        : null;

    private readonly byte[] _own = RandomNumberGenerator.GetBytes(32);

    public bool Configured => _configured is not null;

    public byte[] Bytes => _configured ?? _own;
}

/// <summary>
/// Issues and checks the proof of work of registration and code requests (design 6.2, 6.4). A challenge is stateless: signed
/// with <see cref="BotCheckKey"/>, nothing kept when it is handed out. A solution is used once: its challenge's signature is
/// written to <c>identity.bot_check_use</c> with the challenge's expiry, and the same signature is refused until housekeeping
/// deletes the row after that expiry. While <c>BotCheck:Enabled</c> is off nothing is issued and everything is accepted.
/// The check says only that it passed or not: it is made before anything about the address is looked up, and a refusal never
/// says which of the checks failed (the reason is logged, without the solution).
/// </summary>
internal sealed class BotCheck(IdentityDb db, ISettings settings, BotCheckKey key, TimeProvider clock, ILogger<BotCheck> logger)
{
    /// <summary>The one answer to a missing, wrong, expired or used solution; it says nothing about the address.</summary>
    internal static readonly Error Failed = Error.Validation("bot-check.failed");

    /// <summary>A new challenge, or null while the check is off.</summary>
    public async Task<BotChallenge?> NewChallengeAsync(CancellationToken cancellationToken)
    {
        if (!await settings.GetAsync(SettingKeys.BotCheckEnabled, cancellationToken))
        {
            return null;
        }

        var maxNumber = await settings.GetAsync(SettingKeys.BotCheckMaxNumber, cancellationToken);
        var minutes = await settings.GetAsync(SettingKeys.BotCheckChallengeMinutes, cancellationToken);
        return BotCheckRules.NewChallenge(key.Bytes, maxNumber, clock.GetUtcNow().AddMinutes(minutes));
    }

    /// <summary>Null when the request may go on; <see cref="Failed"/> otherwise.</summary>
    public async Task<Error?> CheckAsync(string? solution, CancellationToken cancellationToken)
    {
        if (!await settings.GetAsync(SettingKeys.BotCheckEnabled, cancellationToken))
        {
            return null;
        }

        var outcome = BotCheckRules.Check(solution, key.Bytes, clock.GetUtcNow(), out var signature, out var expires);
        if (outcome != BotCheckOutcome.Accepted)
        {
            logger.LogInformation("Bot check refused: {Outcome}", outcome);
            return Failed;
        }

        // Used once: the first insert of the signature wins, in any web instance; a second one inserts nothing.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO identity.bot_check_use (signature, expires_at) VALUES ({signature}, {expires}) ON CONFLICT (signature) DO NOTHING",
            cancellationToken);
        if (inserted == 0)
        {
            logger.LogInformation("Bot check refused: used");
            return Failed;
        }

        return null;
    }
}
