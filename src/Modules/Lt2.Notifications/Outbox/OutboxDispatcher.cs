using System.Collections.Concurrent;
using Lt2.Notifications.Transports;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Notifications.Outbox;

/// <summary>
/// Takes due rows with <c>FOR UPDATE SKIP LOCKED</c>, so several workers never send the same message, renders each with
/// its kind's template and sends the batch over one connection; deletes a row on success and reschedules it with backoff
/// on failure. Delivery is at least once. Codes go first (design 6.8), so bulk mail cannot hold one past its lifetime.
/// A stream (design 8.19.3) that has sent its daily ceiling, or whose sender or account the mail server refused, is left out
/// of the batch: its messages wait untouched while the other streams go on.
/// </summary>
internal sealed class OutboxDispatcher(
    NpgsqlDataSource dataSource,
    IEnumerable<IEmailRenderer> renderers,
    IEmailTransport transport,
    IOptions<OutboxOptions> options,
    MpMail.MailKeys keys,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : INotificationsDispatcher
{
    /// <summary>Kinds on the codes stream: sent before any other due message, so bulk mail cannot hold a code past its lifetime.</summary>
    internal static readonly string[] FirstKinds = EmailStreams.KindsOf(EmailStreams.Codes);

    /// <summary>How long a message that gave up is kept for a person to look at (design 6.8).</summary>
    internal static readonly TimeSpan KeepFailed = TimeSpan.FromDays(30);

    /// <summary>How long the count of sends is kept: the ceiling counts 24 hours, and a day's margin is plenty.</summary>
    internal static readonly TimeSpan KeepSends = TimeSpan.FromDays(2);

    private readonly Dictionary<string, IEmailRenderer> _renderers = renderers.ToDictionary(h => h.Kind, StringComparer.Ordinal);

    /// <summary>Until when each stream is paused because its sender or account was refused; this worker's memory, so a restart tries again.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pausedUntil = new(StringComparer.Ordinal);

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();

        var remaining = await RemainingAsync(connection, transaction, now, cancellationToken);
        var held = EmailStreams.All.Where(s => remaining[s] <= 0 || IsPaused(s, now)).ToList();
        var heldKinds = held.SelectMany(EmailStreams.KindsOf).ToArray();

        var rows = new List<(Guid Id, string Kind, string Payload, int Attempts)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT id, kind, payload::text, attempts
            FROM notify.outbox
            WHERE failed_at IS NULL AND due_at <= @now AND kind <> ALL(@held)
            ORDER BY kind = ANY(@first) DESC, due_at
            LIMIT @batch
            FOR UPDATE SKIP LOCKED
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("now", now);
            select.Parameters.AddWithValue("first", NpgsqlDbType.Array | NpgsqlDbType.Text, FirstKinds);
            select.Parameters.AddWithValue("held", NpgsqlDbType.Array | NpgsqlDbType.Text, heldKinds);
            select.Parameters.AddWithValue("batch", options.Value.BatchSize);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
            }
        }

        var handled = 0;
        if (rows.Count > 0)
        {
            await using var mail = await transport.ConnectAsync(cancellationToken);
            foreach (var row in rows)
            {
                var stream = StreamOf(row.Kind);
                if (stream is not null && (remaining[stream] <= 0 || IsPaused(stream, clock.GetUtcNow())))
                {
                    // The ceiling was reached, or the server refused the stream's sender, earlier in this batch: the row waits as it is.
                    continue;
                }

                handled++;
                switch (await SendAsync(connection, transaction, mail, row, stream, cancellationToken))
                {
                    case Outcome.Sent when stream is not null:
                        remaining[stream] = remaining[stream] == int.MaxValue ? int.MaxValue : remaining[stream] - 1;
                        break;
                    case Outcome.SenderRefused when stream is not null:
                        // Not the recipient's fault and not a failed attempt: the stream stops for a while and the row stays due.
                        _pausedUntil[stream] = clock.GetUtcNow() + SmtpRefusals.StreamPause;
                        remaining[stream] = 0;
                        break;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return handled;
    }

    public async Task<int> PurgeFailedAsync(CancellationToken cancellationToken)
    {
        await using var sends = dataSource.CreateCommand("DELETE FROM notify.stream_send WHERE sent_at < @cutoff");
        sends.Parameters.AddWithValue("cutoff", clock.GetUtcNow() - KeepSends);
        await sends.ExecuteNonQueryAsync(cancellationToken);

        await using var command = dataSource.CreateCommand("DELETE FROM notify.outbox WHERE failed_at IS NOT NULL AND failed_at < @cutoff");
        command.Parameters.AddWithValue("cutoff", clock.GetUtcNow() - KeepFailed);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The stream of a kind; null for a kind without one, which then fails like a kind without a template.</summary>
    private static string? StreamOf(string kind) => EmailStreams.Kinds.Contains(kind) ? EmailStreams.Of(kind) : null;

    private bool IsPaused(string stream, DateTimeOffset now) => _pausedUntil.TryGetValue(stream, out var until) && now < until;

    /// <summary>
    /// How many more emails each stream may send now under its daily ceiling (design 8.19.3). With any ceiling set, the count is
    /// taken under an advisory lock held to the commit, so two workers cannot both spend the last of it; with none set no lock is taken.
    /// </summary>
    private async Task<Dictionary<string, int>> RemainingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ceilings = new Dictionary<string, int>();
        await using (var scope = scopes.CreateAsyncScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettings>();
            foreach (var stream in EmailStreams.All)
            {
                ceilings[stream] = await settings.GetAsync(StreamCeilings.KeyOf(stream), cancellationToken);
            }
        }

        var sent = new Dictionary<string, int>();
        if (ceilings.Values.Any(c => c > 0))
        {
            await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('lt2.notify.stream-ceilings', 0))", connection, transaction))
            {
                await gate.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var count = new NpgsqlCommand(
                "SELECT stream, count(*) FROM notify.stream_send WHERE sent_at > @since GROUP BY stream", connection, transaction);
            count.Parameters.AddWithValue("since", now - StreamCeilings.Window);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sent[reader.GetString(0)] = (int)Math.Min(int.MaxValue, reader.GetInt64(1));
            }
        }

        return EmailStreams.All.ToDictionary(s => s, s => StreamCeilings.Remaining(ceilings[s], sent.GetValueOrDefault(s)));
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
    /// A permanent refusal of the recipient's mailbox by the receiving server (design 8.19.3, 8.19.6): an SMTP 5xx answer to the
    /// recipient that is about the address or the mailbox. A temporary (4xx) answer, a refused sender or account (a limit, a
    /// quota, the login: <see cref="SmtpRefusals.Classify"/>) or a lost connection is never one.
    /// </summary>
    internal static bool PermanentRefusal(Exception error) => SmtpRefusals.Classify(error) == SendFailure.Mailbox;

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

    private enum Outcome
    {
        Sent,
        Failed,
        Suppressed,
        SenderRefused,
    }

    private async Task<Outcome> SendAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEmailConnection mail, (Guid Id, string Kind, string Payload, int Attempts) row, string? stream, CancellationToken cancellationToken)
    {
        try
        {
            if (!_renderers.TryGetValue(row.Kind, out var renderer))
            {
                throw new InvalidOperationException($"No template for outbox kind '{row.Kind}'.");
            }

            using var payload = System.Text.Json.JsonDocument.Parse(row.Payload);
            await mail.SendAsync(renderer.Render(payload.RootElement) with { Stream = EmailStreams.Of(row.Kind) }, cancellationToken);
            await ExecuteAsync(connection, transaction, "DELETE FROM notify.outbox WHERE id = @id", row.Id, null, cancellationToken);
            await MarkDeliveryAsync(connection, transaction, row, Digests.DeliveryStatuses.Sent, cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO notify.stream_send (id, stream, sent_at) VALUES (@id, @stream, @now)",
                Ids.New(clock),
                command =>
                {
                    command.Parameters.AddWithValue("stream", stream!);
                    command.Parameters.AddWithValue("now", clock.GetUtcNow());
                },
                cancellationToken);
            logger.LogInformation("Outbox message {MessageId} of kind {Kind} sent", row.Id, row.Kind);
            return Outcome.Sent;
        }
#pragma warning disable CA1031 // One failing message must not stop the others; the error is recorded on the row.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            var attempts = row.Attempts + 1;
            var giveUp = attempts >= options.Value.MaxAttempts;
            var error = Describe(ex);
            var failure = SmtpRefusals.Classify(ex);
            if (failure == SendFailure.Sender && stream is not null)
            {
                // Design 8.19.6: the mail server refused our sender or account (a limit, a quota, the login), which says nothing
                // about the recipient. The message stays due and untried, the stream pauses, and no address is logged.
                logger.LogWarning(
                    "Email stream {Stream} paused for {Minutes} minutes: the mail server refused the sender or the account ({Error}); message {MessageId} waits",
                    stream, (int)SmtpRefusals.StreamPause.TotalMinutes, error, row.Id);
                return Outcome.SenderRefused;
            }

            if (MpMail.MpMailKinds.All.Contains(row.Kind) && failure == SendFailure.Mailbox)
            {
                // Design 8.19.3, 8.19.6: the receiving server refused this MP email for good. The address goes on the
                // suppression list (as a keyed hash only) and the delivery is marked bounced; nothing is retried.
                await SuppressAsync(connection, transaction, row, cancellationToken);
                logger.LogWarning("Outbox message {MessageId} of kind {Kind} refused for good ({Error}); the address is suppressed", row.Id, row.Kind, error);
                return Outcome.Suppressed;
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
            return Outcome.Failed;
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
