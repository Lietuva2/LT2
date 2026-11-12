using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application.Staff;

/// <summary>
/// The person's side of the staff rules (design 6.2): using an activation code in a passkey session, and the fresh
/// passkey confirmation that passkey changes and sensitive actions need.
/// </summary>
internal sealed class StaffHandlers(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    StaffAccess access,
    AccountEvents events,
    TimeProvider clock,
    ILogger<StaffHandlers> logger)
{
    internal static readonly Error InvalidCode = Error.Validation("role-activation.invalid", "code");
    internal static readonly Error PasskeySessionRequired = Error.Forbidden("role-activation.passkey-required");
    internal static readonly Error StaffPasskeyRequired = Error.Forbidden("passkey.staff-required");

    public async Task<Result<Unit>> ActivateAsync(ClaimsPrincipal principal, string code, string? ipHash, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state?.Profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        // The passkey of this session becomes the staff passkey, so the session must be a passkey session, and the
        // passkey must still be on the account.
        if (state.Session?.PasskeyId is not { } passkeyId
            || !await Passkeys(state.UserId).AnyAsync(p => p.CredentialId == passkeyId, cancellationToken))
        {
            return PasskeySessionRequired;
        }

        var now = clock.GetUtcNow();
        var open = await db.RoleActivations.AsNoTracking()
            .Where(a => a.UserId == state.UserId && a.UsedAt == null)
            .ToListAsync(cancellationToken);
        var activation = open.FirstOrDefault(a => StaffRules.Check(a, code, now) == ActivationCheck.Valid);
        if (activation is null)
        {
            return InvalidCode;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var used = await db.RoleActivations
            .Where(a => a.Id == activation.Id && a.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UsedAt, now), cancellationToken);
        if (used == 0)
        {
            return InvalidCode;
        }

        await db.Profiles
            .Where(p => p.UserId == state.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StaffPasskeyId, passkeyId), cancellationToken);
        var removed = await Passkeys(state.UserId)
            .Where(p => p.CredentialId != passkeyId)
            .ExecuteDeleteAsync(cancellationToken);

        var email = (await users.FindByIdAsync(state.UserId.ToString()))!.Email!;
        var actor = new Actor(state.UserId, activation.Role, ipHash);
        await events.RecordAsync(
            transaction.GetDbTransaction(),
            "role.activated",
            state.UserId,
            actor,
            new { role = activation.Role, passkeysRemoved = removed },
            new AccountNoticeEmail(email, AccountNotices.RoleActivated, activation.Role, removed),
            cancellationToken);
        if (removed > 0)
        {
            await events.RecordAsync(
                transaction.GetDbTransaction(),
                "passkey.removed",
                state.UserId,
                actor,
                new { count = removed, reason = "role-activation" },
                null,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Role {Role} activated for account {UserId}; {Removed} other passkeys removed", activation.Role, state.UserId, removed);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ConfirmPasskeyAsync(ClaimsPrincipal principal, string credentialJson, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state?.Profile is null || state.Session is null)
        {
            return Error.Unauthorized("auth.required");
        }

        if (!state.StaffPasskeySession)
        {
            return StaffPasskeyRequired;
        }

        PasskeyAssertionResult<User> assertion;
        try
        {
            assertion = await signIn.PerformPasskeyAssertionAsync(credentialJson);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "Passkey confirmation without a pending challenge");
            return Lt2.Identity.Application.Passkeys.PasskeyHandlers.InvalidPasskey;
        }

        if (!assertion.Succeeded
            || assertion.User.Id != state.UserId
            || !assertion.Passkey.CredentialId.AsSpan().SequenceEqual(state.Profile.StaffPasskeyId))
        {
            logger.LogInformation("Passkey confirmation refused: {Reason}", assertion.Failure?.Message ?? "not the staff passkey");
            return StaffPasskeyRequired;
        }

        await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        var now = clock.GetUtcNow();
        await db.Sessions
            .Where(s => s.Id == state.Session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PasskeyConfirmedAt, now), cancellationToken);
        return Result.Ok();
    }

    public async Task<StaffSession> SessionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        (await access.ForAsync(principal, cancellationToken))?.ToSession() ?? StaffSession.None;

    private IQueryable<IdentityUserPasskey<Guid>> Passkeys(Guid userId) =>
        db.Set<IdentityUserPasskey<Guid>>().Where(p => p.UserId == userId);
}
