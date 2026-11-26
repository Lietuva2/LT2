using Lt2.Parliament.Import;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Parliament;

/// <summary>A bill's latest adoption-stage vote, for the trial's invented closed records (design 8.26.5).</summary>
public sealed record AdoptionVote(string BillNumber, string Title, long VoteId, DateTimeOffset HeldAt, SeimasVoteNumbers Numbers);

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

    /// <summary>
    /// The bill's adoption-stage vote (the vote that closes its record, design 8.1) and when it was held, or null (local
    /// demonstration data only, design 8.26.5).
    /// </summary>
    public static async Task<(long VoteId, DateTimeOffset HeldAt)?> FinalVoteAsync(string connectionString, string number, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT v.id, v.held_at FROM parliament.vote v JOIN parliament.bill b ON b.id = v.bill_id
            WHERE b.number = @n AND v.kind = 'StageResult' AND v.stage = 'Adoption'
            ORDER BY v.held_at DESC LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("n", number);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }

    /// <summary>
    /// The bill's adoption-stage vote with the Seimas's numbers (null numbers while its result is not read yet), or null
    /// (local demonstration data only, design 8.26.5: the invented members' sides agree with it or not on purpose).
    /// </summary>
    public static async Task<(long VoteId, DateTimeOffset HeldAt, SeimasVoteNumbers Numbers)?> FinalVoteNumbersAsync(
        string connectionString, string number, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT v.id, v.held_at, v.is_consensus, v.has_result, v."for", v.against, v.abstain
            FROM parliament.vote v JOIN parliament.bill b ON b.id = v.bill_id
            WHERE b.number = @n AND v.kind = 'StageResult' AND v.stage = 'Adoption'
            ORDER BY v.held_at DESC LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("n", number);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var read = reader.GetBoolean(3);
        int? Count(int i) => read && !reader.IsDBNull(i) ? reader.GetInt32(i) : null;
        return (reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), new SeimasVoteNumbers(reader.GetBoolean(2), Count(4), Count(5), Count(6)));
    }

    /// <summary>
    /// Whether the parliament data is the real Seimas service's only: something real is stored and no stored response, run or
    /// agenda page came from the stand-in. The trial's invented members' activity (<c>--demo-activity</c>, design 8.26.5)
    /// runs only on such data; invented parliament data has <c>--demo-appeal</c>.
    /// </summary>
    public static async Task<bool> IsDataRealAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var synthetic = await ScalarAsync<long>(
            connection,
            null,
            """
            SELECT (SELECT count(*) FROM parliament.import_run WHERE synthetic)
                 + (SELECT count(*) FROM parliament.raw_document WHERE synthetic)
                 + (SELECT count(*) FROM parliament.agenda_snapshot WHERE synthetic)
            """,
            cancellationToken);
        return synthetic == 0 && await ScalarAsync<long>(connection, null, RealRows, cancellationToken) > 0;
    }

    /// <summary>
    /// Of the given bill numbers, those in the parliament data that the Seimas has not finished (no stage result at the adoption
    /// stage, the rule of <see cref="IBillCatalogue.ActiveAsync"/>), with their official titles (design 8.26.5, the trial).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> ActiveBillsAsync(
        string connectionString, IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT b.number, b.title FROM parliament.bill b
            WHERE b.number = ANY(@numbers)
              AND NOT EXISTS (SELECT 1 FROM parliament.vote v WHERE v.bill_id = b.id AND v.kind = 'StageResult' AND v.stage = 'Adoption')
            """,
            connection);
        command.Parameters.AddWithValue("numbers", numbers.ToArray());
        var bills = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bills[reader.GetString(0)] = reader.GetString(1);
        }

        return bills;
    }

    /// <summary>
    /// The bills whose adoption-stage vote was held in [<paramref name="from"/>, <paramref name="to"/>), each with its latest
    /// such vote and the Seimas's numbers (null numbers while the result is not read), oldest first (design 8.26.5, the trial:
    /// invented members' sides are given before the real vote).
    /// </summary>
    public static async Task<IReadOnlyList<AdoptionVote>> AdoptionVotesAsync(
        string connectionString, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT * FROM (
              SELECT DISTINCT ON (b.number) b.number, b.title, v.id, v.held_at, v.is_consensus, v.has_result, v."for", v.against, v.abstain
              FROM parliament.vote v JOIN parliament.bill b ON b.id = v.bill_id
              WHERE v.kind = 'StageResult' AND v.stage = 'Adoption'
              ORDER BY b.number, v.held_at DESC) latest
            WHERE held_at >= @from AND held_at < @to
            ORDER BY held_at, number
            """,
            connection);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var votes = new List<AdoptionVote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var read = reader.GetBoolean(5);
            int? Count(int i) => read && !reader.IsDBNull(i) ? reader.GetInt32(i) : null;
            votes.Add(new AdoptionVote(
                reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3),
                new SeimasVoteNumbers(reader.GetBoolean(4), Count(6), Count(7), Count(8))));
        }

        return votes;
    }

    /// <summary>The Vilnius days with a Seimas sitting in [<paramref name="from"/>, <paramref name="to"/>], in order.</summary>
    public static async Task<IReadOnlyList<DateOnly>> SittingDaysAsync(string connectionString, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT DISTINCT (starts_at AT TIME ZONE 'Europe/Vilnius')::date AS day FROM parliament.sitting
            WHERE (starts_at AT TIME ZONE 'Europe/Vilnius')::date BETWEEN @from AND @to ORDER BY day
            """,
            connection);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var days = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return days;
    }

    /// <summary>
    /// The sitting MPs of the current term who have an official address (the summaries' recipients, as
    /// <c>IMpDirectory.SittingMpsAsync</c> finds them): profile address and Seimas identifier, never the address itself. For the
    /// trial's invented past summaries (design 8.26.5), which list who a dry run would have gone to.
    /// </summary>
    public static async Task<IReadOnlyList<(string Slug, long SeimasPersonId)>> SittingMpsWithAddressAsync(string connectionString, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH term AS (
              SELECT t.id FROM parliament.term t WHERE EXISTS (SELECT 1 FROM parliament.mandate m WHERE m.term_id = t.id)
              ORDER BY (t."to" IS NULL) DESC, t.id DESC LIMIT 1)
            SELECT DISTINCT p.slug, p.seimas_person_id FROM parliament.mandate m JOIN parliament.person p ON p.id = m.person_id
            WHERE m.term_id = (SELECT id FROM term) AND (m."to" IS NULL OR m."to" >= @today)
              AND EXISTS (SELECT 1 FROM parliament.contact c WHERE c.person_id = p.id AND c.kind = @kind AND c.value <> '')
            ORDER BY p.slug
            """,
            connection);
        command.Parameters.AddWithValue("today", today);
        command.Parameters.AddWithValue("kind", Infrastructure.OfficialAddresses.EmailKind);
        var mps = new List<(string, long)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            mps.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return mps;
    }

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
