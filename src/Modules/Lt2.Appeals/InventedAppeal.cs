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
public sealed record InventedAppealSpec(
    string BillNumber,
    string Description,
    DateTimeOffset OpenedAt,
    IReadOnlyList<InventedPosition> Positions,
    IReadOnlyList<InventedReason> Reasons);

/// <summary>What the demonstration seeding found or did.</summary>
public enum InventedAppealOutcome
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
public static class InventedAppeal
{
    /// <summary>
    /// Gives a bill an invented description with the source <c>ai</c> (design 8.13: the label "Sukurta DI · žmonių
    /// neperžiūrėta" seen locally without a provider), as the summary job would store it: in the public history without a
    /// person, with the official text's address and the day. Creates the bill's record when it has none. A bill whose
    /// description a person ever wrote, corrected or removed is left as it is (<see cref="InventedAppealOutcome.BillTaken"/>),
    /// as the job would leave it.
    /// </summary>
    public static async Task<InventedAppealOutcome> SeedAiDescriptionAsync(
        string connectionString, string billNumber, string text, string? address, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid? recordId = null;
        await using (var existing = new NpgsqlCommand(
            """
            SELECT a.id, a.description,
                   EXISTS (SELECT 1 FROM appeals.revision r WHERE r.entity_kind = 'appeal' AND r.entity_id = a.id
                           AND r.field = 'description' AND r.changed_by_user_id IS NOT NULL)
            FROM appeals.appeal a WHERE a.bill_number = @b AND a.status IN ('open', 'closed', 'rejected')
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
                    return InventedAppealOutcome.AlreadyThere;
                }

                if (reader.GetString(1).Length > 0 || reader.GetBoolean(2))
                {
                    return InventedAppealOutcome.BillTaken;
                }

                recordId = reader.GetGuid(0);
            }
        }

        var id = recordId ?? Guid.CreateVersion7(at);
        var sql = recordId is null
            ? """
              INSERT INTO appeals.appeal (id, bill_number, description, description_source, description_url, description_at, status, opened_at,
                                          published_at, sending, threshold_set_by, changed_at)
              VALUES (@id, @bill, @text, 'ai', @url, @at, 'open', @at, @at, 'automatic', 'default', @at)
              """
            : """
              UPDATE appeals.appeal SET description = @text, description_source = 'ai', description_url = @url, description_at = @at, changed_at = @at
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
            VALUES (@id, 'appeal', @appeal, 'description', NULL, @text, NULL, @at)
            """,
            connection,
            transaction))
        {
            revision.Parameters.AddWithValue("id", Guid.CreateVersion7(at));
            revision.Parameters.AddWithValue("appeal", id);
            revision.Parameters.AddWithValue("text", text);
            revision.Parameters.AddWithValue("at", at);
            await revision.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return InventedAppealOutcome.Created;
    }

    public static async Task<InventedAppealOutcome> SeedAsync(string connectionString, InventedAppealSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Positions.Any(p => !Sides.IsSide(p.Side)) || spec.Reasons.Any(r => !Sides.IsSide(r.Side)))
        {
            throw new ArgumentException("Every side is for or against.", nameof(spec));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var existing = new NpgsqlCommand(
            """
            SELECT a.id, EXISTS (SELECT 1 FROM appeals.position p WHERE p.appeal_id = a.id AND p.user_id = @first)
            FROM appeals.appeal a WHERE a.bill_number = @b AND a.status IN ('open', 'closed', 'rejected')
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue("b", spec.BillNumber);
            existing.Parameters.AddWithValue("first", spec.Positions.Count > 0 ? spec.Positions[0].UserId : Guid.Empty);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return reader.GetBoolean(1) ? InventedAppealOutcome.AlreadyThere : InventedAppealOutcome.BillTaken;
            }
        }

        var appealId = Guid.CreateVersion7(spec.OpenedAt);
        var published = spec.OpenedAt;
        await using (var appeal = new NpgsqlCommand(
            """
            INSERT INTO appeals.appeal (id, bill_number, description, description_source, description_at, status, opened_at, published_at, sending,
                                        threshold_set_by, changed_at)
            VALUES (@id, @bill, @description, 'moderator', @published, 'open', @opened, @published, 'automatic', 'default', @published)
            """,
            connection,
            transaction))
        {
            appeal.Parameters.AddWithValue("id", appealId);
            appeal.Parameters.AddWithValue("bill", spec.BillNumber);

            appeal.Parameters.AddWithValue("description", spec.Description);
            appeal.Parameters.AddWithValue("opened", spec.OpenedAt);
            appeal.Parameters.AddWithValue("published", published);

            await appeal.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var positions = await connection.BeginBinaryImportAsync(
            "COPY appeals.position (appeal_id, user_id, side, created_at, changed_at, member_since) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var p in spec.Positions)
            {
                var at = p.TakenAt is { } taken && taken > published && taken > p.MemberSince ? taken : (p.MemberSince > published ? p.MemberSince : published);
                await positions.WriteRowAsync(cancellationToken, appealId, p.UserId, p.Side, at, at, p.MemberSince);
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
                INSERT INTO appeals.reason (id, appeal_id, side, author_user_id, text, status, seen_at, screening_flags, created_at)
                VALUES (@id, @appeal, @side, @author, @text, 'published', @at, '{}', @at)
                """,
                connection,
                transaction))
            {
                reason.Parameters.AddWithValue("id", reasonId);
                reason.Parameters.AddWithValue("appeal", appealId);
                reason.Parameters.AddWithValue("side", r.Side);
                reason.Parameters.AddWithValue("author", r.AuthorUserId);
                reason.Parameters.AddWithValue("text", r.Text);
                reason.Parameters.AddWithValue("at", reasonAt);
                await reason.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var endorsements = await connection.BeginBinaryImportAsync(
                "COPY appeals.endorsement (reason_id, user_id, appeal_id, created_at) FROM STDIN (FORMAT BINARY)", cancellationToken);
            foreach (var user in r.EndorsedBy)
            {
                await endorsements.StartRowAsync(cancellationToken);
                await endorsements.WriteAsync(reasonId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(user, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(appealId, NpgsqlDbType.Uuid, cancellationToken);
                await endorsements.WriteAsync(reasonAt, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await endorsements.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return InventedAppealOutcome.Created;
    }
}
