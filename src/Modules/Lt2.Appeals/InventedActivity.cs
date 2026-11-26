using Lt2.Appeals.Application;
using Lt2.Appeals.Domain;
using Lt2.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Appeals;

/// <summary>One invented "Man svarbu" mark, with when it was given.</summary>
public sealed record InventedMark(Guid UserId, DateTimeOffset At);

/// <summary>One invented argument on a real bill (the trial, design 8.26.5), with when it was written and its marks.</summary>
public sealed record InventedArgument(Guid AuthorUserId, string Side, string Text, DateTimeOffset At, IReadOnlyList<InventedMark> Marks);

/// <summary>One invented comment under an argument (by its place in <see cref="InventedActivitySpec.Arguments"/>), with its "Sutinku".</summary>
public sealed record InventedComment(Guid Id, int Argument, Guid AuthorUserId, string Text, DateTimeOffset At, IReadOnlyList<Guid> Agreements);

/// <summary>
/// Invented members' activity on one real bill (the trial, design 8.26.5): sides, arguments with marks, comments. Never a
/// description: a real bill's description is a moderator's or the AI's, and stays as it is.
/// </summary>
public sealed record InventedActivitySpec(
    string BillNumber,
    DateTimeOffset OpenedAt,
    IReadOnlyList<InventedPosition> Positions,
    IReadOnlyList<InventedArgument> Arguments,
    IReadOnlyList<InventedComment> Comments);

/// <summary>One bill of an invented past summary, as its numbers were at the summary's time (design 8.26.5, the trial).</summary>
public sealed record InventedSnapshot(
    Guid RecordId,
    string BillNumber,
    int For,
    int ForOlderThan30Days,
    int Against,
    int AgainstOlderThan30Days,
    int ForReasons,
    int AgainstReasons,
    IReadOnlyList<DigestReason> Reasons);

/// <summary>
/// The trial's invented members' activity on real Seimas bills (the migrator's <c>--demo-activity</c>, design 8.26.5), under
/// its refusals. Only a bill nobody real took part in is touched: every position, argument and comment on it must be by an
/// invented account (an identifier starting with <see cref="IdPrefix"/>). Nothing here runs in the application.
/// </summary>
public static class InventedActivity
{
    /// <summary>How every invented demonstration account's identifier starts (the migrator's fixed identifiers).</summary>
    public const string IdPrefix = "0193b000-";

    /// <summary>
    /// Writes the invented activity on the bill's open record, creating the record (empty description, the default threshold)
    /// when it has none. An open record with no real participant is used as it is, its description untouched; its opening is
    /// moved back to <see cref="InventedActivitySpec.OpenedAt"/> when that is earlier, so the invented sides can have been
    /// taken over weeks. Returns <see cref="InventedRecordOutcome.AlreadyThere"/> when the first invented side is there, and
    /// <see cref="InventedRecordOutcome.BillTaken"/> when a real account took part or the record is not open.
    /// </summary>
    public static async Task<(InventedRecordOutcome Outcome, Guid? RecordId)> AddToBillAsync(
        string connectionString, InventedActivitySpec spec, bool newRecordOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Positions.Any(p => !Sides.IsSide(p.Side)) || spec.Arguments.Any(r => !Sides.IsSide(r.Side)))
        {
            throw new ArgumentException("Every side is for or against.", nameof(spec));
        }

        if (spec.Positions.Count == 0)
        {
            throw new ArgumentException("Invented activity has at least one side.", nameof(spec));
        }

