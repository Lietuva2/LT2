using System.Data.Common;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Identity.Application.Invitations;

/// <summary>
/// An organisation's invitation code (design 8.14.14, stage B): one per organisation, made when a representative first asks
/// for it, in the format and under the rules of personal codes, with its own uses setting. It belongs to the organisation:
/// no account is kept on it, so it stays when the representative who asked leaves or deletes their account. Whether the
/// caller represents the organisation, and whether it is active, the host asks the Appeals module first; this module only
/// knows the organisation's id. Nothing here is logged or audited, so no row ties a representative to the code.
/// </summary>
internal sealed class OrganisationInvitationHandlers(IdentityDb db, ISettings settings, TimeProvider clock) : IOrganisationInvitations
{
    public async Task<Result<OrganisationInvitation>> ForOrganisationAsync(Guid organisationId, bool active, Guid requestedByUserId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(organisationId, cancellationToken);
        var current = await CurrentAsync(organisationId, cancellationToken);
        if (current is null && active)
        {
            current = await InvitationHandlers.CreateAsync(
                db, clock, InvitationKinds.Organisation, null, organisationId,
                InvitationRules.OrganisationCodeLevel(await InvitationHandlers.MemberLevelAsync(db, requestedByUserId, cancellationToken)), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await InfoAsync(organisationId, current, cancellationToken);
    }

    public async Task<Result<OrganisationInvitation>> ReplaceAsync(Guid organisationId, bool active, CancellationToken cancellationToken)
    {
        if (!active)
        {
            return Error.Conflict("invitation.organisation-inactive");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(organisationId, cancellationToken);
        var current = await CurrentAsync(organisationId, cancellationToken);
        if (current is null)
        {
            return Error.NotFound("invitation.not-found");
        }

        if (current.SwitchedOffAt is not null)
        {
            // An administrator's switch-off is not undone by making a new code (as for personal codes, design 6.2).
            return Error.Conflict("invitation.organisation-switched-off");
        }

        await db.Invitations.Where(i => i.Id == current.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ReplacedAt, clock.GetUtcNow()), cancellationToken);
        var replacement = await InvitationHandlers.CreateAsync(db, clock, InvitationKinds.Organisation, null, organisationId, current.Level, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await InfoAsync(organisationId, replacement, cancellationToken);
    }

    /// <summary>The organisation's code that is not replaced (it may be switched off), or null.</summary>
    private Task<Invitation?> CurrentAsync(Guid organisationId, CancellationToken ct) =>
        db.Invitations.AsNoTracking()
            .Where(i => i.Kind == InvitationKinds.Organisation && i.OrganisationId == organisationId && i.ReplacedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>One organisation's code is made or replaced at a time, so two representatives asking at once get the same one.</summary>
    private Task<int> LockAsync(Guid organisationId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.organisation-code:" + organisationId.ToString("N")], ct);

    private async Task<OrganisationInvitation> InfoAsync(Guid organisationId, Invitation? current, CancellationToken ct)
    {
        // One number: everyone who completed registration with any of the organisation's codes, replaced ones too. Never
        // broken down by day, bill or side for the organisation (design 8.14.14).
        var completed = await db.Invitations.AsNoTracking()
            .Where(i => i.Kind == InvitationKinds.Organisation && i.OrganisationId == organisationId)
            .SumAsync(i => i.UsedCount, ct);
        if (current is null)
        {
            return new OrganisationInvitation(null, completed, false, true, false);
        }

        var usable = InvitationRules.Usable(
            current.Kind, current.UsesLeft, current.UsedCount, current.ExpiresAt, current.SwitchedOffAt, current.ReplacedAt,
            await InvitationHandlers.LimitAsync(settings, current.Kind, ct), clock.GetUtcNow(), current.OrganisationOff);
        return new OrganisationInvitation(current.Code, completed, usable, current.SwitchedOffAt is null, current.OrganisationOff is not null);
    }
}

/// <summary>
/// <see cref="IOrganisationCodeSwitch"/>: an organisation's codes follow its state (design 8.14.14, stage B), in the Appeals
/// module's transaction. Only the organisation's own column changes; an administrator's switch-off stays as it is.
/// </summary>
internal sealed class OrganisationCodeSwitch : IOrganisationCodeSwitch
{
    public async Task SetAsync(Guid organisationId, string state, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Switching codes needs a PostgreSQL transaction.", nameof(transaction));
        }

        var sql = state switch
        {
            // Closing switches every code of the organisation off for good.
            OrganisationCodeStates.Closed =>
                "UPDATE identity.invitation SET organisation_off = 'closed' WHERE kind = 'organisation' AND organisation_id = @organisation",
            OrganisationCodeStates.Blocked =>
                "UPDATE identity.invitation SET organisation_off = 'blocked' WHERE kind = 'organisation' AND organisation_id = @organisation AND organisation_off IS NULL",

            // Lifting the block brings back only what the block switched off: not a closed organisation's codes, and not
            // an administrator's switch-off, which is its own column.
            OrganisationCodeStates.Active =>
                "UPDATE identity.invitation SET organisation_off = NULL WHERE kind = 'organisation' AND organisation_id = @organisation AND organisation_off = 'blocked'",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown organisation state."),
        };
#pragma warning disable CA2100 // Constant SQL chosen above.
        await using var command = new NpgsqlCommand(sql, connection, npgsql);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("organisation", organisationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
