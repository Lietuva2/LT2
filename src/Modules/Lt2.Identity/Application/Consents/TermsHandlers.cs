using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Consents;

/// <summary>
/// The terms of use (design 8.22.2): a member's accepted version is a consent of kind <c>terms</c> with its text version. A
/// member whose newest accepted version is behind the one in force (or who never accepted, an account older than the terms)
/// must accept before writing; <see cref="TermsOfUse.NeedsAcceptance"/> is the rule.
/// </summary>
internal sealed class TermsHandlers(IdentityDb db, TimeProvider clock)
{
    public async Task<Result<Unit>> AcceptAsync(ClaimsPrincipal principal, string textVersion, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        if (textVersion != TermsOfUse.Version)
        {
            // The page showed another version: the person must see the text in force.
            return Error.Conflict("terms.outdated");
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        // A pending account accepts the terms with the rest of its registration (design 6.2, 8.22.2).
        if (profile.IsPending)
        {
            return Error.Conflict("registration.pending");
        }

        if (!TermsOfUse.NeedsAcceptance(await AcceptedVersionAsync(db, userId, cancellationToken)))
        {
            return Result.Ok();
        }

        db.Consents.Add(Given(userId, clock));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Whether the account is a member who must accept the terms in force first; false for a pending or unknown account.</summary>
    public async Task<bool> RequiredAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        principal.UserId() is { } userId && await RequiredAsync(db, userId, cancellationToken);

    public static async Task<bool> RequiredAsync(IdentityDb db, Guid userId, CancellationToken cancellationToken)
    {
        var member = await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.RegisteredAt != null, cancellationToken);
        return member && TermsOfUse.NeedsAcceptance(await AcceptedVersionAsync(db, userId, cancellationToken));
    }

    /// <summary>The consent row for the version in force, given now.</summary>
    public static Consent Given(Guid userId, TimeProvider clock) => new()
    {
        Id = Ids.New(clock),
        UserId = userId,
        Kind = ConsentKinds.Terms,
        TextVersion = TermsOfUse.Version,
        GivenAt = clock.GetUtcNow(),
    };

    /// <summary>The newest version the account accepted and did not withdraw, or null.</summary>
    private static Task<string?> AcceptedVersionAsync(IdentityDb db, Guid userId, CancellationToken cancellationToken) =>
        db.Consents.AsNoTracking()
            .Where(c => c.UserId == userId && c.Kind == ConsentKinds.Terms && c.WithdrawnAt == null)
            .OrderByDescending(c => c.TextVersion)
            .Select(c => c.TextVersion)
            .FirstOrDefaultAsync(cancellationToken);
}
