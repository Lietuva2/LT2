using System.Data.Common;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Appeals.Application;

/// <summary><see cref="IAccountData"/>: one person's data in the appeals schema, for export and deletion (design 6.5).</summary>
internal sealed class AccountData(IDbContextFactory<AppealsDb> contexts, IPositionErasure positions) : IAccountData
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
        return new
        {
            appealsOpened = appeals, positions = myPositions, reasons, endorsements, reports, dateFlags = flags, writingRestrictions = restrictions,
            aiDraftRequests = drafts,
        };
    }

    public async Task EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await positions.EraseAsync(userId, transaction, cancellationToken);
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Erasing needs a PostgreSQL transaction.", nameof(transaction));
        }

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
        })
        {
#pragma warning disable CA2100 // Constant SQL.
            await using var command = new NpgsqlCommand(sql, connection, npgsql);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("user", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}