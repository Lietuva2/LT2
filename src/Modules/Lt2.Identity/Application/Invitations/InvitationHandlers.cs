using System.Text.Json;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Invitations;

/// <summary>
/// Personal invitation codes (design 6.2, decided 2026-11-13): one per member, made when first needed, replaceable;
/// the member sees only their count. Administrators see numbers per day, per code and per level, never who invited
/// whom, and switch codes off and on. Nothing here reads anything about sides.
/// </summary>
internal sealed class InvitationHandlers(IdentityDb db, IAuditLog audit, TimeProvider clock) : IInvitations
{
    public static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    public async Task<Result<MyInvitation>> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null || profile.IsPending)
        {
            return Error.Forbidden("member.required");
        }

        var current = await CurrentAsync(userId, cancellationToken) ?? await CreateAsync(userId, cancellationToken);
        return await InfoAsync(userId, current, cancellationToken);
    }

    public async Task<Result<MyInvitation>> ReplaceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null || profile.IsPending)
        {
            return Error.Forbidden("member.required");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await CurrentAsync(userId, cancellationToken);
        if (current?.SwitchedOffAt is not null)
        {
            // An administrator's switch-off is not undone by making a new code.
            return Error.Conflict("invitation.switched-off");
        }

        if (current is not null)
        {
            await db.Invitations.Where(i => i.Id == current.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ReplacedAt, clock.GetUtcNow()), cancellationToken);
        }

        var replacement = await CreateAsync(userId, cancellationToken, current?.Level);
        await transaction.CommitAsync(cancellationToken);
        return await InfoAsync(userId, replacement, cancellationToken);
    }

    public async Task<InvitationStats> StatsAsync(int days, string? code, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 7, 366);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Vilnius).DateTime);
        var from = today.AddDays(1 - days);

        var dailies = await db.InvitationDailies.AsNoTracking().Where(d => d.Day >= from).ToListAsync(cancellationToken);
        var lookup = string.IsNullOrWhiteSpace(code) ? null : InvitationRules.Lookup(code);
        var codes = await db.Invitations.AsNoTracking()
            .Where(i => lookup == null ? i.UsedCount > 0 || i.SwitchedOffAt != null : i.Lookup == lookup)
            .OrderByDescending(i => i.UsedCount).ThenBy(i => i.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var owners = codes.Where(i => i.Kind == InvitationKinds.Personal && i.IssuedByUserId != null).Select(i => i.IssuedByUserId!.Value).ToList();
        var names = await db.Profiles.AsNoTracking().Where(p => owners.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.DisplayName, cancellationToken);

        IReadOnlyList<InvitationDay> Series(Func<InvitationDaily, bool> which) =>
            Enumerable.Range(0, days).Select(n => from.AddDays(n))
                .Select(day => new InvitationDay(day, dailies.Where(d => d.Day == day && which(d)).Sum(d => d.Completed)))
                .ToList();

        var levels = await db.Invitations.AsNoTracking()
            .GroupBy(i => i.Level)
            .Select(g => new { Level = g.Key, Completed = g.Sum(i => i.UsedCount) })
            .OrderBy(l => l.Level)
            .ToListAsync(cancellationToken);
        var withoutCode = await db.Profiles.AsNoTracking().CountAsync(p => p.RegisteredAt != null && p.InvitationId == null, cancellationToken);

        return new InvitationStats(
            from,
            today,
            await db.Invitations.AsNoTracking().SumAsync(i => i.UsedCount, cancellationToken),
            Series(_ => true),
            codes.Select(i => new InvitationCodeStats(
                    i.Id,
                    i.Kind,
                    i.Kind == InvitationKinds.Team ? i.Code : i.IssuedByUserId is { } owner ? names.GetValueOrDefault(owner) : null,
                    i.UsedCount,
                    i.SwitchedOffAt is null && i.ReplacedAt is null,
                    i.ReplacedAt is not null,
                    Series(d => d.InvitationId == i.Id)))
                .ToList(),
            levels.Where(l => l.Completed > 0).Select(l => new InvitationLevel(l.Level, l.Completed)).ToList(),
            withoutCode);
    }

    public async Task<Result<Unit>> SwitchAsync(Guid invitationId, bool on, string statement, Guid adminUserId, string role, string? ipHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return Error.Validation("statement.required", "statement");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
        {
            return Error.NotFound("invitation.not-found");
        }

        if (on && invitation.Kind == InvitationKinds.Personal && (invitation.IssuedByUserId is null || invitation.ReplacedAt is not null))
        {
            // A deleted member's code, or one its member replaced, stays off.
            return Error.Conflict("invitation.cannot-switch-on");
        }

        if (on == (invitation.SwitchedOffAt is null))
        {
            return Error.Conflict("invitation.already");
        }

        invitation.SwitchedOffAt = on ? null : clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(
                on ? "invitation.switched-on" : "invitation.switched-off",
                adminUserId,
                role,
                "invitation:" + invitationId,
                JsonSerializer.SerializeToElement(new { statement = statement.Trim(), kind = invitation.Kind }, JsonSerializerOptions.Web),
                ipHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// At completed registration (design 6.2): counts the use on the code and in the day's total, atomically, and makes
    /// the new member's own code. Returns false when the code no longer lets a registration through.
    /// </summary>
    internal static async Task<bool> CountUseAsync(IdentityDb db, Invitation invitation, int personalLimit, DateTimeOffset now, CancellationToken ct)
    {
        var counted = invitation.Kind == InvitationKinds.Personal
            ? await db.Invitations
                .Where(i => i.Id == invitation.Id && i.SwitchedOffAt == null && i.ReplacedAt == null && (personalLimit == 0 || i.UsedCount < personalLimit))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedCount, i => i.UsedCount + 1), ct)
            : await db.Invitations
                .Where(i => i.Id == invitation.Id && i.SwitchedOffAt == null && i.UsesLeft > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsesLeft, i => i.UsesLeft - 1).SetProperty(i => i.UsedCount, i => i.UsedCount + 1), ct);
        if (counted == 0)
        {
            return false;
        }

        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Vilnius).DateTime);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO identity.invitation_daily (invitation_id, day, completed) VALUES ({invitation.Id}, {day}, 1)
            ON CONFLICT (invitation_id, day) DO UPDATE SET completed = identity.invitation_daily.completed + 1
            """,
            ct);
        return true;
    }

    /// <summary>The member's own code that is not replaced (it may be switched off), or null.</summary>
    private Task<Invitation?> CurrentAsync(Guid userId, CancellationToken ct) =>
        db.Invitations.AsNoTracking()
            .Where(i => i.Kind == InvitationKinds.Personal && i.IssuedByUserId == userId && i.ReplacedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>Makes a member's personal code, one level below the member (or at <paramref name="level"/> when replacing).</summary>
    internal async Task<Invitation> CreateAsync(Guid userId, CancellationToken ct, int? level = null) =>
        await CreatePersonalAsync(db, clock, userId, level, ct);

    internal static async Task<Invitation> CreatePersonalAsync(IdentityDb db, TimeProvider clock, Guid userId, int? level, CancellationToken ct)
    {
        if (level is null)
        {
            var usedLevel = await (from p in db.Profiles.AsNoTracking()
                                   join i in db.Invitations.AsNoTracking() on p.InvitationId equals i.Id
                                   where p.UserId == userId
                                   select (int?)i.Level).FirstOrDefaultAsync(ct);
            level = InvitationRules.PersonalCodeLevel(InvitationRules.MemberLevel(usedLevel));
        }

        for (var attempt = 0; ; attempt++)
        {
            var code = InvitationRules.GeneratePersonal();
            var lookup = InvitationRules.Lookup(code);
            if (await db.Invitations.AnyAsync(i => i.Lookup == lookup, ct))
            {
                if (attempt < 10)
                {
                    continue;
                }

                throw new InvalidOperationException("Could not make an unused invitation code.");
            }

            var invitation = new Invitation
            {
                Id = Ids.New(clock),
                Code = code,
                Kind = InvitationKinds.Personal,
                IssuedByUserId = userId,
                Level = level.Value,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            return invitation;
        }
    }

    private async Task<MyInvitation> InfoAsync(Guid userId, Invitation current, CancellationToken ct)
    {
        var counts = await db.Invitations.AsNoTracking()
            .Where(i => i.Kind == InvitationKinds.Personal && i.IssuedByUserId == userId)
            .Select(i => i.UsedCount)
            .ToListAsync(ct);
        return new MyInvitation(current.Code, InvitationRules.MemberCount(counts), current.SwitchedOffAt is null);
    }
}
