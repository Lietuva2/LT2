using System.Security.Claims;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Account;

/// <summary>
/// Changing the display name (design 6.2), as often as the member likes (owner, 2026-11-16). The name is read live wherever it
/// is shown (beside arguments, in representatives' lists, on feedback and moderation screens), so a change shows everywhere at
/// once, older arguments included; summaries never hold names. The same rule as at registration (at most 60 characters, empty
/// allowed); refused while a writing restriction holds (asked of Appeals through <see cref="IWritingRestrictions"/>). Audited
/// that it changed, never the names; no earlier names are kept.
/// </summary>
internal sealed class DisplayNameHandlers(IdentityDb db, IWritingRestrictions restrictions, AccountEvents events)
{
    public async Task<Result<Unit>> ChangeAsync(ClaimsPrincipal principal, string? typed, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        if (ProfileRules.CheckDisplayName(typed) is { } nameError)
        {
            return Error.Validation(nameError, "displayName");
        }

        if (await restrictions.IsRestrictedAsync(userId, cancellationToken))
        {
            return Error.Forbidden("writing.restricted");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        // Design 8.25.6 (the designer's proposal): a public profile shows a name, so it cannot be emptied while public.
        if (ProfileRules.CheckNameWhilePublic(profile.PublicProfileId is not null, typed) is { } nameRequired)
        {
            return Error.Validation(nameRequired, "displayName");
        }

        // A pending account gives its name with the rest of its registration (design 6.2).
        if (profile.IsPending)
        {
            return Error.Conflict("registration.pending");
        }

        if (!ProfileRules.IsChange(profile.DisplayName, typed))
        {
            return Result.Ok();
        }

        profile.DisplayName = ProfileRules.DisplayName(typed);
        await db.SaveChangesAsync(cancellationToken);
        await events.RecordAsync(
            transaction.GetDbTransaction(),
            "profile.display-name-changed",
            userId,
            new Actor(userId, Roles.Member, ipHash),
            null,
            null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }
}
