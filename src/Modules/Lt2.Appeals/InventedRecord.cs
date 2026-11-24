using Lt2.Appeals.Domain;
using Lt2.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Appeals;

/// <summary>One invented participant's side, for local demonstration data.</summary>
public sealed record InventedPosition(Guid UserId, string Side, DateTimeOffset MemberSince, DateTimeOffset? TakenAt = null);

/// <summary>One invented argument and who marked it "Man svarbu".</summary>
public sealed record InventedReason(Guid AuthorUserId, string Side, string Text, IReadOnlyList<Guid> EndorsedBy);

/// <summary>
/// A bill's record with its positions and arguments, for local demonstration data. The description is a moderator's and
/// the record has the default threshold (design 8.12).
/// </summary>
public sealed record InventedRecordSpec(
    string BillNumber,
    string Description,
    DateTimeOffset OpenedAt,
    IReadOnlyList<InventedPosition> Positions,
    IReadOnlyList<InventedReason> Reasons);

/// <summary>What the demonstration seeding found or did.</summary>
public enum InventedRecordOutcome
{
    Created,

    /// <summary>The demonstration record is already there (its first invented participant has a side on it); nothing changed.</summary>
    AlreadyThere,

    /// <summary>The bill already has a record that is not the demonstration's; nothing changed.</summary>
    BillTaken,
}

