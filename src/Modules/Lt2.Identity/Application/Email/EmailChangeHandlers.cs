using System.Security.Claims;
using System.Text.Json;
using Lt2.Identity.Application.Passkeys;
using Lt2.Identity.Application.Politicians;
using Lt2.Identity.Application.SignIn;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lt2.Identity.Application.Email;

/// <summary>
/// Changing the account's email address (design 6.2). An account has one address, unique once normalised. The change starts
/// only in a passkey session or after a code to the address in use (<see cref="EmailChangeRules.Proof"/>); the new address gets a
/// code with the rules of a sign-in code, counted against that address's limits under its lock; the answer is the same whether
/// the address is free or not, and an address held by another member's account is told so instead of getting a code. Typing
/// the code moves the account in one transaction: an account still pending on the address is deleted, the account's other
/// sessions and its open codes end, the old address gets the link back (seven days, its code kept as a hash), audited without
/// addresses. The link puts the old address back and ends every session; the address it replaced is told. Nothing here logs an
/// address or a code.
/// </summary>
internal sealed class EmailChangeHandlers(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    StaffAccess staff,
    PoliticianAccess politicians,
    SignInCodeIssuer codes,
    KnownBrowsers browsers,
    AccountEvents events,
    IAuditLog audit,
    IOutbox outbox,
    ISettings settings,
    TimeProvider clock,
    ILogger<EmailChangeHandlers> logger)
{
    /// <summary>A code session without the code to the address in use (design 6.2: a stolen session is not enough).</summary>
    internal static readonly Error ProofRequired = Error.Forbidden("email.proof-required");

    /// <summary>The new address is the one in use, once normalised.</summary>
    internal static readonly Error Same = Error.Validation("email.same", "email");

    /// <summary>The code to the address in use was wrong, used or expired: the sign-in code's one answer, under its own field.</summary>
    internal static readonly Error InvalidProofCode = Error.Validation("auth.code-invalid", "proofCode");

    /// <summary>The code to the new address was wrong, used or expired, or the change can no longer be made: one answer.</summary>
    internal static readonly Error InvalidCode = CodeSignInHandler.InvalidCode;

    /// <summary>A link back that is unknown, used, expired, or whose address meanwhile belongs to another account: one answer.</summary>
    internal static readonly Error RestoreInvalid = Error.Validation("email.restore-invalid", "token");

    private static readonly Error MemberRequired = Error.Forbidden("member.required");

    /// <summary>
    /// "Siųsti kodą dabartiniu adresu": a code to the address in use, with the sign-in code's limits (it is the account's code like
    /// any other, in other words). Refused, without an email, where a code cannot prove it (a staff account, the MP's account).
    /// </summary>
    public async Task<Result<Unit>> SendProofCodeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (await MemberAsync(principal, cancellationToken) is not { } user)
        {
            return MemberRequired;
        }

        var (_, refused, _) = await ProofAsync(principal, codeGiven: true, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await codes.IssueAsync(user, transaction, cancellationToken, SignInCodePurposes.EmailChange);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The new address typed, with the proof (a passkey session, or <paramref name="proofCode"/> sent to the address in use): a
    /// code goes to the new address, or, when another member's account has it, an email to that account that nothing changed.
    /// The answer is the same either way, and the same when the address's limits are used up and nothing is sent.
    /// </summary>
    public async Task<Result<Unit>> ChangeAsync(ClaimsPrincipal principal, string email, string? proofCode, CancellationToken cancellationToken)
    {
        if (await MemberAsync(principal, cancellationToken) is not { } user)
        {
            return MemberRequired;
        }

        var normalized = EmailAddress.Normalize(email);
        if (normalized is null)
        {
            return Error.Validation("email.invalid", "email");
        }

        if (DisposableDomains.Contains(normalized))
        {
            return Error.Validation("email.disposable", "email");
        }

        if (EmailAddress.Same(user.Email, email))
        {
            return Same;
        }

        var (proof, refusal, sessionId) = await ProofAsync(principal, !string.IsNullOrWhiteSpace(proofCode), cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var now = clock.GetUtcNow();
        if (proof == EmailChangeProof.Code && !await SignInCodeUse.ConsumeAsync(db, browsers, user.Id, proofCode!, now, cancellationToken))
        {
            return InvalidProofCode;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The address's limits are counted under its lock, so parallel requests cannot pass them together (design 6.8): the
        // sign-in code's two settings, for every email of a change to this address, whichever account asked.
        await IdentityLocks.EmailAddressAsync(db, normalized, cancellationToken);
        var limits = new CodeLimits(
            await settings.GetAsync(SettingKeys.SignInCodesPerHour, cancellationToken),
            await settings.GetAsync(SettingKeys.SignInCodesPerDay, cancellationToken));
        var inLastHour = await db.EmailChanges.CountAsync(c => c.NormalizedEmail == normalized && c.CreatedAt > now - SignInCodeRules.Hour, cancellationToken);
        var inLastDay = await db.EmailChanges.CountAsync(c => c.NormalizedEmail == normalized && c.CreatedAt > now - SignInCodeRules.Day, cancellationToken);
        if (!SignInCodeRules.MayIssue(inLastHour, inLastDay, limits))
        {
            logger.LogWarning("Email change code limit reached for a new address of account {UserId}; no email sent", user.Id);
            await transaction.CommitAsync(cancellationToken);
            return Result.Ok();
        }

        // The newest replaces the earlier ones.
        await db.EmailChanges
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        // Another member's account has the address: that account is told and gets no code. An account still pending on it
        // (never proven) does not hold it: the code goes as to a free address (design 6.2).
        var holder = await db.Profiles.AsNoTracking()
            .Where(p => p.NormalizedEmail == normalized && p.UserId != user.Id && p.RegisteredAt != null)
            .Join(db.Users, p => p.UserId, u => u.Id, (_, u) => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        var id = Ids.New(clock);
        var code = holder is null ? SignInCodeRules.Generate() : null;
        db.EmailChanges.Add(new EmailChange
        {
            Id = id,
            UserId = user.Id,
            Email = email.Trim(),
            NormalizedEmail = normalized,
            CodeHash = code is null ? null : SignInCodeRules.Hash(id, code),
            SessionId = sessionId,
            Proof = proof == EmailChangeProof.Code ? "code" : "passkey",
            CreatedAt = now,
            ExpiresAt = now + SignInCodeRules.Lifetime,
            ConsumedAt = code is null ? now : null,
        });
        await db.SaveChangesAsync(cancellationToken);

        var message = holder is not null
            ? OutboxMessage.Create(MessageKinds.EmailChangeTakenEmail, new EmailChangeTakenEmail(holder))
            : OutboxMessage.Create(MessageKinds.EmailChangeCodeEmail, new EmailChangeCodeEmail(email.Trim(), code!, (int)SignInCodeRules.Lifetime.TotalMinutes));
        await outbox.EnqueueAsync(message, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The code from the new address, typed in the session that asked: the account moves to it in one transaction (see the class),
    /// then this session goes on under a new key. Answers the new address.
    /// </summary>
    public async Task<Result<string>> ConfirmAsync(ClaimsPrincipal principal, string code, string? ipHash, CancellationToken cancellationToken)
    {
        if (await MemberAsync(principal, cancellationToken) is not { } member)
        {
            return MemberRequired;
        }

        var now = clock.GetUtcNow();
        var current = Guid.TryParse(principal.FindFirstValue(SessionTicketStore.SessionIdClaim), out var sessionId) ? sessionId : Guid.Empty;
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == current && s.UserId == member.Id && s.ExpiresAt > now, cancellationToken);
        var change = await db.EmailChanges.AsNoTracking()
            .Where(c => c.UserId == member.Id && c.ConsumedAt == null && c.CodeHash != null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (session is null || change is null || change.SessionId != session.Id)
        {
            return InvalidCode;
        }

        var check = SignInCodeRules.Check(change.Id, change.CodeHash, change.ExpiresAt, change.FailedAttempts, change.ConsumedAt, code, now);
        if (check != CodeCheck.Valid)
        {
            if (check == CodeCheck.Mismatch)
            {
                await db.EmailChanges
                    .Where(c => c.Id == change.Id && c.FailedAttempts < SignInCodeRules.MaxAttempts)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancellationToken);
            }

            return InvalidCode;
        }

        string oldEmail;
        User user;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await IdentityLocks.EmailAddressAsync(db, change.NormalizedEmail, cancellationToken);
            var consumed = await db.EmailChanges
                .Where(c => c.Id == change.Id && c.ConsumedAt == null && c.FailedAttempts < SignInCodeRules.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
            if (consumed == 0)
            {
                return InvalidCode;
            }

            // A member's account took the address meanwhile: nothing changes, and the used code says no more than a wrong one.
            var (taken, pendingRemoved) = await FreeAddressAsync(change.NormalizedEmail, member.Id, transaction, cancellationToken);
            if (taken)
            {
                await transaction.CommitAsync(cancellationToken);
                return InvalidCode;
            }

            user = (await users.FindByIdAsync(member.Id.ToString()))!;
            oldEmail = user.Email!;
            if (!await MoveAsync(user, change.Email, change.NormalizedEmail, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return InvalidCode;
            }

            // Codes sent to the old address stop working, and so does any other code of a change.
            await EndCodesAsync(user.Id, now, cancellationToken);

            // The account's other sessions end (as when a passkey is removed); this one goes on under a new key below.
            var sessionsEnded = await db.Sessions.Where(s => s.UserId == user.Id && s.Id != session.Id).ExecuteDeleteAsync(cancellationToken);

            // The old address's link back: one use, seven days, its code kept only as a hash.
            var token = OneTimeCode.New();
            var expires = now + EmailChangeRules.RestoreLifetime;
            db.EmailRestores.Add(new EmailRestore
            {
                Id = Ids.New(clock),
                UserId = user.Id,
                Email = oldEmail,
                NormalizedEmail = EmailAddress.Normalize(oldEmail) ?? "",
                TokenHash = OneTimeCode.Hash(token),
                CreatedAt = now,
                ExpiresAt = expires,
            });
            await db.SaveChangesAsync(cancellationToken);

            await events.RecordAsync(
                transaction.GetDbTransaction(),
                "account.email-changed",
                user.Id,
                new Actor(user.Id, Roles.Member, ipHash),
                new { proof = change.Proof, sessionsEnded, pendingAccountRemoved = pendingRemoved },
                null,
                cancellationToken);
            await outbox.EnqueueAsync(
                OutboxMessage.Create(
                    MessageKinds.EmailChangedEmail,
                    new EmailChangedEmail(oldEmail, EmailAddress.Mask(change.Email), now, AccountScreens.EmailRestore + "#" + token, AccountScreens.EmailRestore, token, expires)),
                transaction.GetDbTransaction(),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // The session goes on under a new key, as what it was signed in as (design 6.2): its row is replaced, its cookie too.
        await SessionSignIn.SignInAsync(signIn, user, session.Method, session.PasskeyId);
        logger.LogInformation("Email address changed for account {UserId}", user.Id);
        return user.Email!;
    }

    /// <summary>
    /// "Ne jūs? Grąžinti ankstesnį adresą": the old address's link puts it back and ends every session of the account; the address
    /// it replaces is told. Needs no session and no accepted terms. A link unknown, malformed, used or expired, or whose address
    /// meanwhile belongs to another member's account, gets <see cref="RestoreInvalid"/> and changes nothing.
    /// </summary>
    public async Task<Result<Unit>> RestoreAsync(string token, string? ipHash, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = OneTimeCode.Hash(token);
        var restore = await db.EmailRestores.AsNoTracking().FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (restore is null || restore.UsedAt is not null || restore.ExpiresAt <= now)
        {
            return RestoreInvalid;
        }

        int sessionsEnded;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await IdentityLocks.EmailAddressAsync(db, restore.NormalizedEmail, cancellationToken);
            var used = await db.EmailRestores
                .Where(r => r.Id == restore.Id && r.UsedAt == null && r.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), cancellationToken);
            var (taken, pendingRemoved) = used == 0 ? (true, false) : await FreeAddressAsync(restore.NormalizedEmail, restore.UserId, transaction, cancellationToken);
            var user = taken ? null : await users.FindByIdAsync(restore.UserId.ToString());
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return RestoreInvalid;
            }

            var replaced = user.Email!;
            if (!await MoveAsync(user, restore.Email, restore.NormalizedEmail, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return RestoreInvalid;
            }

            // The account's other links back stop working, and so do its open codes; every session ends.
            await db.EmailRestores
                .Where(r => r.UserId == user.Id && r.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), cancellationToken);
            await EndCodesAsync(user.Id, now, cancellationToken);
            sessionsEnded = await db.Sessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);

            await audit.WriteAsync(
                new AuditEntry(
                    "account.email-restored",
                    null,
                    null,
                    user.Id.ToString(),
                    JsonSerializer.SerializeToElement(new { sessionsEnded, pendingAccountRemoved = pendingRemoved }, JsonSerializerOptions.Web),
                    ipHash),
                transaction.GetDbTransaction(),
                cancellationToken);
            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.EmailRestoredEmail, new EmailRestoredEmail(replaced, now)),
                transaction.GetDbTransaction(),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // A browser still holding one of the account's sessions is signed out (its row is gone already).
        if (signIn.Context.User.UserId() == restore.UserId)
        {
            await signIn.SignOutAsync();
        }

        logger.LogInformation("Email address put back for account {UserId}; {Sessions} sessions ended", restore.UserId, sessionsEnded);
        return Result.Ok();
    }

    /// <summary>The signed-in account when it is a member (a completed registration), or null.</summary>
    private async Task<User?> MemberAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId
            || !await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.RegisteredAt != null, cancellationToken))
        {
            return null;
        }

        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    /// <summary>The proof this session gives (<see cref="EmailChangeRules.Proof"/>), the refusal when it gives none, and its key.</summary>
    private async Task<(EmailChangeProof Proof, Error? Error, Guid? SessionId)> ProofAsync(ClaimsPrincipal principal, bool codeGiven, CancellationToken cancellationToken)
    {
        var state = await staff.ForAsync(principal, cancellationToken);
        var linked = await politicians.ForAsync(principal, cancellationToken);
        var proof = EmailChangeRules.Proof(
            state?.AccountHasActiveStaffRole == true,
            state?.StaffPasskeySession == true,
            state?.RecentlyConfirmed == true,
            linked?.IsMp == true,
            linked?.PasskeySession == true,
            codeGiven);
        Error? error = proof switch
        {
            EmailChangeProof.StaffPasskeyRequired => StaffHandlers.StaffPasskeyRequired,
            EmailChangeProof.ConfirmationRequired => PasskeyHandlers.ConfirmationRequired,
            EmailChangeProof.PasskeyRequired => PoliticianAccess.PasskeyRequired,
            EmailChangeProof.ProofRequired => ProofRequired,
            _ => null,
        };
        return (proof, error, state?.Session?.Id);
    }

    /// <summary>
    /// Under the address's lock, in the transaction: whether another member's account holds <paramref name="normalized"/>; an
    /// account only pending on it (never proven) holds nothing and is deleted, as it would be after seven days, with the emails
    /// to it that were not sent (design 6.2, 6.8).
    /// </summary>
    private async Task<(bool Taken, bool PendingRemoved)> FreeAddressAsync(string normalized, Guid userId, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var holder = await db.Profiles.AsNoTracking()
            .Where(p => p.NormalizedEmail == normalized && p.UserId != userId)
            .Select(p => new { p.UserId, p.RegisteredAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (holder is null)
        {
            return (false, false);
        }

        if (holder.RegisteredAt is not null)
        {
            return (true, false);
        }

        var address = await db.Users.AsNoTracking().Where(u => u.Id == holder.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        if (address is not null)
        {
            await outbox.ForgetAsync([address], transaction.GetDbTransaction(), cancellationToken);
        }

        await db.Users.Where(u => u.Id == holder.UserId).ExecuteDeleteAsync(cancellationToken);
        return (false, true);
    }

    /// <summary>
    /// The account's address becomes <paramref name="email"/> (proven), Identity's own fields with it, and its security stamp
    /// changes, so a session the rows did not end would end at its next check. False when the address was taken in the meantime
    /// by a registration that does not wait for the address's lock (the profile's unique key refuses it); the caller rolls back.
    /// </summary>
    private async Task<bool> MoveAsync(User user, string email, string normalized, CancellationToken cancellationToken)
    {
        try
        {
            var moved = await db.Profiles
                .Where(p => p.UserId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.NormalizedEmail, normalized), cancellationToken);
            if (moved != 1)
            {
                return false;
            }
        }
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        user.Email = email;
        user.EmailConfirmed = true;
        return (await users.UpdateSecurityStampAsync(user)).Succeeded;
    }

    /// <summary>Every open sign-in code of the account and every open code of a change stop working.</summary>
    private async Task EndCodesAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.SignInCodes
            .Where(c => c.UserId == userId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        await db.EmailChanges
            .Where(c => c.UserId == userId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
    }
}
