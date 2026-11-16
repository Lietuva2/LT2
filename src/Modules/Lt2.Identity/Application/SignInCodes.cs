using Lt2.Identity.Application.SignIn;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application;

/// <summary>
/// Issues sign-in codes (design 6.2): applies the per-address limits (an hour and a day, both settings), replaces any
/// earlier code, and writes the email to the outbox. A code belongs to the address and works in any browser.
/// A browser known to the account (design 8.20.2) has its own allowance instead: the same two settings, counted for it alone,
/// and a code it asks for replaces only its own earlier one and is accepted only from it.
/// </summary>
internal sealed class SignInCodeIssuer(IdentityDb db, IOutbox outbox, ISettings settings, KnownBrowsers browsers, TimeProvider clock, ILogger<SignInCodeIssuer> logger)
{
    /// <summary>Must be called inside <paramref name="transaction"/>, so the code and its email commit together.</summary>
    public async Task IssueAsync(User user, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        // The limits are counted under the account's lock, so parallel requests cannot pass them together (design 6.8).
        await IdentityLocks.SignInCodesAsync(db, user.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var hourAgo = now - SignInCodeRules.Hour;
        var dayAgo = now - SignInCodeRules.Day;
        var browser = await browsers.RecognisedAsync(user.Id, cancellationToken);
        var limits = new CodeLimits(
            await settings.GetAsync(SettingKeys.SignInCodesPerHour, cancellationToken),
            await settings.GetAsync(SettingKeys.SignInCodesPerDay, cancellationToken));

        // The same two limits for every allowance, counted separately: the codes of the browsers the account does not know
        // (design 6.2), or those of this known browser alone (design 8.20.2). Nothing asked for elsewhere counts against it.
        var inLastDay = await db.SignInCodes.CountAsync(c => c.UserId == user.Id && c.KnownDeviceId == browser && c.CreatedAt > dayAgo, cancellationToken);
        var inLastHour = await db.SignInCodes.CountAsync(c => c.UserId == user.Id && c.KnownDeviceId == browser && c.CreatedAt > hourAgo, cancellationToken);
        var allowed = browser is null
            ? SignInCodeRules.MayIssue(inLastHour, inLastDay, limits)
            : KnownBrowserRules.MayIssue(inLastHour, inLastDay, limits);
        if (!allowed)
        {
            logger.LogWarning("Sign-in code limit reached for account {UserId}; no email sent", user.Id);
            return;
        }

        // A new code replaces the earlier one of the same allowance: a request from elsewhere never cancels a known browser's code.
        await db.SignInCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null && c.KnownDeviceId == browser)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        var id = Ids.New(clock);
        var code = SignInCodeRules.Generate();
        db.SignInCodes.Add(new SignInCode
        {
            Id = id,
            UserId = user.Id,
            CodeHash = SignInCodeRules.Hash(id, code),
            CreatedAt = now,
            ExpiresAt = now + SignInCodeRules.Lifetime,
            KnownDeviceId = browser,
        });
        await db.SaveChangesAsync(cancellationToken);

        var email = new SignInCodeEmail(user.Email!, code, (int)SignInCodeRules.Lifetime.TotalMinutes);
        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.SignInCodeEmail, email),
            transaction.GetDbTransaction(),
            cancellationToken);
    }
}
