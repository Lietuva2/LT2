using Lt2.Appeals.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Appeals;

/// <summary>One invented participant's side, for local demonstration data.</summary>
public sealed record InventedPosition(Guid UserId, string Side, DateTimeOffset MemberSince, DateTimeOffset? TakenAt = null);

/// <summary>One invented argument and who marked it "Man svarbu".</summary>
public sealed record InventedReason(Guid AuthorUserId, string Side, string Text, IReadOnlyList<Guid> EndorsedBy);

/// <summary>
/// A bill's record with its positions and arguments, for local demonstration data. The description is a moderator's; the
/// record has no initiator and the default threshold (design 8.12).
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

    /// <summary>
    /// The demonstration record is already there (its first invented participant has a side on it); nothing changed except
    /// that a description seeded as an invented initiator's becomes the moderator's.
    /// </summary>
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
        string connectionString, string billNumber, string text, string? address, DateTimeOffset at, CancellationToken cancellationToken)
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
              INSERT INTO appeals.bill_record (id, bill_number, description, description_source, description_url, description_at, status, opened_at,
                                          published_at, sending, threshold_set_by, changed_at)
              VALUES (@id, @bill, @text, 'ai', @url, @at, 'open', @at, @at, 'automatic', 'default', @at)
              """
            : """
              UPDATE appeals.bill_record SET description = @text, description_source = 'ai', description_url = @url, description_at = @at, changed_at = @at
              WHERE id = @id
              """;
#pragma warning disable CA2100 // One of two constant statements.
        await using (var write = new NpgsqlCommand(sql, connection, transaction))
#pragma warning restore CA2100
        {
            write.Parameters.AddWithValue("id", id);
            write.Parameters.AddWithValue("bill", billNumber);
            write.Parameters.AddWithValue("text", text);
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
        var byInitiator = false;
        await using (var existing = new NpgsqlCommand(
            """
            SELECT a.id, EXISTS (SELECT 1 FROM appeals.position p WHERE p.record_id = a.id AND p.user_id = @first),
                   a.description_source = 'initiator' AND a.description = @description
            FROM appeals.bill_record a WHERE a.bill_number = @b AND a.status IN ('open', 'closed', 'rejected')
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue("b", spec.BillNumber);
            existing.Parameters.AddWithValue("first", spec.Positions.Count > 0 ? spec.Positions[0].UserId : Guid.Empty);
            existing.Parameters.AddWithValue("description", spec.Description);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingId = reader.GetGuid(0);
                ours = reader.GetBoolean(1);
                byInitiator = !reader.IsDBNull(2) && reader.GetBoolean(2);
            }
        }

        if (existingId is { } found)
        {
            if (ours && byInitiator)
            {
                // Seeded before 2026-11-14 16:13 (commit 95369bc) with the description as an invented initiator's. The
                // demonstration description is a moderator's and the record has no initiator (design 8.12.4): repaired here.
                await using var repair = new NpgsqlCommand(
                    "UPDATE appeals.bill_record SET description_source = 'moderator', initiator_user_id = NULL, description_at = coalesce(description_at, published_at) WHERE id = @id",
                    connection,
                    transaction);
                repair.Parameters.AddWithValue("id", found);
                await repair.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

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
                INSERT INTO appeals.reason (id, record_id, side, author_user_id, text, status, seen_at, screening_flags, created_at)
                VALUES (@id, @record, @side, @author, @text, 'published', @at, '{}', @at)
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
            UPDATE appeals.bill_record r SET status = 'closed', closed_at = @at, changed_at = @at, closing_vote_id = @vote,
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