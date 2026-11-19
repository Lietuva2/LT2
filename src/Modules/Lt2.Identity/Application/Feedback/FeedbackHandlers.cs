using System.Text.Json;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Feedback;

/// <summary>
/// Feedback from members to the team (design 8.10). An item is linked to the account only when the member allows a reply;
/// otherwise it has a random id, no account and only the day. Staff never see an address: replies go through the outbox.
/// Nothing here reads sides, and no item text is logged. A context of its own per operation.
/// </summary>
internal sealed class FeedbackHandlers(IDbContextFactory<IdentityDb> contexts, IOutbox outbox, IAuditLog audit, TimeProvider clock) : IFeedback
{
    public const string StaffPath = "/moderavimas/atsiliepimai";


    public async Task<Result<Unit>> SendAsync(Guid userId, NewFeedback feedback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        if (FeedbackRules.CheckText(feedback.Text) is { } text)
        {
            return Error.Validation(text, "text");
        }

        if (FeedbackRules.CheckKind(feedback.Kind) is { } kind)
        {
            return Error.Validation(kind, "kind");
        }

        // A member, not a pending account. A writing restriction (design 8.3) does not stop feedback.
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.RegisteredAt != null, cancellationToken))
        {
            return Error.Forbidden("member.required");
        }

        db.Feedback.Add(new FeedbackItem
        {
            // Linked: an ordinary time-ordered id. Unlinked: a random id, so not even the id tells when it came.
            Id = feedback.MayReply ? Ids.New(clock) : Guid.NewGuid(),
            UserId = feedback.MayReply ? userId : null,
            Kind = feedback.Kind,
            Text = feedback.Text.Trim(),
            PagePath = FeedbackRules.PagePath(feedback.PagePath),
            Browser = FeedbackRules.BrowserAsSent(feedback.Browser),
            CreatedOn = VilniusTime.Today(clock),
        });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<FeedbackInfo>> ListAsync(string? kind, string? status, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var items = await db.Feedback.AsNoTracking()
            .Where(f => (kind == null || f.Kind == kind) && (status == null || f.Status == status))
            .OrderByDescending(f => f.CreatedOn).ThenBy(f => f.Status)
            .Take(200)
            .ToListAsync(cancellationToken);
        var linked = items.Where(f => f.UserId != null).Select(f => f.UserId!.Value).Distinct().ToList();
        var names = await db.Profiles.AsNoTracking().Where(p => linked.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.DisplayName, cancellationToken);
        return items.Select(f => new FeedbackInfo(
                f.Id, f.Kind, f.Text, f.Status, f.Note, f.PagePath, f.Browser, f.CreatedOn, f.UserId != null,
                f.UserId is { } user ? names.GetValueOrDefault(user) : null,
                Replies(f.Replies)))
            .ToList();
    }

    public async Task<Result<Unit>> UpdateAsync(Guid id, string status, string? note, StaffActor staff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staff);
        if (!FeedbackRules.Statuses.Contains(status))
        {
            return Error.Validation("feedback.status", "status");
        }

        if (note is { Length: > 1000 })
        {
            return Error.Validation("feedback.note", "note");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = await db.Feedback.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("feedback.not-found");
        }

        var from = item.Status;
        var newNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var noteChanged = !string.Equals(item.Note, newNote, StringComparison.Ordinal);
        item.Status = status;
        item.Note = newNote;
        await db.SaveChangesAsync(cancellationToken);

        // Design 6.9: who changed what, never the note's or the feedback's text.
        await audit.WriteAsync(
            new AuditEntry(
                "feedback.updated",
                staff.UserId,
                staff.Role,
                "feedback:" + id,
                JsonSerializer.SerializeToElement(new { from, to = status, noteChanged }, JsonSerializerOptions.Web),
                staff.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ReplyAsync(Guid id, string text, StaffActor staff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staff);
        if (FeedbackRules.CheckText(text) is not null)
        {
            return Error.Validation("feedback.reply", "text");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = await db.Feedback.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("feedback.not-found");
        }

        var email = item.UserId is { } userId ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken) : null;
        if (email is null)
        {
            // Unlinked (or the account is gone): there is nobody to reply to.
            return Error.Conflict("feedback.not-linked");
        }

        var now = clock.GetUtcNow();
        var replies = Replies(item.Replies).Append(new FeedbackReply(now, text.Trim())).ToList();
        item.Replies = JsonSerializer.Serialize(replies, JsonSerializerOptions.Web);
        if (item.Status == "new")
        {
            item.Status = "seen";
        }

        await db.SaveChangesAsync(cancellationToken);
        var excerpt = item.Text.Length > 120 ? item.Text[..120] + "…" : item.Text;
        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.FeedbackReplyEmail, new FeedbackReplyEmail(email, text.Trim(), excerpt)),
            transaction.GetDbTransaction(),
            cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("feedback.replied", staff.UserId, staff.Role, "feedback:" + id, null, staff.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<int> SendNoticeAsync(string? teamAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(teamAddress))
        {
            return 0;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var count = await db.Feedback.Where(f => !f.Noticed).ExecuteUpdateAsync(s => s.SetProperty(f => f.Noticed, true), cancellationToken);
        if (count > 0)
        {
            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.FeedbackNoticeEmail, new FeedbackNoticeEmail(teamAddress.Trim(), count, StaffPath)),
                transaction.GetDbTransaction(),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    /// <summary>The daily clean-up (design 8.10): items older than the setting's months go.</summary>
    internal static Task<int> DeleteOldAsync(IdentityDb db, DateOnly keepFrom, CancellationToken ct) =>
        db.Feedback.Where(f => f.CreatedOn < keepFrom).ExecuteDeleteAsync(ct);


    private static List<FeedbackReply> Replies(string json) =>
        JsonSerializer.Deserialize<List<FeedbackReply>>(json, JsonSerializerOptions.Web) ?? [];
}
