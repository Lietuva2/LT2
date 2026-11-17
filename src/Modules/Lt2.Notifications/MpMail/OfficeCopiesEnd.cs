using System.Data.Common;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Notifications.MpMail;

/// <summary>
/// <see cref="IOfficeCopiesEnd"/> (design 8.19.3, owner 2026-11-17): an office link ended, so the member's copies of the MP's
/// summaries end and their choice is deleted, in Identity's transaction. Plain SQL on that transaction's connection.
/// </summary>
internal sealed class OfficeCopiesEnd : IOfficeCopiesEnd
{
    public async Task EndAsync(Guid? userId, long? seimasPersonId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (userId is null && seimasPersonId is null)
        {
            throw new ArgumentException("An account, an MP or both are needed.", nameof(userId));
        }

        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Ending copies needs a PostgreSQL transaction.", nameof(transaction));
        }

        await using var command = new NpgsqlCommand(
            "DELETE FROM notify.office_copy WHERE (@user::uuid IS NULL OR user_id = @user::uuid) AND (@person::bigint IS NULL OR seimas_person_id = @person::bigint)",
            connection,
            npgsql);
        command.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("person", (object?)seimasPersonId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
