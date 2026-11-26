using System.Text.Json;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Notifications;

/// <summary>One bill of an invented past summary, frozen as the summary would have frozen it (design 8.4, 8.19.2).</summary>
public sealed record InventedDigestItem(
    Guid RecordId,
    string BillNumber,
    string BillTitle,
    int For,
    int ForOlderThan30Days,
    int Against,
    int AgainstOlderThan30Days,
    int ForReasons,
    int AgainstReasons,
    IReadOnlyList<DigestReason> Reasons);

/// <summary>One invented past weekly summary: its Monday, when it went (07:00 Vilnius) and its bills in their order.</summary>
public sealed record InventedDigestRun(Guid Id, DateOnly Monday, DateTimeOffset At, IReadOnlyList<InventedDigestItem> Items);

/// <summary>
/// The trial's invented past summaries (the migrator's <c>--demo-activity</c>, design 8.26.5), under its refusals: weekly
/// summaries stored as dry runs only, as a summary made while sending is off is stored (8.19.7), with each sitting MP's
/// delivery row as a dry run. Never <c>sent</c>, never an outbox row: nothing can be sent from them. Nothing here runs in the
/// application.
/// </summary>
public static class InventedDigests
{
    /// <summary>
    /// Writes each run whose week has no summary yet (a week that has one, of any status, is left as it is, as the weekly
    /// rule leaves it). Returns how many runs were written.
    /// </summary>
    public static async Task<int> WriteDryRunsAsync(
        string connectionString, IReadOnlyList<InventedDigestRun> runs, IReadOnlyList<(string Slug, long SeimasPersonId)> mps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(mps);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var written = 0;
        foreach (var run in runs.Where(r => r.Items.Count > 0))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var exists = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM notify.digest_run WHERE kind = 'weekly' AND run_date >= @monday AND run_date < @next)",
                connection,
                transaction))
            {
                exists.Parameters.AddWithValue("monday", run.Monday);
                exists.Parameters.AddWithValue("next", run.Monday.AddDays(7));
                if ((bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
                {
                    continue;
                }
            }

            await using (var insert = new NpgsqlCommand(
                """
                INSERT INTO notify.digest_run (id, kind, run_date, created_at, due_at, status, mp_count, sent_at, sent_count)
                VALUES (@id, 'weekly', @monday, @created, @at, @status, @mps, @at, 0)
                """,
                connection,
                transaction))
            {
                insert.Parameters.AddWithValue("id", run.Id);
                insert.Parameters.AddWithValue("monday", run.Monday);
                insert.Parameters.AddWithValue("created", run.At.AddHours(-24));
                insert.Parameters.AddWithValue("at", run.At);
                insert.Parameters.AddWithValue("status", DigestRunStatuses.DryRun);
                insert.Parameters.AddWithValue("mps", mps.Count);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            var position = 0;
            foreach (var item in run.Items)
            {
                await using var row = new NpgsqlCommand(
                    """
                    INSERT INTO notify.digest_item (id, run_id, position, record_id, bill_number, bill_title, for_count, for_older_than30days,
                      against_count, against_older_than30days, for_reason_count, against_reason_count, reasons, organisations)
                    VALUES (@id, @run, @position, @record, @bill, @title, @for, @forOlder, @against, @againstOlder, @forReasons, @againstReasons,
                      CAST(@reasons AS jsonb), '[]'::jsonb)
                    """,
                    connection,
                    transaction);
                row.Parameters.AddWithValue("id", Guid.CreateVersion7(run.At.AddMinutes(position)));
                row.Parameters.AddWithValue("run", run.Id);
                row.Parameters.AddWithValue("position", ++position);
                row.Parameters.AddWithValue("record", item.RecordId);
                row.Parameters.AddWithValue("bill", item.BillNumber);
                row.Parameters.AddWithValue("title", item.BillTitle);
                row.Parameters.AddWithValue("for", item.For);
                row.Parameters.AddWithValue("forOlder", item.ForOlderThan30Days);
                row.Parameters.AddWithValue("against", item.Against);
                row.Parameters.AddWithValue("againstOlder", item.AgainstOlderThan30Days);
                row.Parameters.AddWithValue("forReasons", item.ForReasons);
                row.Parameters.AddWithValue("againstReasons", item.AgainstReasons);
                row.Parameters.AddWithValue("reasons", JsonSerializer.Serialize(item.Reasons, JsonSerializerOptions.Web));
                await row.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deliveries = await connection.BeginBinaryImportAsync(
                "COPY notify.digest_delivery (run_id, mp_slug, recipient, office_user_id, seimas_person_id, status, party_numbers) FROM STDIN (FORMAT BINARY)",
                cancellationToken))
            {
                foreach (var (slug, personId) in mps)
                {
                    await deliveries.StartRowAsync(cancellationToken);
                    await deliveries.WriteAsync(run.Id, NpgsqlTypes.NpgsqlDbType.Uuid, cancellationToken);
                    await deliveries.WriteAsync(slug, NpgsqlTypes.NpgsqlDbType.Varchar, cancellationToken);
                    await deliveries.WriteAsync(Digests.Recipients.Official, NpgsqlTypes.NpgsqlDbType.Varchar, cancellationToken);
                    await deliveries.WriteAsync(Guid.Empty, NpgsqlTypes.NpgsqlDbType.Uuid, cancellationToken);
                    await deliveries.WriteAsync(personId, NpgsqlTypes.NpgsqlDbType.Bigint, cancellationToken);
                    await deliveries.WriteAsync(Digests.DeliveryStatuses.DryRun, NpgsqlTypes.NpgsqlDbType.Varchar, cancellationToken);
                    await deliveries.WriteAsync("[]", NpgsqlTypes.NpgsqlDbType.Jsonb, cancellationToken);
                }

                await deliveries.CompleteAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            written++;
        }

        return written;
    }
}
