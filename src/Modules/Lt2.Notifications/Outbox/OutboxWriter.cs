using System.Data.Common;
using System.Text.Json;
using Lt2.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Notifications.Outbox;

/// <summary>Inserts and forgets outbox rows on the caller's connection and transaction, so they commit with the change.</summary>
internal sealed class OutboxWriter(TimeProvider clock) : IOutbox
{
    public async Task EnqueueAsync(OutboxMessage message, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var npgsql = OpenTransaction(transaction);
        var now = clock.GetUtcNow();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO notify.outbox (id, kind, payload, created_at, due_at, attempts)
            VALUES (@id, @kind, @payload, @now, @due, 0)
            """,
            npgsql.Connection,
            npgsql);
        command.Parameters.AddWithValue("id", Ids.New(clock));
        command.Parameters.AddWithValue("kind", message.Kind);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, message.Payload.GetRawText());
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("due", message.DueAt ?? now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ForgetAsync(IReadOnlyCollection<string> addresses, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var npgsql = OpenTransaction(transaction);
        if (addresses.Count == 0)
        {
            return 0;
        }

        // Every email payload names its recipient as "to" (see MessageKinds); addresses are compared without case.
        await using var command = new NpgsqlCommand(
            "DELETE FROM notify.outbox WHERE lower(payload->>'to') = ANY(@addresses)",
            npgsql.Connection,
            npgsql);
        command.Parameters.AddWithValue("addresses", addresses.Select(a => a.Trim().ToLowerInvariant()).Distinct().ToArray());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlTransaction OpenTransaction(DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction is NpgsqlTransaction { Connection: not null } npgsql
            ? npgsql
            : throw new ArgumentException("The outbox needs an open PostgreSQL transaction.", nameof(transaction));
    }
}

/// <summary>Turns one kind of outbox message into the email it sends.</summary>
internal interface IEmailRenderer
{
    string Kind { get; }

    EmailMessage Render(JsonElement payload);
}

/// <summary>The one <see cref="IEmailRenderer"/>: reads the payload as <typeparamref name="TModel"/> and hands it to its template.</summary>
internal sealed class EmailRenderer<TModel>(string kind, Func<TModel, EmailMessage> render) : IEmailRenderer
    where TModel : class
{
    public string Kind => kind;

    public EmailMessage Render(JsonElement payload) =>
        render(payload.Deserialize<TModel>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException($"Empty payload of kind '{kind}'."));
}
