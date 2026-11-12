using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Staff;

/// <summary>
/// Granting staff roles and resetting accounts (design 6.2). Works on the database alone, so the migrator can use it for
/// the first administrator. The activation code is returned once and stored only as a hash; it never goes into an email,
/// the audit log or a log line.
/// </summary>
internal sealed class RoleAdministration(IdentityDb db, AccountEvents events, TimeProvider clock) : IIdentityAdministration
{
    public async Task<Result<RoleGrant>> GrantRoleAsync(string email, string role, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Roles.Staff.Contains(role))
        {
            return Error.Validation("role.unknown", "role");
        }

        var account = await FindAccountAsync(email, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("account.not-found");
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var roleId = SeedRoles.IdOf(role);
        if (!await db.UserRoles.AnyAsync(r => r.UserId == account.Value.UserId && r.RoleId == roleId, cancellationToken))
        {
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = account.Value.UserId, RoleId = roleId });
        }

        // An earlier code for the same role that was never used stops working.
        await db.RoleActivations
            .Where(a => a.UserId == account.Value.UserId && a.Role == role && a.UsedAt == null)
            .ExecuteDeleteAsync(cancellationToken);

        var id = Ids.New(clock);
        var code = StaffRules.GenerateActivationCode();
        var expires = now + StaffRules.ActivationLifetime;
        db.RoleActivations.Add(new RoleActivation
        {
            Id = id,
            UserId = account.Value.UserId,
            Role = role,
            CodeHash = StaffRules.HashActivationCode(id, code),
            IssuedByUserId = actor.UserId,
            CreatedAt = now,
            ExpiresAt = expires,
        });
        await db.SaveChangesAsync(cancellationToken);

        await events.RecordAsync(
            transaction.GetDbTransaction(),
            "role.granted",
            account.Value.UserId,
            actor,
            new { role, expiresAt = expires },
            new AccountNoticeEmail(account.Value.Email, AccountNotices.RoleGranted, role),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RoleGrant(code, expires);
    }

    public async Task<Result<Unit>> ResetAccountAsync(string email, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var account = await FindAccountAsync(email, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("account.not-found");
        }

        var userId = account.Value.UserId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var removed = await db.Set<IdentityUserPasskey<Guid>>().Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Profiles
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StaffPasskeyId, (byte[]?)null), cancellationToken);

        // Every activation goes, used or not: the roles stay granted but inactive until a new code is used.
        await db.RoleActivations.Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        await events.RecordAsync(
            transaction.GetDbTransaction(),
            "account.reset",
            userId,
            actor,
            new { passkeysRemoved = removed },
            new AccountNoticeEmail(account.Value.Email, AccountNotices.AccountReset, PasskeysRemoved: removed),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>A completed account by address; pending accounts cannot hold roles.</summary>
    private async Task<(Guid UserId, string Email)?> FindAccountAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = EmailAddress.Normalize(email);
        if (normalized is null)
        {
            return null;
        }

        var account = await db.Profiles.AsNoTracking()
            .Where(p => p.NormalizedEmail == normalized && p.RegisteredAt != null)
            .Join(db.Users, p => p.UserId, u => u.Id, (p, u) => new { p.UserId, u.Email })
            .FirstOrDefaultAsync(cancellationToken);
        return account is null ? null : (account.UserId, account.Email!);
    }
}
