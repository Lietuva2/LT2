using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lt2.Notifications.Outbox;

/// <summary>
/// Takes due rows with <c>FOR UPDATE SKIP LOCKED</c>, so several workers never send the same message, hands each to its
/// handler, deletes it on success and reschedules it with backoff on failure. Delivery is at least once.
/// </summary>
internal sealed class OutboxDispatcher(
    NpgsqlDataSource dataSource,
    IEnumerable<IOutboxHandler> handlers,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : INotificationsDispatcher
{
    private readonly Dictionary<string, IOutboxHandler> _handlers = handlers.ToDictionary(h => h.Kind, StringComparer.Ordinal);

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var rows = new List<(Guid Id, string Kind, string Payload, int Attempts)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT id, kind, payload::text, attempts
            FROM notify.outbox
            WHERE failed_at IS NULL AND due_at <= @now
            ORDER BY due_at
            LIMIT @batch
            FOR UPDATE SKIP LOCKED
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("now", clock.GetUtcNow());
            select.Parameters.AddWithValue("batch", options.Value.BatchSize);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
            }
        }

        foreach (var row in rows)
        {
            try
            {
                if (!_handlers.TryGetValue(row.Kind, out var handler))
                {
                    throw new InvalidOperationException($"No handler for outbox kind '{row.Kind}'.");
                }

                using var payload = JsonDocument.Parse(row.Payload);
                await handler.HandleAsync(payload.RootElement, cancellationToken);
                await ExecuteAsync(connection, transaction, "DELETE FROM notify.outbox WHERE id = @id", row.Id, null, cancellationToken);
                logger.LogInformation("Outbox message {MessageId} of kind {Kind} sent", row.Id, row.Kind);
            }
#pragma warning disable CA1031 // One failing message must not stop the others; the error is recorded on the row.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                var attempts = row.Attempts + 1;
                var giveUp = attempts >= options.Value.MaxAttempts;
                logger.LogWarning(ex, "Outbox message {MessageId} of kind {Kind} failed (attempt {Attempt})", row.Id, row.Kind, attempts);
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    UPDATE notify.outbox
                    SET attempts = @attempts, last_attempt_at = @now, last_error = @error,
                        due_at = @due, failed_at = CASE WHEN @giveUp THEN @now ELSE NULL END
                    WHERE id = @id
                    """,
                    row.Id,
                    command =>
                    {
                        var now = clock.GetUtcNow();
                        command.Parameters.AddWithValue("attempts", attempts);
                        command.Parameters.AddWithValue("now", now);
                        command.Parameters.AddWithValue("error", Truncate(ex.GetType().Name + ": " + ex.Message, 2000));
                        command.Parameters.AddWithValue("due", now + Backoff(attempts));
                        command.Parameters.AddWithValue("giveUp", giveUp);
                    },
                    cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    /// <summary>10 s, 20 s, 40 s … capped at one hour.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(3600, 10 * Math.Pow(2, Math.Max(0, attempts - 1))));

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid id,
        Action<NpgsqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        configure?.Invoke(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
