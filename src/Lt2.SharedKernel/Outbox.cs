using System.Data.Common;
using System.Text.Json;

namespace Lt2.SharedKernel;

/// <summary>A message to be dispatched by the worker after the transaction that wrote it commits.</summary>
/// <param name="Kind">Stable message kind, for example <c>email.sign-in-code</c>.</param>
/// <param name="Payload">JSON payload; the dispatcher for the kind knows its shape.</param>
/// <param name="DueAt">Earliest dispatch time; <c>null</c> means now.</param>
public sealed record OutboxMessage(string Kind, JsonElement Payload, DateTimeOffset? DueAt = null)
{
    public static OutboxMessage Create<T>(string kind, T payload, DateTimeOffset? dueAt = null) =>
        new(kind, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web), dueAt);
}

/// <summary>
/// Writes messages to the transactional outbox. Implemented by the Notifications module; any module may use it.
/// The caller passes its own open transaction so the message commits or rolls back with the change.
/// </summary>
public interface IOutbox
{
    Task EnqueueAsync(OutboxMessage message, DbTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every message addressed to <paramref name="addresses"/> that was not sent, waiting or failed (design 6.8:
    /// the outbox forgets a deleted account and an unfinished registration), in the caller's transaction. Returns how many.
    /// </summary>
    Task<int> ForgetAsync(IReadOnlyCollection<string> addresses, DbTransaction transaction, CancellationToken cancellationToken = default);
}
