namespace Lt2.Notifications;

/// <summary>Sends due outbox messages. Called in a loop by the worker.</summary>
public interface INotificationsDispatcher
{
    /// <summary>Dispatches one batch of due messages and returns how many were handled (sent or failed).</summary>
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

/// <summary>Which sending stream a message belongs to, so a problem with one cannot block the other (design 8.4).</summary>
public enum EmailCategory
{
    /// <summary>Sign-in codes and other messages a person asked for.</summary>
    Transactional,

    /// <summary>Summaries to MPs.</summary>
    Digest,
}

/// <summary>An email ready to send: plain text always, light HTML optionally.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody, EmailCategory Category);

/// <summary>Delivers email. SMTP today; the provider is chosen later (design section 12).</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
