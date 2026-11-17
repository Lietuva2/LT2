using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
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
    SignIn.KnownBrowsers browsers,
    ILogger<StaffHandlers> logger)
{
    internal static readonly Error InvalidCode = Error.Validation("role-activation.invalid", "code");
    internal static readonly Error PasskeySessionRequired = Error.Forbidden("role-activation.passkey-required");
    internal static readonly Error StaffPasskeyRequired = Error.Forbidden("passkey.staff-required");

    /// <summary>A passkey of another account offered to make this account's session a passkey session (owner, 2026-11-17).</summary>
    internal static readonly Error OtherAccountsPasskey = Error.Forbidden("passkey.other-account");

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

        // The sessions signed in with the removed passkeys end with them (design 6.8).
        var sessionsEnded = await db.Sessions
            .Where(s => s.UserId == state.UserId && s.PasskeyId != null && s.PasskeyId != passkeyId)
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
                new { count = removed, reason = "role-activation", sessionsEnded },
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

        // The new signature counter must be stored, or the confirmation does not count (as at sign-in).
        if (!(await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey)).Succeeded)
        {
            return Lt2.Identity.Application.Passkeys.PasskeyHandlers.InvalidPasskey;
        }

        var now = clock.GetUtcNow();
        await db.Sessions
            .Where(s => s.Id == state.Session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PasskeyConfirmedAt, now), cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Design 6.2 (owner, 2026-11-16): a code session on a staff account becomes a staff session by an assertion with the
    /// account's staff passkey, without signing out. Only the signed-in account's own staff passkey counts: another account's
    /// passkey, a passkey of this account that is not the staff passkey, and an account without an activated staff role are
    /// all refused with <see cref="StaffPasskeyRequired"/> and change nothing. On success the session ends and a new one is
    /// signed in with a new key (<see cref="SessionRotation"/>), as a passkey session just confirmed; audited, no email.
    /// </summary>
    public Task<Result<Unit>> StartStaffSessionAsync(ClaimsPrincipal principal, string credentialJson, string? ipHash, CancellationToken cancellationToken) =>
        StartAsync(principal, credentialJson, ipHash, staffOnly: true, cancellationToken);

    /// <summary>
    /// Owner, 2026-11-17 ("Patvirtinti prieigos raktu" in place): the signed-in account's session becomes a passkey session by an
    /// assertion with any of the account's own passkeys, as signing in with it would, but without leaving the page; with the
    /// staff passkey of an account with an activated staff role it is a staff session too. Another account's passkey is
    /// refused with <see cref="OtherAccountsPasskey"/> and changes nothing (the browser stays signed in to its own account); an
    /// assertion that fails, with <c>passkey.invalid</c>. On success as <see cref="StartStaffSessionAsync"/>: a new key, the
    /// passkey just confirmed, audited (<c>passkey.session-started</c>, or <c>staff.session-started</c> for the staff passkey),
    /// no email.
    /// </summary>
    public Task<Result<Unit>> StartPasskeySessionAsync(ClaimsPrincipal principal, string credentialJson, string? ipHash, CancellationToken cancellationToken) =>
        StartAsync(principal, credentialJson, ipHash, staffOnly: false, cancellationToken);

    private async Task<Result<Unit>> StartAsync(ClaimsPrincipal principal, string credentialJson, string? ipHash, bool staffOnly, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state?.Profile is null || state.Session is null)
        {
            return Error.Unauthorized("auth.required");
        }

        var staffPasskey = state.AccountHasActiveStaffRole ? state.Profile.StaffPasskeyId : null;
        if (staffOnly && staffPasskey is null)
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
            logger.LogInformation(ex, "Passkey session without a pending challenge");
            return Lt2.Identity.Application.Passkeys.PasskeyHandlers.InvalidPasskey;
        }

        var isStaffPasskey = assertion.Succeeded && staffPasskey is not null && assertion.Passkey.CredentialId.AsSpan().SequenceEqual(staffPasskey);
        if (!assertion.Succeeded || !await signIn.CanSignInAsync(assertion.User))
        {
            logger.LogInformation("Passkey session refused: {Reason}", assertion.Failure?.Message ?? "the account cannot sign in");
            return staffOnly ? StaffPasskeyRequired : Lt2.Identity.Application.Passkeys.PasskeyHandlers.InvalidPasskey;
        }

        if (assertion.User.Id != state.UserId || (staffOnly && !isStaffPasskey))
        {
            logger.LogInformation("Passkey session refused: {Reason}", assertion.User.Id != state.UserId ? "another account's passkey" : "not this account's staff passkey");
            return staffOnly ? StaffPasskeyRequired : OtherAccountsPasskey;
        }

        // The new signature counter must be stored, or the assertion does not count (as at sign-in).
        if (!(await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey)).Succeeded)
        {
            return Lt2.Identity.Application.Passkeys.PasskeyHandlers.InvalidPasskey;
        }

        var used = assertion.Passkey.CredentialId;
        var role = isStaffPasskey ? (state.AccountActiveRoles.Contains(Roles.Administrator) ? Roles.Administrator : state.AccountActiveRoles[0]) : Roles.Member;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await events.RecordAsync(
                transaction.GetDbTransaction(),
                isStaffPasskey ? "staff.session-started" : "passkey.session-started",
                state.UserId,
                new Actor(state.UserId, role, ipHash),
                isStaffPasskey
                    ? (object)new { roles = state.AccountActiveRoles, credential = WebEncoders.Base64UrlEncode(used)[..12] }
                    : new { credential = WebEncoders.Base64UrlEncode(used)[..12] },
                null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // A new session key on the change of privilege: the current row is deleted and a passkey session stored, whose passkey
        // counts as just confirmed (SessionTicketStore.StoreAsync). The sessions list shows one session for this browser.
        await SessionSignIn.SignInAsync(signIn, assertion.User, AuthenticationMethods.Passkey, used);

        // Design 8.20.2: the browser stays known to the account, renewed as at every sign-in.
        await browsers.RememberAsync(state.UserId, CancellationToken.None);
        logger.LogInformation("Passkey session started for account {UserId}; staff: {Staff}", state.UserId, isStaffPasskey);
        return Result.Ok();
    }

    public async Task<StaffSession> SessionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        (await access.ForAsync(principal, cancellationToken))?.ToSession() ?? StaffSession.None;

    private IQueryable<IdentityUserPasskey<Guid>> Passkeys(Guid userId) =>
        db.Set<IdentityUserPasskey<Guid>>().Where(p => p.UserId == userId);
}
