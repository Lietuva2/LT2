using System.Data.Common;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Appeals.Application;

/// <summary>
/// Deletes every position and "Man svarbu" mark of one person, in the caller's transaction (design 6.5: withdrawing the
/// consent deletes the positions). Takes the person's participation lock first, so a side being taken at the same
/// moment either finishes before and is deleted, or starts after and finds the consent withdrawn.
/// </summary>
internal sealed class PositionErasure : IPositionErasure
{
    public async Task<int> EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Erasing positions needs a PostgreSQL transaction.", nameof(transaction));
        }

        await using (var lockCommand = new NpgsqlCommand(AdvisoryLocks.Sql, connection, npgsql))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLocks.PersonKey(userId));
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var endorsements = new NpgsqlCommand("DELETE FROM appeals.endorsement WHERE user_id = @user", connection, npgsql))
        {
            endorsements.Parameters.AddWithValue("user", userId);
            await endorsements.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var positions = new NpgsqlCommand("DELETE FROM appeals.position WHERE user_id = @user", connection, npgsql);
        positions.Parameters.AddWithValue("user", userId);
        return await positions.ExecuteNonQueryAsync(cancellationToken);
    }
}
