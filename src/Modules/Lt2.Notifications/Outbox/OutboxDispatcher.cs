using Lt2.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Notifications.Outbox;

/// <summary>
/// Takes due rows with <c>FOR UPDATE SKIP LOCKED</c>, so several workers never send the same message, renders each with
/// its kind's template and sends the batch over one connection; deletes a row on success and reschedules it with backoff
/// on failure. Delivery is at least once. Codes go first (design 6.8), so bulk mail cannot hold one past its lifetime.
/// </summary>
internal sealed class OutboxDispatcher(
    NpgsqlDataSource dataSource,
    IEnumerable<IEmailRenderer> renderers,
    IEmailTransport transport,
    IOptions<OutboxOptions> options,
    MpMail.MailKeys keys,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : INotificationsDispatcher
{
    /// <summary>Kinds a person is waiting for with a code in hand: sent before any other due message.</summary>
    internal static readonly string[] FirstKinds = [MessageKinds.SignInCodeEmail, MessageKinds.OrganisationCodeEmail];

    /// <summary>How long a message that gave up is kept for a person to look at (design 6.8).</summary>
    internal static readonly TimeSpan KeepFailed = TimeSpan.FromDays(30);

    private readonly Dictionary<string, IEmailRenderer> _renderers = renderers.ToDictionary(h => h.Kind, StringComparer.Ordinal);

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
            ORDER BY kind = ANY(@first) DESC, due_at
            LIMIT @batch
            FOR UPDATE SKIP LOCKED
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("now", clock.GetUtcNow());
            select.Parameters.AddWithValue("first", NpgsqlDbType.Array | NpgsqlDbType.Text, FirstKinds);
            select.Parameters.AddWithValue("batch", options.Value.BatchSize);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
            }
        }

        if (rows.Count > 0)
        {
            await using var mail = await transport.ConnectAsync(cancellationToken);
            foreach (var row in rows)
            {
                await SendAsync(connection, transaction, mail, row, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<int> PurgeFailedAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM notify.outbox WHERE failed_at IS NOT NULL AND failed_at < @cutoff");
        command.Parameters.AddWithValue("cutoff", clock.GetUtcNow() - KeepFailed);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>10 s, 20 s, 40 s … capped at one hour.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(3600, 10 * Math.Pow(2, Math.Max(0, attempts - 1))));

    /// <summary>
    /// What is stored and logged of a failed send (design 6.8): the error's type and the mail server's status code, never
    /// its text, which can quote the recipient's address.
    /// </summary>
    internal static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error is MailKit.Net.Smtp.SmtpCommandException smtp
            ? $"{error.GetType().Name} {(int)smtp.StatusCode}"
            : error.GetType().Name;
    }

    /// <summary>
    /// A permanent refusal of the recipient by the receiving server (design 8.19.3): an SMTP 5xx answer to the recipient. A
    /// temporary (4xx) answer, a refused sender or a lost connection is retried as for any email.
    /// </summary>
    internal static bool PermanentRefusal(Exception error) =>
        error is MailKit.Net.Smtp.SmtpCommandException { ErrorCode: MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted } smtp
        && (int)smtp.StatusCode is >= 500 and < 600;

    /// <summary>The delivery row an MP email stands for, read from its payload; null for any other email.</summary>
    private static (Guid RunId, string MpSlug, string Recipient, Guid OfficeUserId)? DeliveryOf((Guid Id, string Kind, string Payload, int Attempts) row)
    {
        if (!MpMail.MpMailKinds.All.Contains(row.Kind))
        {
            return null;
        }

        using var payload = System.Text.Json.JsonDocument.Parse(row.Payload);
        if (!payload.RootElement.TryGetProperty("delivery", out var d) || d.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        // An office member's copy names the member (design 8.19.3); every other row has none.
        var office = d.TryGetProperty("officeUserId", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.String ? o.GetGuid() : Guid.Empty;
        return (d.GetProperty("runId").GetGuid(), d.GetProperty("mpSlug").GetString()!, d.GetProperty("recipient").GetString()!, office);
    }

    private static async Task MarkDeliveryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, (Guid Id, string Kind, string Payload, int Attempts) row, string status, CancellationToken cancellationToken)
    {
        if (DeliveryOf(row) is not { } delivery)
        {
            return;
        }

        await using var command = new NpgsqlCommand(
            "UPDATE notify.digest_delivery SET status = @status WHERE run_id = @run AND mp_slug = @mp AND recipient = @recipient AND office_user_id = @office AND status = 'queued'",
            connection,
            transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("run", delivery.RunId);
        command.Parameters.AddWithValue("mp", delivery.MpSlug);
        command.Parameters.AddWithValue("recipient", delivery.Recipient);
        command.Parameters.AddWithValue("office", delivery.OfficeUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The refused address on the suppression list (its keyed hash, reason <c>bounced</c>), the delivery bounced, the row gone.</summary>
    private async Task SuppressAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, (Guid Id, string Kind, string Payload, int Attempts) row, CancellationToken cancellationToken)
    {
        string to;
        using (var payload = System.Text.Json.JsonDocument.Parse(row.Payload))
        {
            to = payload.RootElement.GetProperty("to").GetString() ?? "";
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO notify.suppression (id, address_hash, reason, created_at)
            VALUES (@id, @hash, 'bounced', @now)
            ON CONFLICT (address_hash) DO NOTHING
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("id", Lt2.SharedKernel.Ids.New(clock));
            insert.Parameters.AddWithValue("hash", keys.AddressHash(to));
            insert.Parameters.AddWithValue("now", clock.GetUtcNow());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await MarkDeliveryAsync(connection, transaction, row, Digests.DeliveryStatuses.Bounced, cancellationToken);
        await ExecuteAsync(connection, transaction, "DELETE FROM notify.outbox WHERE id = @id", row.Id, null, cancellationToken);
    }

    private async Task SendAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEmailConnection mail, (Guid Id, string Kind, string Payload, int Attempts) row, CancellationToken cancellationToken)
    {
        try
        {
            if (!_renderers.TryGetValue(row.Kind, out var renderer))
            {
                throw new InvalidOperationException($"No template for outbox kind '{row.Kind}'.");
            }

            using var payload = System.Text.Json.JsonDocument.Parse(row.Payload);
            await mail.SendAsync(renderer.Render(payload.RootElement), cancellationToken);
            await ExecuteAsync(connection, transaction, "DELETE FROM notify.outbox WHERE id = @id", row.Id, null, cancellationToken);
            await MarkDeliveryAsync(connection, transaction, row, Digests.DeliveryStatuses.Sent, cancellationToken);
            logger.LogInformation("Outbox message {MessageId} of kind {Kind} sent", row.Id, row.Kind);
        }
#pragma warning disable CA1031 // One failing message must not stop the others; the error is recorded on the row.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            var attempts = row.Attempts + 1;
            var giveUp = attempts >= options.Value.MaxAttempts;
            var error = Describe(ex);
            if (MpMail.MpMailKinds.All.Contains(row.Kind) && PermanentRefusal(ex))
            {
                // Design 8.19.3, 8.19.6: the receiving server refused this MP email for good. The address goes on the
                // suppression list (as a keyed hash only) and the delivery is marked bounced; nothing is retried.
                await SuppressAsync(connection, transaction, row, cancellationToken);
                logger.LogWarning("Outbox message {MessageId} of kind {Kind} refused for good ({Error}); the address is suppressed", row.Id, row.Kind, error);
                return;
            }

            logger.LogWarning("Outbox message {MessageId} of kind {Kind} failed (attempt {Attempt}): {Error}", row.Id, row.Kind, attempts, error);
            if (giveUp)
            {
                await MarkDeliveryAsync(connection, transaction, row, Digests.DeliveryStatuses.Failed, cancellationToken);
            }

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
                    command.Parameters.AddWithValue("error", error);
                    command.Parameters.AddWithValue("due", now + Backoff(attempts));
                    command.Parameters.AddWithValue("giveUp", giveUp);
                },
                cancellationToken);
        }
    }

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
