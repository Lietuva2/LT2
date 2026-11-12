using System.Data.Common;
using System.Text.Json;
using Lt2.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Notifications.Outbox;

/// <summary>Inserts outbox rows on the caller's connection and transaction, so they commit with the change.</summary>
internal sealed class OutboxWriter(TimeProvider clock) : IOutbox
{
    public async Task EnqueueAsync(OutboxMessage message, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction is not NpgsqlTransaction npgsqlTransaction || npgsqlTransaction.Connection is null)
        {
            throw new ArgumentException("The outbox needs an open PostgreSQL transaction.", nameof(transaction));
        }

        var now = clock.GetUtcNow();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO notify.outbox (id, kind, payload, created_at, due_at, attempts)
            VALUES (@id, @kind, @payload, @now, @due, 0)
            """,
            npgsqlTransaction.Connection,
            npgsqlTransaction);
        command.Parameters.AddWithValue("id", Ids.New(clock));
        command.Parameters.AddWithValue("kind", message.Kind);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, message.Payload.GetRawText());
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("due", message.DueAt ?? now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Sends one kind of outbox message.</summary>
internal interface IOutboxHandler
{
    string Kind { get; }

    Task HandleAsync(JsonElement payload, CancellationToken cancellationToken);
}
