using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.SignIn;

/// <summary>Requests a sign-in code. Always succeeds, so the answer never tells whether the address is registered.</summary>
internal sealed class RequestCodeHandler(IdentityDb db, UserManager<User> users, SignInCodeIssuer codes)
{
    public async Task<Result<Unit>> HandleAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = EmailAddress.Normalize(email);
        if (normalized is null)
        {
            return Result.Ok();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var userId = await db.Profiles.AsNoTracking()
            .Where(p => p.NormalizedEmail == normalized)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (userId != Guid.Empty && await users.FindByIdAsync(userId.ToString()) is { } user)
        {
            await codes.IssueAsync(user, transaction, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>
/// Signs in with the emailed code. Every failure (unknown address, wrong, expired, used or exhausted code) gives the
/// same error. The first successful code confirms the email address; a pending account then completes its registration.
/// </summary>
internal sealed class CodeSignInHandler(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    TimeProvider clock)
{
    internal static readonly Error InvalidCode = Error.Validation("auth.code-invalid", "code");

    public async Task<Result<Unit>> HandleAsync(string email, string code, CancellationToken cancellationToken)
    {
        var normalized = EmailAddress.Normalize(email);
        if (normalized is null)
        {
            return InvalidCode;
        }

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.NormalizedEmail == normalized, cancellationToken);
        if (profile is null)
        {
            return InvalidCode;
        }

        var now = clock.GetUtcNow();
        var current = await db.SignInCodes.AsNoTracking()
            .Where(c => c.UserId == profile.UserId && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return InvalidCode;
        }

        switch (SignInCodeRules.Check(current, code, now))
        {
            case CodeCheck.Mismatch:
                await db.SignInCodes
                    .Where(c => c.Id == current.Id && c.FailedAttempts < SignInCodeRules.MaxAttempts)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancellationToken);
                return InvalidCode;
            case CodeCheck.Valid:
                break;
            default:
                return InvalidCode;
        }

        // Consume atomically: of two concurrent requests with the right code, only one signs in.
        var consumed = await db.SignInCodes
            .Where(c => c.Id == current.Id && c.ConsumedAt == null && c.FailedAttempts < SignInCodeRules.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return InvalidCode;
        }

        var user = await users.FindByIdAsync(profile.UserId.ToString());
        if (user is null || !await signIn.CanSignInAsync(user))
        {
            return InvalidCode;
        }

        // The first correct code proves the address. A pending account stays pending: the member role comes with the
        // completed registration (design 6.2).
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            profile.VerificationLevel = VerificationLevels.Email;
            await users.UpdateAsync(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        await signIn.SignInWithClaimsAsync(
            user,
            new AuthenticationProperties { IsPersistent = true },
            [new Claim(AuthenticationMethods.ClaimType, AuthenticationMethods.EmailCode)]);
        return Result.Ok();
    }
}