        if (spec.Positions.Select(p => p.UserId).Concat(spec.Arguments.Select(a => a.AuthorUserId)).Concat(spec.Comments.Select(c => c.AuthorUserId))
            .Any(id => !id.ToString().StartsWith(IdPrefix, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invented accounts' identifiers start with {IdPrefix}.", nameof(spec));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockBill = new NpgsqlCommand(AdvisoryLocks.Sql, connection, transaction))
        {
            lockBill.Parameters.AddWithValue("key", "lt2.appeal-bill:" + spec.BillNumber);
            await lockBill.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid? existingId = null;
        string? status = null;
        DateTimeOffset? published = null;
        bool real = false, ours = false;
        await using (var existing = new NpgsqlCommand(
            """
            SELECT a.id, a.status, coalesce(a.published_at, a.opened_at),
                   EXISTS (SELECT 1 FROM appeals.position p WHERE p.record_id = a.id AND p.user_id::text NOT LIKE @prefix)
                   OR EXISTS (SELECT 1 FROM appeals.reason r WHERE r.record_id = a.id AND r.author_user_id::text NOT LIKE @prefix)
                   OR EXISTS (SELECT 1 FROM appeals.comment c WHERE c.bill_number = a.bill_number AND c.author_user_id::text NOT LIKE @prefix),
                   EXISTS (SELECT 1 FROM appeals.position p WHERE p.record_id = a.id AND p.user_id = @first)
            FROM appeals.bill_record a WHERE a.bill_number = @b AND a.status IN ('open', 'closed', 'rejected')
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue("b", spec.BillNumber);
            existing.Parameters.AddWithValue("prefix", IdPrefix + "%");
            existing.Parameters.AddWithValue("first", spec.Positions[0].UserId);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingId = reader.GetGuid(0);
                status = reader.GetString(1);
                published = reader.GetFieldValue<DateTimeOffset>(2);
                real = reader.GetBoolean(3);
                ours = reader.GetBoolean(4);
            }
        }

        if (existingId is { } found)
        {
            if (ours)
            {
                return (InventedRecordOutcome.AlreadyThere, found);
            }

            if (real || newRecordOnly || status != BillRecordStatuses.Open)
            {
                return (InventedRecordOutcome.BillTaken, null);
            }
        }

        var recordId = existingId ?? Guid.CreateVersion7(spec.OpenedAt);
        var opened = published is { } p0 && p0 < spec.OpenedAt ? p0 : spec.OpenedAt;
        await using (var record = new NpgsqlCommand(
            existingId is null
                ? """
                  INSERT INTO appeals.bill_record (id, bill_number, description, status, opened_at, published_at, sending, threshold_set_by, changed_at)
                  VALUES (@id, @bill, '', 'open', @opened, @opened, 'automatic', 'default', @opened)
                  """
                : "UPDATE appeals.bill_record SET opened_at = least(opened_at, @opened), published_at = least(coalesce(published_at, opened_at), @opened) WHERE id = @id",
            connection,
            transaction))
        {
            record.Parameters.AddWithValue("id", recordId);
            record.Parameters.AddWithValue("bill", spec.BillNumber);
            record.Parameters.AddWithValue("opened", opened);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var positions = await connection.BeginBinaryImportAsync(
            "COPY appeals.position (record_id, user_id, side, created_at, changed_at, member_since) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var p in spec.Positions)
            {
                var at = Latest(p.TakenAt ?? opened, opened, p.MemberSince);
                await positions.WriteRowAsync(cancellationToken, recordId, p.UserId, p.Side, at, at, p.MemberSince);
            }

            await positions.CompleteAsync(cancellationToken);
        }

        var reasonIds = new List<Guid>();
        foreach (var r in spec.Arguments)
        {
            var at = Latest(r.At, opened, opened);
            var reasonId = Guid.CreateVersion7(at);
            reasonIds.Add(reasonId);
            await using (var reason = new NpgsqlCommand(
                """
                INSERT INTO appeals.reason (id, record_id, side, author_user_id, text, status, seen_at, screening_flags, created_at, first_public_at)
                VALUES (@id, @record, @side, @author, @text, 'published', @at, '{}', @at, @at)
                """,
                connection,
                transaction))
            {
                reason.Parameters.AddWithValue("id", reasonId);
                reason.Parameters.AddWithValue("record", recordId);
                reason.Parameters.AddWithValue("side", r.Side);
                reason.Parameters.AddWithValue("author", r.AuthorUserId);
                reason.Parameters.AddWithValue("text", r.Text);
                reason.Parameters.AddWithValue("at", at);
                await reason.ExecuteNonQueryAsync(cancellationToken);
            }

            // Nobody marks their own argument (owner, 2026-11-17), the invented members neither; a mark comes after its argument.
            await using var endorsements = await connection.BeginBinaryImportAsync(
                "COPY appeals.endorsement (reason_id, user_id, record_id, created_at) FROM STDIN (FORMAT BINARY)", cancellationToken);
            foreach (var mark in r.Marks.Where(m => m.UserId != r.AuthorUserId).DistinctBy(m => m.UserId))
            {
                await endorsements.StartRowAsync(cancellationToken);
                await endorsements.WriteAsync(reasonId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(mark.UserId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(recordId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(Latest(mark.At, at.AddMinutes(1), at), NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await endorsements.CompleteAsync(cancellationToken);
        }

        foreach (var c in spec.Comments.Where(c => c.Argument >= 0 && c.Argument < reasonIds.Count))
        {
            var target = reasonIds[c.Argument];
            var at = Latest(c.At, Latest(spec.Arguments[c.Argument].At, opened, opened).AddMinutes(5), opened);
            await using (var comment = new NpgsqlCommand(
                """
                INSERT INTO appeals.comment (id, target_kind, target_id, bill_number, speaks_as, author_user_id, text, status, created_at)
                VALUES (@id, 'reason', @target, @bill, 'person', @author, @text, 'published', @at)
                ON CONFLICT (id) DO NOTHING
                """,
                connection,
                transaction))
            {
                comment.Parameters.AddWithValue("id", c.Id);
                comment.Parameters.AddWithValue("target", target);
                comment.Parameters.AddWithValue("bill", spec.BillNumber);
                comment.Parameters.AddWithValue("author", c.AuthorUserId);
                comment.Parameters.AddWithValue("text", c.Text);
                comment.Parameters.AddWithValue("at", at);
                await comment.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var person in c.Agreements.Where(a => a != c.AuthorUserId).Distinct())
            {
                await using var agreement = new NpgsqlCommand(
                    "INSERT INTO appeals.comment_agreement (comment_id, user_id, created_at) VALUES (@c, @u, @at) ON CONFLICT DO NOTHING",
                    connection,
                    transaction);
                agreement.Parameters.AddWithValue("c", c.Id);
                agreement.Parameters.AddWithValue("u", person);
                agreement.Parameters.AddWithValue("at", at.AddHours(3));
                await agreement.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return (InventedRecordOutcome.Created, recordId);
    }

    /// <summary>
    /// Design 5.3 for the invented records: the daily aggregate of every day from the record's opening to today (or its
    /// closing), counted from its positions as the daily job counts a day (<c>AppealsMaintenance.AggregateDayAsync</c>), so the
    /// trend chart has the weeks the invented sides were taken over. Returns the rows written.
    /// </summary>
    public static async Task<int> AggregateDaysAsync(string connectionString, IReadOnlyCollection<Guid> recordIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH r AS (
              SELECT id, published_at, closed_at FROM appeals.bill_record
              WHERE id = ANY(@ids) AND status IN ('open', 'closed') AND published_at IS NOT NULL),
            days AS (
              SELECT r.id, d::date AS day,
                     (d::timestamp AT TIME ZONE 'Europe/Vilnius') AS day_start,
                     least((d::timestamp + interval '1 day') AT TIME ZONE 'Europe/Vilnius', @now) AS as_of
              FROM r, generate_series((r.published_at AT TIME ZONE 'Europe/Vilnius')::date::timestamp,
                                      least((@now AT TIME ZONE 'Europe/Vilnius')::date,
                                            coalesce((r.closed_at AT TIME ZONE 'Europe/Vilnius')::date, (@now AT TIME ZONE 'Europe/Vilnius')::date))::timestamp,
                                      interval '1 day') d),
            counted AS (
              SELECT days.id, days.day,
                count(p.user_id) FILTER (WHERE p.side = 'for') AS for_count,
                count(p.user_id) FILTER (WHERE p.side = 'against') AS against_count,
                count(p.user_id) FILTER (WHERE p.side = 'for' AND p.member_since <= days.as_of - @established) AS for_older,
                count(p.user_id) FILTER (WHERE p.side = 'against' AND p.member_since <= days.as_of - @established) AS against_older,
                count(p.user_id) FILTER (WHERE p.created_at >= days.day_start) AS fresh,
                count(p.user_id) FILTER (WHERE p.created_at >= days.day_start AND p.member_since > days.as_of - @established) AS fresh_young
              FROM days LEFT JOIN appeals.position p ON p.record_id = days.id AND p.created_at < days.as_of
              GROUP BY days.id, days.day)
            INSERT INTO appeals.daily_stat (record_id, day, for_count, against_count, for_older_than30days, against_older_than30days, new_account_share, computed_at)
            SELECT id, day, for_count, against_count, for_older, against_older,
                   CASE WHEN fresh = 0 THEN 0 ELSE trunc(fresh_young::numeric / fresh, 4) END, @now
            FROM counted
            ON CONFLICT (record_id, day) DO UPDATE SET
              for_count = EXCLUDED.for_count, against_count = EXCLUDED.against_count,
              for_older_than30days = EXCLUDED.for_older_than30days, against_older_than30days = EXCLUDED.against_older_than30days,
              new_account_share = EXCLUDED.new_account_share, computed_at = EXCLUDED.computed_at
            """,
            connection);
        command.Parameters.AddWithValue("ids", recordIds.ToArray());
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("established", CountRules.EstablishedAge);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The invented records' numbers as they were at <paramref name="at"/> (design 8.26.5, the trial's invented past
    /// summaries): sides taken before it, the published arguments written before it and, per side, the two with the most
    /// counted marks given before it (only marks of people asking for the argument's side count, design 8.2). Records with no
    /// side yet are left out.
    /// </summary>
    public static async Task<IReadOnlyList<InventedSnapshot>> SnapshotAsync(
        string connectionString, IReadOnlyCollection<Guid> recordIds, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var counts = new List<(Guid Record, string Bill, int For, int ForOlder, int Against, int AgainstOlder)>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT a.id, a.bill_number,
              count(*) FILTER (WHERE p.side = 'for'), count(*) FILTER (WHERE p.side = 'for' AND p.member_since <= @established),
              count(*) FILTER (WHERE p.side = 'against'), count(*) FILTER (WHERE p.side = 'against' AND p.member_since <= @established)
            FROM appeals.bill_record a JOIN appeals.position p ON p.record_id = a.id AND p.created_at < @at
            WHERE a.id = ANY(@ids) GROUP BY a.id, a.bill_number
            """,
            connection))
        {
            command.Parameters.AddWithValue("ids", recordIds.ToArray());
            command.Parameters.AddWithValue("at", at);
            command.Parameters.AddWithValue("established", CountRules.EstablishedBefore(at));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts.Add((reader.GetGuid(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3), (int)reader.GetInt64(4), (int)reader.GetInt64(5)));
            }
        }

        var reasons = new List<(Guid Record, string Side, string Text, int Marks)>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT r.record_id, r.side, r.text,
              (SELECT count(*) FROM appeals.endorsement e JOIN appeals.position p ON p.record_id = e.record_id AND p.user_id = e.user_id
               WHERE e.reason_id = r.id AND e.created_at < @at AND p.side = r.side)
            FROM appeals.reason r
            WHERE r.record_id = ANY(@ids) AND r.status = 'published' AND r.created_at < @at
            """,
            connection))
        {
            command.Parameters.AddWithValue("ids", recordIds.ToArray());
            command.Parameters.AddWithValue("at", at);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                reasons.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), (int)reader.GetInt64(3)));
            }
        }

        return counts.Select(c =>
            {
                var mine = reasons.Where(r => r.Record == c.Record).ToList();
                var top = Sides.All.SelectMany(side => mine.Where(r => r.Side == side).OrderByDescending(r => r.Marks).ThenBy(r => r.Text, StringComparer.Ordinal).Take(2)
                    .Select(r => new DigestReason(side, r.Text, r.Marks))).ToList();
                return new InventedSnapshot(
                    c.Record, c.Bill, c.For, c.ForOlder, c.Against, c.AgainstOlder,
                    mine.Count(r => r.Side == Sides.For), mine.Count(r => r.Side == Sides.Against), top);
            })
            .ToList();
    }

    private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset b, DateTimeOffset c) =>
        a > b ? (a > c ? a : c) : (b > c ? b : c);
}
