using Lt2.Appeals.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Appeals;

/// <summary>One invented participant's side, for local demonstration data.</summary>
public sealed record InventedPosition(Guid UserId, string Side, DateTimeOffset MemberSince, DateTimeOffset? TakenAt = null);

/// <summary>One invented argument and who marked it "Man svarbu".</summary>
public sealed record InventedReason(Guid AuthorUserId, string Side, string Text, IReadOnlyList<Guid> EndorsedBy);

/// <summary>A published appeal with its positions and arguments, for local demonstration data.</summary>
public sealed record InventedAppealSpec(
    string BillNumber,
    Guid InitiatorUserId,
    string Description,
    int Threshold,
    DateTimeOffset OpenedAt,
    IReadOnlyList<InventedPosition> Positions,
    IReadOnlyList<InventedReason> Reasons);

/// <summary>What the demonstration seeding found or did.</summary>
public enum InventedAppealOutcome
{
    Created,

    /// <summary>The same initiator's appeal is already there; nothing changed.</summary>
    AlreadyThere,

    /// <summary>Someone else's appeal holds the bill; nothing changed.</summary>
    BillTaken,
}

/// <summary>
/// Local demonstration data only (the migrator's <c>--demo-appeal</c>, which refuses unless the parliament data is the
/// stand-in's): writes a published appeal with invented positions, arguments and "Man svarbu" marks in one transaction.
/// Nothing here runs in the application.
/// </summary>
public static class InventedAppeal
{
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
        await using (var existing = new NpgsqlCommand("SELECT initiator_user_id FROM appeals.appeal WHERE bill_number = @b AND status <> 'rejected'", connection, transaction))
        {
            existing.Parameters.AddWithValue("b", spec.BillNumber);
            if (await existing.ExecuteScalarAsync(cancellationToken) is Guid initiator)
            {
                return initiator == spec.InitiatorUserId ? InventedAppealOutcome.AlreadyThere : InventedAppealOutcome.BillTaken;
            }
        }

        var appealId = Guid.CreateVersion7(spec.OpenedAt);
        var published = spec.OpenedAt.AddHours(3);
        await using (var appeal = new NpgsqlCommand(
            """
            INSERT INTO appeals.appeal (id, bill_number, initiator_user_id, description, status, opened_at, published_at, sending,
                                        threshold, threshold_set_by, screening_flags, changed_at)
            VALUES (@id, @bill, @initiator, @description, 'open', @opened, @published, 'automatic', @threshold, 'initiator', '{}', @published)
            """,
            connection,
            transaction))
        {
            appeal.Parameters.AddWithValue("id", appealId);
            appeal.Parameters.AddWithValue("bill", spec.BillNumber);
            appeal.Parameters.AddWithValue("initiator", spec.InitiatorUserId);
            appeal.Parameters.AddWithValue("description", spec.Description);
            appeal.Parameters.AddWithValue("opened", spec.OpenedAt);
            appeal.Parameters.AddWithValue("published", published);
            appeal.Parameters.AddWithValue("threshold", spec.Threshold);
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
