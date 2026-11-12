using System.Data.Common;
using Lt2.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Audit.Infrastructure;

/// <summary>Inserts into <c>audit.event</c>, on the caller's transaction when one is given.</summary>
internal sealed class AuditLogWriter(NpgsqlDataSource dataSource, TimeProvider clock) : IAuditLog
{
    private const string Sql =
        """
        INSERT INTO audit.event (id, occurred_at, actor_user_id, actor_role, action, target, details, ip_hash)
        VALUES (@id, @at, @actor, @role, @action, @target, @details, @ip)
        """;

    public async Task WriteAsync(AuditEntry entry, DbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (transaction is not null)
        {
            if (transaction is not NpgsqlTransaction { Connection: not null } npgsqlTransaction)
            {
                throw new ArgumentException("The audit log needs a PostgreSQL transaction.", nameof(transaction));
            }

            await using var command = new NpgsqlCommand(Sql, npgsqlTransaction.Connection, npgsqlTransaction);
            Bind(command, entry);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var standalone = dataSource.CreateCommand(Sql);
        Bind(standalone, entry);
        await standalone.ExecuteNonQueryAsync(cancellationToken);
    }

    private void Bind(NpgsqlCommand command, AuditEntry entry)
    {
        command.Parameters.AddWithValue("id", Ids.New(clock));
        command.Parameters.AddWithValue("at", clock.GetUtcNow());
        command.Parameters.AddWithValue("actor", (object?)entry.ActorUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("role", (object?)entry.ActorRole ?? DBNull.Value);
        command.Parameters.AddWithValue("action", entry.Action);
        command.Parameters.AddWithValue("target", (object?)entry.Target ?? DBNull.Value);
        command.Parameters.AddWithValue("details", NpgsqlDbType.Jsonb, (object?)entry.Details?.GetRawText() ?? DBNull.Value);
        command.Parameters.AddWithValue("ip", (object?)entry.IpHash ?? DBNull.Value);
    }
}
