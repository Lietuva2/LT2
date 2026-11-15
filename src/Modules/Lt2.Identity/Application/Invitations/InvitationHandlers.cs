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
internal sealed class InvitationHandlers(IdentityDb db, IAuditLog audit, ISettings settings, TimeProvider clock) : IInvitations
{
    public async Task<Result<MyInvitation>> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(userId, cancellationToken))
        {
            return Error.Forbidden("member.required");
        }

        // Made when first needed, under the member's lock: two parallel requests make one code (design 6.8).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.PersonalCodeAsync(db, userId, cancellationToken);
        var current = await CurrentAsync(db, InvitationKinds.Personal, userId, cancellationToken)
            ?? await CreatePersonalAsync(db, clock, userId, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await InfoAsync(userId, current, cancellationToken);
    }

    public async Task<Result<MyInvitation>> ReplaceAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(userId, cancellationToken))
        {
            return Error.Forbidden("member.required");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.PersonalCodeAsync(db, userId, cancellationToken);
        var current = await CurrentAsync(db, InvitationKinds.Personal, userId, cancellationToken);
        if (current?.SwitchedOffAt is not null)
        {
            // An administrator's switch-off is not undone by making a new code.
            return Error.Conflict("invitation.switched-off");
        }

        var replacement = current is null
            ? await CreatePersonalAsync(db, clock, userId, null, cancellationToken)
            : await ReplaceCurrentAsync(db, clock, current, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await InfoAsync(userId, replacement, cancellationToken);
    }

    public async Task<InvitationStats> StatsAsync(int days, string? code, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 7, 366);
        var today = VilniusTime.Today(clock);
        var from = today.AddDays(1 - days);

        var dailies = await db.InvitationDailies.AsNoTracking().Where(d => d.Day >= from).ToListAsync(cancellationToken);
        var perCode = dailies.ToDictionary(d => (d.InvitationId, d.Day), d => d.Completed);
        var perDay = dailies.GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Sum(d => d.Completed));
        var lookup = string.IsNullOrWhiteSpace(code) ? null : InvitationRules.Lookup(code);
        var codes = await db.Invitations.AsNoTracking()
            .Where(i => lookup == null ? i.UsedCount > 0 || i.SwitchedOffAt != null : i.Lookup == lookup)
            .OrderByDescending(i => i.UsedCount).ThenBy(i => i.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var owners = codes.Where(i => i.Kind == InvitationKinds.Personal && i.IssuedByUserId != null).Select(i => i.IssuedByUserId!.Value).ToList();
        var names = await db.Profiles.AsNoTracking().Where(p => owners.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.DisplayName, cancellationToken);

        IReadOnlyList<InvitationDay> Series(Func<DateOnly, int> completedOn) =>
            Enumerable.Range(0, days).Select(n => from.AddDays(n)).Select(day => new InvitationDay(day, completedOn(day))).ToList();

        var levels = await db.Invitations.AsNoTracking()
            .GroupBy(i => i.Level)
            .Select(g => new { Level = g.Key, Completed = g.Sum(i => i.UsedCount) })
            .OrderBy(l => l.Level)
            .ToListAsync(cancellationToken);

        // An organisation's code leaves only its level on the profile (design 8.14.14), so "without a code" is "without a level".
        var withoutCode = await db.Profiles.AsNoTracking().CountAsync(p => p.RegisteredAt != null && p.InvitationId == null && p.InvitationLevel == null, cancellationToken);

        return new InvitationStats(
            from,
            today,
            await db.Invitations.AsNoTracking().SumAsync(i => i.UsedCount, cancellationToken),
            Series(day => perDay.GetValueOrDefault(day)),
            codes.Select(i => new InvitationCodeStats(
                    i.Id,
                    i.Kind,
                    i.Kind == InvitationKinds.Team ? i.Code : i.IssuedByUserId is { } owner ? names.GetValueOrDefault(owner) : null,
                    i.UsedCount,
                    i.SwitchedOffAt is null && i.ReplacedAt is null,
                    i.ReplacedAt is not null,
                    Series(day => perCode.GetValueOrDefault((i.Id, day))),
                    i.OrganisationId,
                    i.OrganisationOff))
                .ToList(),
            levels.Where(l => l.Completed > 0).Select(l => new InvitationLevel(l.Level, l.Completed)).ToList(),
            withoutCode);
    }

    public async Task<Result<Unit>> SwitchAsync(Guid invitationId, bool on, string statement, Guid adminUserId, string role, string? ipHash, CancellationToken cancellationToken)
    {
        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
        {
            return Error.NotFound("invitation.not-found");
        }

        if (on && ((invitation.Kind == InvitationKinds.Personal && (invitation.IssuedByUserId is null || invitation.ReplacedAt is not null))
            || (invitation.Kind == InvitationKinds.Organisation && invitation.ReplacedAt is not null)))
        {
            // A deleted member's code, or one its member or organisation replaced, stays off. An organisation's code switched
            // on while the organisation is blocked or closed still does not work: that is the organisation's state, not this switch.
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
    /// At completed registration (design 6.2): counts the use on the code and in the day's total, atomically. The same
    /// conditions as <see cref="InvitationRules.Usable(Invitation, int, DateTimeOffset)"/>, checked again in the update, so of
    /// two completions racing for the last use one gets it. Returns false when the code no longer lets a registration through.
    /// </summary>
    internal static async Task<bool> CountUseAsync(IdentityDb db, Invitation invitation, int perCodeLimit, DateTimeOffset now, CancellationToken ct)
    {
        var usable = db.Invitations.Where(i => i.Id == invitation.Id
            && i.SwitchedOffAt == null && i.ReplacedAt == null && i.OrganisationOff == null && (i.ExpiresAt == null || i.ExpiresAt > now));
        var counted = InvitationRules.LimitedBySetting(invitation.Kind)
            ? await usable
                .Where(i => perCodeLimit == 0 || i.UsedCount < perCodeLimit)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedCount, i => i.UsedCount + 1), ct)
            : await usable
                .Where(i => i.UsesLeft > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsesLeft, i => i.UsesLeft - 1).SetProperty(i => i.UsedCount, i => i.UsedCount + 1), ct);
        if (counted == 0)
        {
            return false;
        }

        var day = VilniusTime.DateOf(now);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO identity.invitation_daily (invitation_id, day, completed) VALUES ({invitation.Id}, {day}, 1)
            ON CONFLICT (invitation_id, day) DO UPDATE SET completed = identity.invitation_daily.completed + 1
            """,
            ct);
        return true;
    }

    /// <summary>
    /// The current code of a member (<see cref="InvitationKinds.Personal"/>, by account) or of an organisation
    /// (<see cref="InvitationKinds.Organisation"/>, by its id): the one not replaced, which may be switched off; or null.
    /// </summary>
    internal static Task<Invitation?> CurrentAsync(IdentityDb db, string kind, Guid ownerId, CancellationToken ct)
    {
        var codes = db.Invitations.AsNoTracking().Where(i => i.Kind == kind && i.ReplacedAt == null);
        codes = kind == InvitationKinds.Organisation ? codes.Where(i => i.OrganisationId == ownerId) : codes.Where(i => i.IssuedByUserId == ownerId);
        return codes.OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Replaces a current code with a new one of the same kind, owner and level; the old one stops working and keeps its
    /// count. The caller holds the owner's lock in a transaction.
    /// </summary>
    internal static async Task<Invitation> ReplaceCurrentAsync(IdentityDb db, TimeProvider clock, Invitation current, CancellationToken ct)
    {
        await db.Invitations.Where(i => i.Id == current.Id && i.ReplacedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReplacedAt, clock.GetUtcNow()), ct);
        return await CreateAsync(db, clock, current.Kind, current.IssuedByUserId, current.OrganisationId, current.Level, ct);
    }

    /// <summary>Makes a member's personal code, one level below the member (or at <paramref name="level"/>).</summary>
    internal static async Task<Invitation> CreatePersonalAsync(IdentityDb db, TimeProvider clock, Guid userId, int? level, CancellationToken ct)
    {
        level ??= InvitationRules.PersonalCodeLevel(await MemberLevelAsync(db, userId, ct));
        return await CreateAsync(db, clock, InvitationKinds.Personal, userId, null, level.Value, ct);
    }

    /// <summary>A member's level (design 6.2): that of the code their registration was completed with, 1 without one.</summary>
    internal static async Task<int> MemberLevelAsync(IdentityDb db, Guid userId, CancellationToken ct)
    {
        var kept = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.InvitationLevel, p.InvitationId }).FirstOrDefaultAsync(ct);
        var level = kept?.InvitationLevel;
        if (level is null && kept?.InvitationId is { } used)
        {
            // Profiles completed before the level was kept on them.
            level = await db.Invitations.AsNoTracking().Where(i => i.Id == used).Select(i => (int?)i.Level).FirstOrDefaultAsync(ct);
        }

        return InvitationRules.MemberLevel(level);
    }

    /// <summary>The uses setting for codes of <paramref name="kind"/> (<see cref="InvitationRules.LimitFor"/>; 0 = no limit).</summary>
    internal static async Task<int> LimitAsync(ISettings settings, string kind, CancellationToken ct) =>
        InvitationRules.LimitedBySetting(kind)
            ? InvitationRules.LimitFor(
                kind,
                await settings.GetAsync(SettingKeys.RegistrationPersonalCodeUses, ct),
                await settings.GetAsync(SettingKeys.RegistrationOrganisationCodeUses, ct))
            : 0;

    /// <summary>Makes an unused code of the personal format, for a member or an organisation.</summary>
    internal static async Task<Invitation> CreateAsync(IdentityDb db, TimeProvider clock, string kind, Guid? userId, Guid? organisationId, int level, CancellationToken ct)
    {
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
                Kind = kind,
                IssuedByUserId = userId,
                OrganisationId = organisationId,
                Level = level,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            return invitation;
        }
    }

    private Task<bool> IsMemberAsync(Guid userId, CancellationToken ct) =>
        db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.RegisteredAt != null, ct);

    private async Task<MyInvitation> InfoAsync(Guid userId, Invitation current, CancellationToken ct)
    {
        var counts = await db.Invitations.AsNoTracking()
            .Where(i => i.Kind == InvitationKinds.Personal && i.IssuedByUserId == userId)
            .Select(i => i.UsedCount)
            .ToListAsync(ct);

        // Whether the code would let a registration through now (design 8.11.5: the share line has no link otherwise).
        var usable = InvitationRules.Usable(current, await LimitAsync(settings, current.Kind, ct), clock.GetUtcNow());
        return new MyInvitation(current.Code, InvitationRules.MemberCount(counts), current.SwitchedOffAt is null, usable);
    }
}