/// <summary>
/// Local demonstration data only (the migrator's <c>--demo-appeal</c>, which refuses unless the parliament data is the
/// stand-in's): writes a bill's record with invented positions, arguments and "Man svarbu" marks in one transaction.
/// Nothing here runs in the application.
/// </summary>
public static class InventedRecord
{
    /// <summary>
    /// Gives a bill an invented description with the source <c>ai</c> (design 8.13: the label "Sukurta DI · žmonių
    /// neperžiūrėta" seen locally without a provider), as the summary job would store it: in the public history without a
    /// person, with the official text's address and the day. Creates the bill's record when it has none. A bill whose
    /// description a person ever wrote, corrected or removed is left as it is (<see cref="InventedRecordOutcome.BillTaken"/>),
    /// as the job would leave it.
    /// </summary>
    public static async Task<InventedRecordOutcome> SeedAiDescriptionAsync(
        string connectionString, string billNumber, string officialTitle, string text, string plainTitle, string? address, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid? recordId = null;
        await using (var existing = new NpgsqlCommand(
            """
            SELECT a.id, a.description,
                   EXISTS (SELECT 1 FROM appeals.revision r WHERE r.entity_kind = 'record' AND r.entity_id = a.id
                           AND r.field = 'description' AND r.changed_by_user_id IS NOT NULL)
            FROM appeals.bill_record a WHERE a.bill_number = @b AND a.status IN ('open', 'closed', 'rejected')
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue("b", billNumber);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(1) == text)
                {
                    return InventedRecordOutcome.AlreadyThere;
                }

                if (reader.GetString(1).Length > 0 || reader.GetBoolean(2))
                {
                    return InventedRecordOutcome.BillTaken;
                }

                recordId = reader.GetGuid(0);
            }
        }

        var id = recordId ?? Guid.CreateVersion7(at);
        var sql = recordId is null
            ? """
              INSERT INTO appeals.bill_record (id, bill_number, description, plain_title, description_source, description_url, description_at, status, opened_at,
                                          published_at, sending, threshold_set_by, changed_at)
              VALUES (@id, @bill, @text, @title, 'ai', @url, @at, 'open', @at, @at, 'automatic', 'default', @at)
              """
            : """
              UPDATE appeals.bill_record SET description = @text, plain_title = @title, description_source = 'ai', description_url = @url, description_at = @at, changed_at = @at
              WHERE id = @id
              """;
#pragma warning disable CA2100 // One of two constant statements.
        await using (var write = new NpgsqlCommand(sql, connection, transaction))
#pragma warning restore CA2100
        {
            write.Parameters.AddWithValue("id", id);
            write.Parameters.AddWithValue("bill", billNumber);
            write.Parameters.AddWithValue("text", text);
            write.Parameters.AddWithValue("title", plainTitle);
            write.Parameters.AddWithValue("url", (object?)address ?? DBNull.Value);
            write.Parameters.AddWithValue("at", at);
            await write.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var revision = new NpgsqlCommand(
            """
            INSERT INTO appeals.revision (id, entity_kind, entity_id, field, old_value, new_value, changed_by_user_id, changed_at)
            VALUES (@id, 'record', @record, 'description', NULL, @text, NULL, @at)
            """,
            connection,
            transaction))
        {
            revision.Parameters.AddWithValue("id", Guid.CreateVersion7(at));
            revision.Parameters.AddWithValue("record", id);
            revision.Parameters.AddWithValue("text", text);
            revision.Parameters.AddWithValue("at", at);
            await revision.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var titleRevision = new NpgsqlCommand(
            """
            INSERT INTO appeals.revision (id, entity_kind, entity_id, field, old_value, new_value, changed_by_user_id, changed_at)
            VALUES (@id, 'record', @record, 'plain_title', NULL, @title, NULL, @at)
            """,
            connection,
            transaction))
        {
            titleRevision.Parameters.AddWithValue("id", Guid.CreateVersion7(at));
            titleRevision.Parameters.AddWithValue("record", id);
            titleRevision.Parameters.AddWithValue("title", plainTitle);
            titleRevision.Parameters.AddWithValue("at", at);
            await titleRevision.ExecuteNonQueryAsync(cancellationToken);
        }

        // The search copy of the bill (design 8.12.5) holds the readable title as it is written, here as everywhere.
        await using (var card = new NpgsqlCommand(Application.BillCards.UpsertSql, connection, transaction))
        {
            card.Parameters.AddWithValue("number", billNumber);
            card.Parameters.AddWithValue("title", officialTitle);
            card.Parameters.AddWithValue("plain", plainTitle);
            card.Parameters.AddWithValue("titleFolded", SearchText.Fold(billNumber + " " + officialTitle));
            card.Parameters.AddWithValue("plainFolded", SearchText.Fold(plainTitle));
            await card.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return InventedRecordOutcome.Created;
    }

    public static async Task<InventedRecordOutcome> SeedAsync(string connectionString, InventedRecordSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Positions.Any(p => !Sides.IsSide(p.Side)) || spec.Reasons.Any(r => !Sides.IsSide(r.Side)))
        {
            throw new ArgumentException("Every side is for or against.", nameof(spec));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid? existingId = null;
        var ours = false;
        await using (var existing = new NpgsqlCommand(
            """
            SELECT a.id, EXISTS (SELECT 1 FROM appeals.position p WHERE p.record_id = a.id AND p.user_id = @first)
            FROM appeals.bill_record a WHERE a.bill_number = @b AND a.status IN ('open', 'closed', 'rejected')
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue("b", spec.BillNumber);
            existing.Parameters.AddWithValue("first", spec.Positions.Count > 0 ? spec.Positions[0].UserId : Guid.Empty);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingId = reader.GetGuid(0);
                ours = reader.GetBoolean(1);
            }
        }

        if (existingId is not null)
        {
            return ours ? InventedRecordOutcome.AlreadyThere : InventedRecordOutcome.BillTaken;
        }

        var recordId = Guid.CreateVersion7(spec.OpenedAt);
        var published = spec.OpenedAt;
        await using (var record = new NpgsqlCommand(
            """
            INSERT INTO appeals.bill_record (id, bill_number, description, description_source, description_at, status, opened_at, published_at, sending,
                                        threshold_set_by, changed_at)
            VALUES (@id, @bill, @description, 'moderator', @published, 'open', @opened, @published, 'automatic', 'default', @published)
            """,
            connection,
            transaction))
        {
            record.Parameters.AddWithValue("id", recordId);
            record.Parameters.AddWithValue("bill", spec.BillNumber);

            record.Parameters.AddWithValue("description", spec.Description);
            record.Parameters.AddWithValue("opened", spec.OpenedAt);
            record.Parameters.AddWithValue("published", published);

            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var positions = await connection.BeginBinaryImportAsync(
            "COPY appeals.position (record_id, user_id, side, created_at, changed_at, member_since) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var p in spec.Positions)
            {
                var at = p.TakenAt is { } taken && taken > published && taken > p.MemberSince ? taken : (p.MemberSince > published ? p.MemberSince : published);
                await positions.WriteRowAsync(cancellationToken, recordId, p.UserId, p.Side, at, at, p.MemberSince);
            }

            await positions.CompleteAsync(cancellationToken);
        }

        var reasonAt = published;
        foreach (var r in spec.Reasons)
        {
            reasonAt = reasonAt.AddMinutes(7);
            var reasonId = Guid.CreateVersion7(reasonAt);
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
                reason.Parameters.AddWithValue("at", reasonAt);
                await reason.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var endorsements = await connection.BeginBinaryImportAsync(
                "COPY appeals.endorsement (reason_id, user_id, record_id, created_at) FROM STDIN (FORMAT BINARY)", cancellationToken);
            // Nobody marks their own argument (owner, 2026-11-17), the invented members neither.
            foreach (var user in r.EndorsedBy.Where(u => u != r.AuthorUserId))
            {
                await endorsements.StartRowAsync(cancellationToken);
                await endorsements.WriteAsync(reasonId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(user, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(recordId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(reasonAt, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await endorsements.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return InventedRecordOutcome.Created;
    }

    /// <summary>
    /// Design 8.26.5, the migrator's <c>--demo-sides-for</c> only: gives one account a side on each of these bills whose record
    /// is closed, dated before the record closed (at <c>Placed</c>, 0 to 1, of the time between its opening and an hour
    /// before it closed), and adds it to the frozen counts as the closing job would have counted it (older than 30 days by
    /// <paramref name="memberSince"/> at the closing). A bill where the account already has a side, or whose record is not
    /// closed, is left as it is, so running it again changes nothing. Returns the sides given for and against.
    /// </summary>
    public static async Task<(int For, int Against)> GiveClosedSidesAsync(
        string connectionString, Guid userId, DateTimeOffset memberSince, IReadOnlyList<(string Bill, string Side, double Placed)> sides, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sides);
        if (sides.Any(s => !Sides.IsSide(s.Side) || s.Placed is < 0 or > 1))
        {
            throw new ArgumentException("Every side is for or against, placed between 0 and 1.", nameof(sides));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (given, against) = (0, 0);
        foreach (var (bill, side, placed) in sides)
        {
            Guid recordId;
            DateTimeOffset opened, closed;
            await using (var record = new NpgsqlCommand(
                "SELECT id, coalesce(published_at, opened_at), closed_at FROM appeals.bill_record WHERE bill_number = @b AND status = 'closed' AND closed_at IS NOT NULL FOR UPDATE",
                connection,
                transaction))
            {
                record.Parameters.AddWithValue("b", bill);
                await using var reader = await record.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    continue;
                }

                recordId = reader.GetGuid(0);
                opened = reader.GetFieldValue<DateTimeOffset>(1);
                closed = reader.GetFieldValue<DateTimeOffset>(2);
            }

            var latest = closed.AddHours(-2);
            var at = latest > opened ? opened + TimeSpan.FromTicks((long)((latest - opened).Ticks * placed)) : opened;
            await using (var insert = new NpgsqlCommand(
                """
                INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since)
                VALUES (@record, @user, @side, @at, @at, @since) ON CONFLICT DO NOTHING
                """,
                connection,
                transaction))
            {
                insert.Parameters.AddWithValue("record", recordId);
                insert.Parameters.AddWithValue("user", userId);
                insert.Parameters.AddWithValue("side", side);
                insert.Parameters.AddWithValue("at", at);
                insert.Parameters.AddWithValue("since", memberSince);
                if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    continue;
                }
            }

            var older = memberSince < CountRules.EstablishedBefore(closed) ? 1 : 0;
            await using (var counts = new NpgsqlCommand(
                """
                UPDATE appeals.bill_record SET changed_at = @now,
                  closed_for = closed_for + @for, closed_for_older_than30days = closed_for_older_than30days + @for * @older,
                  closed_against = closed_against + @against, closed_against_older_than30days = closed_against_older_than30days + @against * @older
                WHERE id = @record
                """,
                connection,
                transaction))
            {
                var isFor = side == Sides.For ? 1 : 0;
                counts.Parameters.AddWithValue("now", now);
                counts.Parameters.AddWithValue("for", isFor);
                counts.Parameters.AddWithValue("against", 1 - isFor);
                counts.Parameters.AddWithValue("older", older);
                counts.Parameters.AddWithValue("record", recordId);
                await counts.ExecuteNonQueryAsync(cancellationToken);
            }

            if (side == Sides.For)
            {
                given++;
            }
            else
            {
                against++;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return (given, against);
    }

    /// <summary>
    /// Closes the bill's open demonstration record as the closing job would (design 8.1, 8.26.5): the counts are frozen from
    /// its positions (older than 30 days at <paramref name="at"/>) and the closing vote is kept. Returns whether it closed;
    /// an already closed record, or none, is left as it is.
    /// </summary>
    public static async Task<bool> CloseAsync(string connectionString, string billNumber, long closingVoteId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var close = new NpgsqlCommand(
            """
            UPDATE appeals.bill_record r SET status = 'closed', closed_at = @at, changed_at = @at, closing_vote_id = @vote, closed_reason = 'vote',
              closed_for = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'for'),
              closed_for_older_than30days = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'for' AND p.member_since < @established),
              closed_against = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'against'),
              closed_against_older_than30days = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'against' AND p.member_since < @established)
            WHERE r.bill_number = @b AND r.status = 'open'
            """,
            connection);
        close.Parameters.AddWithValue("at", at);
        close.Parameters.AddWithValue("vote", closingVoteId);
        close.Parameters.AddWithValue("established", CountRules.EstablishedBefore(at));
        close.Parameters.AddWithValue("b", billNumber);
        return await close.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}