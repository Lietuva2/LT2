using System.Data.Common;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Appeals.Application;

/// <summary><see cref="IAccountData"/>: one person's data in the appeals schema, for export and deletion (design 6.5).</summary>
internal sealed class AccountData(IDbContextFactory<AppealsDb> contexts, IPositionErasure positions, OrganisationReads organisations, IOrganisationCodeSwitch codes, TimeProvider clock) : IAccountData
{
    public string Section => "appeals";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var bills = await db.Appeals.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.BillNumber, cancellationToken);

        // Only from before 2026-11-14 (design 8.12.4): appeals whose proposal the person wrote.
        var appeals = await db.Appeals.AsNoTracking().Where(a => a.InitiatorUserId == userId)
            .Select(a => new { bill = a.BillNumber, a.Description, a.Status, a.OpenedAt }).ToListAsync(cancellationToken);
        var myPositions = (await db.Positions.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(cancellationToken))
            .Select(p => new { bill = bills.GetValueOrDefault(p.AppealId), side = p.Side, takenAt = p.CreatedAt, changedAt = p.ChangedAt });
        var reasons = (await db.Reasons.AsNoTracking().Where(r => r.AuthorUserId == userId).ToListAsync(cancellationToken))
            .Select(r => new { r.Id, bill = bills.GetValueOrDefault(r.AppealId), side = r.Side, text = r.Text, status = r.Status, writtenAt = r.CreatedAt });

        // Only which argument was marked, not its words: those are another person's.
        var endorsements = (await db.Endorsements.AsNoTracking().Where(e => e.UserId == userId).ToListAsync(cancellationToken))
            .Select(e => new { reason = e.ReasonId, bill = bills.GetValueOrDefault(e.AppealId), markedAt = e.CreatedAt });
        var reports = await db.Reports.AsNoTracking().Where(r => r.ReporterUserId == userId)
            .Select(r => new { r.TargetKind, r.TargetId, r.Ground, r.Text, r.Status, r.CreatedAt }).ToListAsync(cancellationToken);
        var flags = (await db.Flags.AsNoTracking().Where(f => f.ReporterUserId == userId).ToListAsync(cancellationToken))
            .Select(f => new { bill = bills.GetValueOrDefault(f.AppealId), f.Kind, f.Link, f.Note, f.Status, f.CreatedAt });

        // Restrictions on the person's writing (design 8.3): the person's own data, without who set or lifted them.
        var restrictions = (await db.WritingRestrictions.AsNoTracking().Where(w => w.UserId == userId).ToListAsync(cancellationToken))
            .Select(w => new { until = Domain.WritingRules.Until(w.EndsAt), statement = w.Statement, setAt = w.SetAt, liftedAt = w.LiftedAt, liftStatement = w.LiftStatement });


        // A moderator's own requests for AI drafts (design 8.11.4): bill and time only, as audited.
        var drafts = await db.AiDraftRequests.AsNoTracking().Where(r => r.ModeratorUserId == userId).OrderBy(r => r.RequestedAt)
            .Select(r => new { bill = r.BillNumber, requestedAt = r.RequestedAt }).ToListAsync(cancellationToken);

        // Design 8.14.14: the person's representative roles and the statements they wrote (or last changed), their own data.
        var roles = await (from r in db.OrganisationRepresentatives.AsNoTracking()
                           join o in db.Organisations.AsNoTracking() on r.OrganisationId equals o.Id
                           where r.UserId == userId
                           orderby r.GrantedAt
                           select new { organisation = o.Name, slug = o.Slug, since = r.GrantedAt, ended = r.EndedAt, registeredByMe = o.RegisteredByUserId == userId })
            .ToListAsync(cancellationToken);
        var written = await (from s in db.OrganisationStatements.AsNoTracking()
                             join o in db.Organisations.AsNoTracking() on s.OrganisationId equals o.Id
                             where s.WrittenByUserId == userId
                             orderby s.PublishedAt
                             select new { organisation = o.Name, bill = s.BillNumber, stance = s.Stance, text = s.Text, link = s.Link, goal = s.Goal, status = s.Status, publishedAt = s.PublishedAt, editedAt = s.EditedAt })
            .ToListAsync(cancellationToken);
        return new
        {
            appealsOpened = appeals, positions = myPositions, reasons, endorsements, reports, dateFlags = flags, writingRestrictions = restrictions,
            aiDraftRequests = drafts, organisationRoles = roles, organisationStatements = written,
        };
    }

    public async Task EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await positions.EraseAsync(userId, transaction, cancellationToken);
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Erasing needs a PostgreSQL transaction.", nameof(transaction));
        }

        await EraseOrganisationRolesAsync(userId, npgsql, cancellationToken);
        foreach (var sql in new[]
        {
            "DELETE FROM appeals.endorsement WHERE reason_id IN (SELECT id FROM appeals.reason WHERE author_user_id = @user)",
            "UPDATE appeals.reason SET merged_into_id = NULL WHERE merged_into_id IN (SELECT id FROM appeals.reason WHERE author_user_id = @user)",
            "DELETE FROM appeals.reason WHERE author_user_id = @user",
            "DELETE FROM appeals.report WHERE reporter_user_id = @user",
            "DELETE FROM appeals.flag WHERE reporter_user_id = @user",

            // With the account (design 8.3); no hash of the address is kept anywhere.
            "DELETE FROM appeals.writing_restriction WHERE user_id = @user",

            // A moderator's draft requests go with the account (design 8.11.4).
            "DELETE FROM appeals.ai_draft_request WHERE moderator_user_id = @user",

            // Design 8.14.14: representative roles go; who registered, granted, asked for a code, made a link, wrote a
            // statement or changed a profile is emptied. The organisations and their statements stay.
            "DELETE FROM appeals.organisation_representative WHERE user_id = @user",
            "UPDATE appeals.organisation_representative SET granted_by_user_id = NULL WHERE granted_by_user_id = @user",
            "UPDATE appeals.organisation SET registered_by_user_id = NULL WHERE registered_by_user_id = @user",
            "UPDATE appeals.organisation_statement SET written_by_user_id = NULL WHERE written_by_user_id = @user",
            "UPDATE appeals.organisation_invite SET created_by_user_id = NULL WHERE created_by_user_id = @user",
            "UPDATE appeals.organisation_code SET requested_by_user_id = NULL WHERE requested_by_user_id = @user",
            "UPDATE appeals.revision SET changed_by_user_id = NULL WHERE changed_by_user_id = @user AND entity_kind IN ('organisation', 'statement')",
        })
        {
#pragma warning disable CA2100 // Constant SQL.
            await using var command = new NpgsqlCommand(sql, connection, npgsql);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("user", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Deleting the account of an organisation's last representative closes it as closing does (design 8.14.14): its
    /// statements on open bills are withdrawn. Runs in the caller's transaction, before the roles are deleted.
    /// </summary>
    private async Task EraseOrganisationRolesAsync(Guid userId, NpgsqlTransaction transaction, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<AppealsDb>()
            .UseNpgsql(transaction.Connection!, o => o.MigrationsHistoryTable("__ef_migrations_history", AppealsModule.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppealsDb(options);
        await db.Database.UseTransactionAsync(transaction, ct);
        var mine = await db.OrganisationRepresentatives.AsNoTracking()
            .Where(r => r.UserId == userId && r.EndedAt == null)
            .Select(r => r.OrganisationId)
            .ToListAsync(ct);
        foreach (var id in mine)
        {
            await OrganisationReads.LockAsync(db, id, ct);
            var o = await db.Organisations.FirstAsync(x => x.Id == id, ct);
            var others = await db.OrganisationRepresentatives.CountAsync(r => r.OrganisationId == id && r.EndedAt == null && r.UserId != userId, ct);
            if (others == 0 && o.Status != OrganisationStatuses.Closed)
            {
                await OrganisationsService.CloseOrganisationAsync(db, organisations, codes, transaction, o, clock.GetUtcNow(), ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}