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
    KnownBrowsers browsers,
    TimeProvider clock)
{
    internal static readonly Error InvalidCode = Error.Validation("auth.code-invalid", "code");

    public async Task<Result<SignInOutcome>> HandleAsync(string email, string code, CancellationToken cancellationToken)
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

        // Design 8.20.2: a browser tries the newest code any browser may use and, when it is known to the account, the newest
        // code it asked for itself. A known browser's code is never tried from elsewhere, so guesses there cannot use it up.
        var browser = await browsers.RecognisedAsync(profile.UserId, cancellationToken);
        var open = await db.SignInCodes.AsNoTracking()
            .Where(c => c.UserId == profile.UserId && c.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        var tried = KnownBrowserRules.CodesToTry(open, browser);
        var current = tried.FirstOrDefault(c => SignInCodeRules.Check(c, code, now) == CodeCheck.Valid);
        if (current is null)
        {
            // A wrong code counts as an attempt against each code it was tried against.
            var mismatched = tried.Where(c => SignInCodeRules.Check(c, code, now) == CodeCheck.Mismatch).Select(c => c.Id).ToList();
            if (mismatched.Count > 0)
            {
                await db.SignInCodes
                    .Where(c => mismatched.Contains(c.Id) && c.FailedAttempts < SignInCodeRules.MaxAttempts)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancellationToken);
            }

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

        // A new session under a new key, whichever account the browser held before (design 6.2): never a silent switch.
        var changed = await SessionSignIn.SignInAsync(signIn, user, AuthenticationMethods.EmailCode);

        // Design 8.20.2: a member's browser becomes known, or is renewed; a pending account's waits for its completion.
        await browsers.RememberAsync(user.Id, cancellationToken);
        return new SignInOutcome(changed);
    }
}
