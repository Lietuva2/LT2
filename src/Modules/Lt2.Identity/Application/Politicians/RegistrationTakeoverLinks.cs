using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application.Politicians;

/// <summary>
/// Design 8.5.2, "Registering with the official address is the take-over": a registration of a pending account whose address is
/// the official address of an MP nobody has taken over gets the take-over link in place of the six-digit code, so one email and
/// one page finish the registration and the take-over. Limited as sign-in codes are (the same two settings, counted over these
/// links of the account); a new one replaces only the account's own earlier one, never a link sent by "Perimti profilį", and is
/// not counted with them. Nothing logs the address or the token.
/// </summary>
internal sealed class RegistrationTakeoverLinks(
    IdentityDb db,
    IMpDirectory mps,
    IOutbox outbox,
    ISettings settings,
    TimeProvider clock,
    ILogger<RegistrationTakeoverLinks> logger)
{
    /// <summary>
    /// Sends the take-over link instead of the code when the address is an MP's official address and the profile is free; false
    /// when it is not (the caller sends the ordinary code). Must be called inside <paramref name="transaction"/>, so the token and
    /// its email commit together. Beyond the limits nothing is sent, and the answer is true all the same, as for a code.
    /// </summary>
    public async Task<bool> TryIssueAsync(User user, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        if (await Staff.AccountLocks.IsLockedAsync(db, user.Id, cancellationToken))
        {
            return false;
        }

        var mp = await mps.OfficialAddressOwnerAsync(user.Email!, cancellationToken);
        if (mp is null)
        {
            return false;
        }

        await IdentityLocks.MpAsync(db, mp.SeimasPersonId, cancellationToken);
        if (await PoliticianAccess.OpenLinks(db).AnyAsync(l => l.SeimasPersonId == mp.SeimasPersonId && l.Part == PoliticianParts.Mp, cancellationToken)
            || await PoliticianAccess.OpenLinks(db).AnyAsync(l => l.UserId == user.Id, cancellationToken))
        {
            return false;
        }

        // The sign-in codes' limits, counted under the same lock of the account, over the links sent with its registrations.
        await IdentityLocks.SignInCodesAsync(db, user.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var dayAgo = now - SignInCodeRules.Day;
        var hourAgo = now - SignInCodeRules.Hour;
        var sent = db.PoliticianTokens.Where(t => t.Kind == PoliticianTokenKinds.Registration && t.UserId == user.Id);
        var limits = new CodeLimits(
            await settings.GetAsync(SettingKeys.SignInCodesPerHour, cancellationToken),
            await settings.GetAsync(SettingKeys.SignInCodesPerDay, cancellationToken));
        if (!SignInCodeRules.MayIssue(
                await sent.CountAsync(t => t.CreatedAt > hourAgo, cancellationToken),
                await sent.CountAsync(t => t.CreatedAt > dayAgo, cancellationToken),
                limits))
        {
            logger.LogWarning("Registration take-over link limit reached for account {UserId}; no email sent", user.Id);
            return true;
        }

        // A new one replaces only the account's own earlier one: a link sent by "Perimti profilį" stays.
        await sent.Where(t => t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);
        var token = OneTimeCode.New();
        db.PoliticianTokens.Add(new PoliticianToken
        {
            Id = Ids.New(clock),
            Kind = PoliticianTokenKinds.Registration,
            SeimasPersonId = mp.SeimasPersonId,
            UserId = user.Id,
            TokenHash = OneTimeCode.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + PoliticianRules.TakeoverLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        await outbox.EnqueueAsync(
            OutboxMessage.Create(
                MessageKinds.MpTakeoverLinkEmail,
                new MpTakeoverLinkEmail(
                    user.Email!,
                    mp.FirstName + " " + mp.LastName,
                    PoliticianHandlers.TakeoverPage + "#" + token,
                    (int)PoliticianRules.TakeoverLifetime.TotalHours,
                    PoliticianHandlers.TakeoverPage,
                    token,
                    AtRegistration: true)),
            transaction.GetDbTransaction(),
            cancellationToken);
        return true;
    }
}
