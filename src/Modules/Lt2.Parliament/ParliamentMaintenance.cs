using Lt2.Parliament.Import;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Parliament;

/// <summary>What emptying the parliament data did.</summary>
public sealed record ParliamentReset(long ImportRuns, long RawDocuments, long People, long Votes);

/// <summary>Operations the migrator runs on the parliament schema.</summary>
public static class ParliamentMaintenance
{
    /// <summary>
    /// The one rule for "real Seimas data is stored": import runs, stored responses or agenda pages not marked synthetic.
    /// Both the reset and the demonstration data refuse while it counts anything. (The site's banner, <see
    /// cref="IParliamentDataNotice"/>, keeps its own meaning: any stored response from the stand-in.)
    /// </summary>
    private const string RealRows =
        """
        SELECT (SELECT count(*) FROM parliament.import_run WHERE NOT synthetic)
             + (SELECT count(*) FROM parliament.raw_document WHERE NOT synthetic)
             + (SELECT count(*) FROM parliament.agenda_snapshot WHERE NOT synthetic)
        """;

    /// <summary>
    /// The way back from invented data: empties every parliament table (the migration history stays), so the next import
    /// starts from nothing, for example when the local stack switches from the stand-in to the real service. Refuses
    /// (<c>parliament.reset.real-data</c>) unless every import run stored so far was synthetic and every stored response
    /// came from the stand-in, and (<c>parliament.reset.import-running</c>) while an import is running. Run as the
    /// schema owner (<c>lt2_migrator</c>).
    /// </summary>
    public static async Task<Result<ParliamentReset>> ResetInventedDataAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await ScalarAsync<bool>(connection, null, $"SELECT pg_try_advisory_lock({ParliamentImporter.LockKey})", cancellationToken))
        {
            return Error.Conflict("parliament.reset.import-running");
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, "LOCK TABLE parliament.import_run, parliament.raw_document IN EXCLUSIVE MODE", cancellationToken);
            if (await ScalarAsync<long>(connection, transaction, RealRows, cancellationToken) > 0)
            {
                return Error.Conflict("parliament.reset.real-data");
            }

            var reset = new ParliamentReset(
                await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM parliament.import_run", cancellationToken),
                await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM parliament.raw_document", cancellationToken),
                await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM parliament.person", cancellationToken),
                await ScalarAsync<long>(connection, transaction, "SELECT count(*) FROM parliament.vote", cancellationToken));
            await ExecuteAsync(
                connection,
                transaction,
                """
                DO $$ DECLARE t record; BEGIN
                  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'parliament' AND tablename <> '__ef_migrations_history' LOOP
                    EXECUTE format('TRUNCATE TABLE parliament.%I CASCADE', t.tablename);
                  END LOOP;
                END $$;
                """,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reset;
        }
        finally
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_unlock({ParliamentImporter.LockKey})", CancellationToken.None);
        }
    }

    /// <summary>
    /// Whether the parliament data is the stand-in's: at least one stored response, and every import run and every stored
    /// response synthetic. A database that ever imported from the real Seimas service answers false.
    /// </summary>
    public static async Task<bool> IsDataInventedAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var synthetic = await ScalarAsync<long>(connection, null, "SELECT count(*) FROM parliament.raw_document WHERE synthetic", cancellationToken);
        return synthetic > 0 && await ScalarAsync<long>(connection, null, RealRows, cancellationToken) == 0;
    }

    /// <summary>The official title of a stored bill, or <c>null</c>.</summary>
    public static Task<string?> BillTitleAsync(string connectionString, string number, CancellationToken cancellationToken) =>
        BillColumnAsync(connectionString, "SELECT title FROM parliament.bill WHERE number = @n", number, cancellationToken);

    /// <summary>The bill's e-seimas address from the parliament data, or null (local demonstration data only).</summary>
    public static Task<string?> BillAddressAsync(string connectionString, string number, CancellationToken cancellationToken) =>
        BillColumnAsync(connectionString, "SELECT e_seimas_url FROM parliament.bill WHERE number = @n", number, cancellationToken);

    private static async Task<string?> BillColumnAsync(string connectionString, string sql, string number, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
#pragma warning disable CA2100 // Constant SQL from this class; the bill number is a parameter.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("n", number);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // Constant SQL; the only interpolation is a constant lock key.
        await using var command = new NpgsqlCommand(sql, connection, transaction);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // Constant SQL; the only interpolation is a constant lock key.
        await using var command = new NpgsqlCommand(sql, connection, transaction);
#pragma warning restore CA2100
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
