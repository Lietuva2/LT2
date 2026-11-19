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
/// the audit log or a log line. A context of its own per operation.
/// </summary>
internal sealed class RoleAdministration(IDbContextFactory<IdentityDb> contexts, AccountEvents events, TimeProvider clock) : IIdentityAdministration
{
    public async Task<Result<RoleGrant>> GrantRoleAsync(string email, string role, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Roles.Staff.Contains(role))
        {
            return Error.Validation("role.unknown", "role");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var account = await FindAccountAsync(db, email, cancellationToken);
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
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var account = await FindAccountAsync(db, email, cancellationToken);
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

        // The sessions signed in with the removed passkeys end in the same step (design 6.8): a lost or stolen passkey
        // leaves no session behind, not even a member one.
        var sessionsEnded = await db.Sessions.Where(s => s.UserId == userId && s.PasskeyId != null).ExecuteDeleteAsync(cancellationToken);

        await events.RecordAsync(
            transaction.GetDbTransaction(),
            "account.reset",
            userId,
            actor,
            new { passkeysRemoved = removed, sessionsEnded },
            new AccountNoticeEmail(account.Value.Email, AccountNotices.AccountReset, PasskeysRemoved: removed),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public Task<Result<Unit>> LockAccountAsync(string email, string statement, StaffActor administrator, string? contact, CancellationToken cancellationToken) =>
        SetLockAsync(email, statement, administrator, contact, locked: true, cancellationToken);

    public Task<Result<Unit>> UnlockAccountAsync(string email, string statement, StaffActor administrator, string? contact, CancellationToken cancellationToken) =>
        SetLockAsync(email, statement, administrator, contact, locked: false, cancellationToken);

    /// <summary>
    /// Design 8.3: locking ends every session and the open sign-in codes in the same transaction as the mark, so no sign-in works
    /// afterwards (<see cref="AccountLocks"/> is checked where a code or a passkey is used). The statement is kept in the audit
    /// entry and the email only.
    /// </summary>
    private async Task<Result<Unit>> SetLockAsync(
        string email, string statement, StaffActor administrator, string? contact, bool locked, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (StaffStatements.Check(statement) is { } broken)
        {
            return broken;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var account = await FindAccountAsync(db, email, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("account.not-found");
        }

        var userId = account.Value.UserId;
        if (locked)
        {
            if (userId == administrator.UserId)
            {
                return Error.Forbidden("account.lock-self");
            }

            var profile = await db.Profiles.AsNoTracking().FirstAsync(p => p.UserId == userId, cancellationToken);
            if ((await StaffAccess.ActiveRolesAsync(db, userId, profile, cancellationToken)).Count > 0)
            {
                return Error.Conflict("account.lock-staff");
            }
        }

        var now = clock.GetUtcNow();
        var text = statement.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = locked
            ? await db.Profiles.Where(p => p.UserId == userId && p.LockedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LockedAt, now), cancellationToken)
            : await db.Profiles.Where(p => p.UserId == userId && p.LockedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LockedAt, (DateTimeOffset?)null), cancellationToken);
        if (changed == 0)
        {
            return Error.Conflict(locked ? "account.locked" : "account.not-locked");
        }

        var sessionsEnded = 0;
        if (locked)
        {
            sessionsEnded = await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.SignInCodes.Where(c => c.UserId == userId && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        }

        await events.RecordAsync(
            transaction.GetDbTransaction(),
            locked ? "account.locked" : "account.unlocked",
            userId,
            new Actor(administrator.UserId, administrator.Role, administrator.IpHash),
            locked ? new { statement = text, sessionsEnded } : new { statement = text },
            new AccountNoticeEmail(
                account.Value.Email, locked ? AccountNotices.AccountLocked : AccountNotices.AccountUnlocked, Statement: text, Contact: contact),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>A completed account by address; pending accounts cannot hold roles.</summary>
    private static async Task<(Guid UserId, string Email)?> FindAccountAsync(IdentityDb db, string email, CancellationToken cancellationToken)
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
