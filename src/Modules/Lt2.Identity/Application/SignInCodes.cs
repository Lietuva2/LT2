using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application;

/// <summary>Issues sign-in codes: replaces older ones, applies the per-account limit, writes the email to the outbox.</summary>
internal sealed class SignInCodeIssuer(IdentityDb db, IOutbox outbox, TimeProvider clock, ILogger<SignInCodeIssuer> logger)
{
    /// <summary>Must be called inside <paramref name="transaction"/>, so the code and its email commit together.</summary>
    public async Task IssueAsync(User user, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var since = now - SignInCodeRules.IssueWindow;
        var recent = await db.SignInCodes.CountAsync(c => c.UserId == user.Id && c.CreatedAt > since, cancellationToken);
        if (!SignInCodeRules.MayIssue(recent))
        {
            logger.LogWarning("Sign-in code limit reached for account {UserId}; no email sent", user.Id);
            return;
        }

        await db.SignInCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        var id = Ids.New(clock);
        var code = SignInCodeRules.Generate();
        db.SignInCodes.Add(new SignInCode
        {
            Id = id,
            UserId = user.Id,
            CodeHash = SignInCodeRules.Hash(id, code),
            CreatedAt = now,
            ExpiresAt = now + SignInCodeRules.Lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        var email = new SignInCodeEmail(user.Email!, code, (int)SignInCodeRules.Lifetime.TotalMinutes);
        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.SignInCodeEmail, email),
            transaction.GetDbTransaction(),
            cancellationToken);
    }
}
