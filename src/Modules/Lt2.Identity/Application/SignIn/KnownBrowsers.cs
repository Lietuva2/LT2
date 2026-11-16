using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.SignIn;

/// <summary>
/// Known browsers (design 6.2, 8.20.2): the <c>__Host-lt2.device</c> cookie and <c>identity.known_device</c>. The cookie is set
/// on a successful sign-in by code or passkey of a member and at the end of registration, renewed on later sign-ins, and kept
/// on signing out. It never authenticates, never lengthens a session and never skips a code: <see cref="RecognisedAsync"/> only
/// tells the code rules which allowance and which codes apply. Nothing here is logged.
/// </summary>
internal sealed class KnownBrowsers(IdentityDb db, IHttpContextAccessor accessor, DeviceKeys keys, TimeProvider clock)
{
    /// <summary>The known browser of the current request for <paramref name="userId"/>, or null for a stranger to that account.</summary>
    public async Task<Guid?> RecognisedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (CurrentHash() is not { } hash)
        {
            return null;
        }

        return await db.KnownDevices.AsNoTracking()
            .Where(d => d.UserId == userId && d.IdentifierHash == hash)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// After a successful sign-in of a member (or the completed registration): the browser becomes, or stays, known to the
    /// account, its last-seen time and the cookie's year are renewed, and the account keeps at most twenty. A pending account
    /// is not remembered until its registration is complete.
    /// </summary>
    public async Task RememberAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!keys.Configured || accessor.HttpContext is not { } context
            || !await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.RegisteredAt != null, cancellationToken))
        {
            return;
        }

        var identifier = CookieValue(context) ?? DeviceKeys.NewIdentifier();
        var hash = keys.Hash(identifier)!;
        var now = clock.GetUtcNow();
        var label = DeviceDescription.FromUserAgent(context.Request.Headers.UserAgent.ToString());
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            // One account's browsers change one request at a time, so two sign-ins at once neither duplicate a row nor pass the cap.
            await IdentityLocks.KnownDevicesAsync(db, userId, cancellationToken);
            var row = await db.KnownDevices.FirstOrDefaultAsync(d => d.UserId == userId && d.IdentifierHash == hash, cancellationToken);
            if (row is null)
            {
                db.KnownDevices.Add(new KnownDevice
                {
                    Id = Ids.New(clock),
                    UserId = userId,
                    IdentifierHash = hash,
                    Label = label,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                });
            }
            else
            {
                row.LastSeenAt = now;
                row.Label = label ?? row.Label;
            }

            await db.SaveChangesAsync(cancellationToken);
            var all = await db.KnownDevices.AsNoTracking().Where(d => d.UserId == userId).Select(d => new { d.Id, d.LastSeenAt }).ToListAsync(cancellationToken);
            var beyond = KnownBrowserRules.BeyondCap(all.Select(d => (d.Id, d.LastSeenAt)));
            if (beyond.Count > 0)
            {
                await db.KnownDevices.Where(d => beyond.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
            }

            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }

        context.Response.Cookies.Append(IdentityModule.DeviceCookieName, identifier, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = now + KnownBrowserRules.Lifetime,
        });
    }

    /// <summary>The account's known browsers, last seen first; the current request's own is marked.</summary>
    public async Task<IReadOnlyList<KnownBrowserSummary>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var hash = CurrentHash();
        var rows = await db.KnownDevices.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(cancellationToken);
        return rows.Select(d => new KnownBrowserSummary(
                d.Id, d.Label, d.FirstSeenAt, d.LastSeenAt, hash is not null && d.IdentifierHash.AsSpan().SequenceEqual(hash)))
            .ToList();
    }

    /// <summary>"Pamiršti": the browser becomes a stranger again; the codes it asked for go with it.</summary>
    public async Task<Result<Unit>> ForgetAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var deleted = await db.KnownDevices.Where(d => d.Id == id && d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 1 ? Result.Ok() : Error.NotFound("known-browser.not-found");
    }

    private static string? CookieValue(HttpContext context) =>
        context.Request.Cookies.TryGetValue(IdentityModule.DeviceCookieName, out var value) && DeviceKeys.LooksLikeIdentifier(value) ? value : null;

    private byte[]? CurrentHash() =>
        accessor.HttpContext is { } context && CookieValue(context) is { } identifier ? keys.Hash(identifier) : null;
}
