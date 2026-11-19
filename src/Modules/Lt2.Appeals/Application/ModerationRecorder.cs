using System.Text.Json;
using System.Text.Json.Serialization;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// What every moderator's action leaves behind, in the action's transaction (design 8.3, 8.14.14): a row of
/// <c>appeals.moderation_action</c> with the statement, an audit entry (moderator, target, statement, the bill and other
/// details; never a side or a text written by a member), and, for an action that answers them (design 6.8), the closing of
/// the open reports on the target.
/// </summary>
internal sealed class ModerationRecorder(IAuditLog audit, ReportNotices notices, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Details = new(JsonSerializerOptions.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <param name="kind">The target's kind: <c>record</c>, <c>reason</c>, <c>restriction</c>, <c>organisation</c> or <c>statement</c>.</param>
    /// <param name="bill">The bill the action is about, when there is one.</param>
    /// <param name="details">Further details for the audit entry, or null.</param>
    /// <param name="answersReports">The action answers the open reports on its target, which then close.</param>
    /// <param name="auditAction">
    /// The audit entry's action when it is not <c>moderation.{kind}.{action}</c>: a moderator's close and reopening of a record
    /// are <c>record.closed</c> and <c>record.reopened</c>, as the closing job's close is (design 8.1 step 6).
    /// </param>
    public async Task RecordAsync(
        AppealsDb db, IDbContextTransaction transaction, string kind, Guid targetId, string action, string? statement, StaffActor moderator,
        DateTimeOffset now, string? bill, object? details, bool answersReports, CancellationToken ct, string? auditAction = null)
    {
        db.ModerationActions.Add(new ModerationAction
        {
            Id = Ids.New(clock),
            ModeratorUserId = moderator.UserId,
            TargetKind = kind,
            TargetId = targetId,
            Action = action,
            Statement = statement,
            CreatedAt = now,
        });
        if (answersReports)
        {
            // Owner, 2026-11-17: each member whose report was open is told the decision, with the explanation when there is one.
            await notices.TellAsync(db, transaction, targetId, ReportOutcomes.Of(action), statement, ct);
            await CloseReportsAsync(db, targetId, now, ct);
        }

        await audit.WriteAsync(
            new AuditEntry(
                auditAction ?? $"moderation.{kind}.{action}", moderator.UserId, moderator.Role, $"{kind}:{targetId}",
                JsonSerializer.SerializeToElement(new { bill, statement, extra = details }, Details), moderator.IpHash),
            transaction.GetDbTransaction(),
            ct);
    }

    /// <summary>Closes the open reports on a target: it was answered, or it is gone.</summary>
    public static Task<int> CloseReportsAsync(AppealsDb db, Guid targetId, DateTimeOffset now, CancellationToken ct) =>
        db.Reports.Where(r => r.TargetId == targetId && r.Status == "open")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "closed").SetProperty(r => r.ClosedAt, now), ct);
}
