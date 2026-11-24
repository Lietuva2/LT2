using System.Globalization;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lt2.Appeals.Application;

/// <summary>
/// The search of bills (design 8.12.5) and the copy it reads, <c>appeals.bill_card</c>. The Parliament module owns the bills and
/// no module reads another's tables, so this module keeps a card per bill: refreshed from <see cref="IBillCatalogue"/> after every
/// import, and holding the readable title as the record's writers write it (<see cref="WriteAsync(AppealsDb, IBillCatalogue, string, string?, CancellationToken)"/>,
/// in the writer's transaction).
/// </summary>
internal sealed class BillCards(IDbContextFactory<AppealsDb> contexts, IBillCatalogue bills) : IBillCards
{
    internal const string UpsertSql = """
        INSERT INTO appeals.bill_card (bill_number, title, plain_title, title_folded, plain_title_folded)
        VALUES (@number, @title, @plain, @titleFolded, @plainFolded)
        ON CONFLICT (bill_number) DO UPDATE SET
            title = excluded.title, plain_title = excluded.plain_title,
            title_folded = excluded.title_folded, plain_title_folded = excluded.plain_title_folded
        """;

    private const string RefreshSql = """
        INSERT INTO appeals.bill_card (bill_number, title, plain_title, title_folded, plain_title_folded)
        SELECT x.number, x.title, x.plain, x.title_folded, x.plain_folded
        FROM unnest(@numbers, @titles, @titlesFolded, @plains, @plainsFolded) AS x(number, title, title_folded, plain, plain_folded)
        ON CONFLICT (bill_number) DO UPDATE SET title = excluded.title, title_folded = excluded.title_folded
        WHERE appeals.bill_card.title IS DISTINCT FROM excluded.title OR appeals.bill_card.title_folded IS DISTINCT FROM excluded.title_folded
        """;

    private const string SearchSql = """
        SELECT c.bill_number AS "Value" FROM appeals.bill_card c
        WHERE NOT EXISTS (
            SELECT 1 FROM unnest(@words, @folded, @patterns) AS w(word, folded, pattern)
            WHERE NOT (c.search_vector @@ plainto_tsquery('lithuanian'::regconfig, w.word)
                OR c.search_vector_folded @@ plainto_tsquery('lithuanian'::regconfig, w.folded)
                OR c.search_folded LIKE w.pattern ESCAPE '\'))
        """;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var all = await bills.AllAsync(cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.BillCardsAsync(db, cancellationToken);
        if (all.Count > 0)
        {
            // A card made now takes the readable title of the bill's open or closed record, if it has one; a card that exists keeps
            // the title its writer gave it.
            var plains = await db.BillRecords.AsNoTracking().Records()
                .Where(r => r.PlainTitle != null && (r.Status == BillRecordStatuses.Open || r.Status == BillRecordStatuses.Closed))
                .Select(r => new { r.BillNumber, r.PlainTitle })
                .ToDictionaryAsync(r => r.BillNumber, r => r.PlainTitle!, StringComparer.Ordinal, cancellationToken);
            var plainOf = all.Select(b => plains.GetValueOrDefault(b.Number)).ToArray();
            await db.Database.ExecuteSqlRawAsync(
                RefreshSql,
                [
                    new NpgsqlParameter("numbers", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = all.Select(b => b.Number).ToArray() },
                    new NpgsqlParameter("titles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = all.Select(b => b.Title).ToArray() },
                    new NpgsqlParameter("titlesFolded", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = all.Select(b => TitleFolded(b.Number, b.Title)).ToArray() },
                    new NpgsqlParameter("plains", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = plainOf },
                    new NpgsqlParameter("plainsFolded", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = plainOf.Select(p => p is null ? null : SearchText.Fold(p)).ToArray() },
                ],
                cancellationToken);
        }

        // A card of a bill the catalogue no longer holds goes, so the count that tells whether the copy is complete stays true.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM appeals.bill_card WHERE NOT (bill_number = ANY(@numbers))",
            [new NpgsqlParameter("numbers", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = all.Select(b => b.Number).ToArray() }],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the card of one bill in the caller's transaction, as its readable title is written (design 8.13.1): the official
    /// title from the catalogue and <paramref name="plainTitle"/> (null when there is none). Nothing is written for a number
    /// the catalogue does not hold.
    /// </summary>
    public static async Task WriteAsync(AppealsDb db, IBillCatalogue bills, string billNumber, string? plainTitle, CancellationToken ct)
    {
        var bill = await bills.FindAsync(billNumber, ct);
        if (bill is not null)
        {
            await WriteAsync(db, bill, plainTitle, ct);
        }
    }

    /// <summary>The same with the bill's facts at hand.</summary>
    public static Task<int> WriteAsync(AppealsDb db, BillFacts bill, string? plainTitle, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            UpsertSql,
            [
                new NpgsqlParameter("number", NpgsqlDbType.Text) { Value = bill.Number },
                new NpgsqlParameter("title", NpgsqlDbType.Text) { Value = bill.Title },
                new NpgsqlParameter("plain", NpgsqlDbType.Text) { Value = (object?)plainTitle ?? DBNull.Value },
                new NpgsqlParameter("titleFolded", NpgsqlDbType.Text) { Value = TitleFolded(bill.Number, bill.Title) },
                new NpgsqlParameter("plainFolded", NpgsqlDbType.Text) { Value = plainTitle is null ? DBNull.Value : SearchText.Fold(plainTitle) },
            ],
            ct);

    public async Task<IReadOnlyCollection<string>?> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var words = SearchText.Words(query);
        if (words.Count == 0)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // Bills added outside the import (a fresh database) have no card yet: the counts tell, and the refresh makes them.
        if (await bills.CountAsync(cancellationToken) != await db.BillCards.AsNoTracking().CountAsync(cancellationToken))
        {
            await RefreshAsync(cancellationToken);
        }

        // Every word is found by its stem (the text search, as typed or with Lithuanian letters left out on both sides) or as part
        // of the folded text; the words go in as parameter arrays.
        var folded = words.Select(SearchText.Fold).ToArray();
        var patterns = folded.Select(w => "%" + EscapeLike(w) + "%").ToArray();
        return await db.Database
            .SqlQueryRaw<string>(
                SearchSql,
                new NpgsqlParameter("words", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = words.ToArray() },
                new NpgsqlParameter("folded", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = folded },
                new NpgsqlParameter("patterns", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = patterns })
            .ToListAsync(cancellationToken);
    }

    private static string TitleFolded(string number, string title) => SearchText.Fold(number + " " + title);

    /// <summary>A word as a LIKE pattern's literal: <c>\</c>, <c>%</c> and <c>_</c> mean themselves.</summary>
    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
