using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Politicians;

/// <summary>The current session as the rules of an MP's link see it (design 8.5.4).</summary>
/// <param name="Link">The account's open link, or null.</param>
/// <param name="PasskeySession">This session was signed in with a passkey that is still on the account.</param>
internal sealed record PoliticianState(Guid UserId, Session? Session, PoliticianLink? Link, bool PasskeySession)
{
    public bool IsMp => Link?.Part == PoliticianParts.Mp;

    /// <summary>Speaking for the MP: a link and a passkey session.</summary>
    public bool MayWrite => PoliticianRules.MaySpeakForMp(Link is not null, PasskeySession);
}

/// <summary>
/// Reads the link and the session from the database on every call (design 8.5.4: a link is not a role in the session), as
/// the staff checks do, so ending a link or removing a passkey works at once. A context of its own per call: a page's
/// components ask (who speaks for an MP, the account) while others read at the same time.
/// </summary>
internal sealed class PoliticianAccess(IDbContextFactory<IdentityDb> contexts, TimeProvider clock)
{
    public static readonly Error PasskeyRequired = Error.Forbidden("mp.passkey-required");

    public async Task<PoliticianState?> ForAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        Session? session = null;
        if (Guid.TryParse(principal.FindFirstValue(SessionTicketStore.SessionIdClaim), out var sessionId))
        {
            session = await db.Sessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.ExpiresAt > now, cancellationToken);
        }

        var passkey = session?.PasskeyId is { } used
            && await db.Set<IdentityUserPasskey<Guid>>().AnyAsync(p => p.UserId == userId && p.CredentialId == used, cancellationToken);
        var link = await OpenLinks(db).AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        return new PoliticianState(userId, session, link, passkey);
    }

    /// <summary>Links that have not ended.</summary>
    public static IQueryable<PoliticianLink> OpenLinks(IdentityDb db) => db.PoliticianLinks.Where(l => l.EndedAt == null);
}
