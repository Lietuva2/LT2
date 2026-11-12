using System.Security.Cryptography;
using System.Text;
using Lt2.Parliament.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lt2.Parliament.Import.Agenda;

/// <summary>
/// Configuration section <c>Agenda</c>: where the upcoming plenary agenda is read from (lrs.lt pages, not the open-data
/// service). Empty <see cref="BaseAddress"/> means the watcher does nothing: nothing fetches lrs.lt unless configured,
/// and the host must also be on <c>Outbound:AllowedHosts</c>.
/// </summary>
internal sealed class AgendaOptions
{
    public const string Section = "Agenda";

    /// <summary>Base address, ending in a slash; local stack: the stand-in. Assumption A7 for the real one.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The page with the upcoming sittings and their items, relative to <see cref="BaseAddress"/> (A7).</summary>
    public string ListPath { get; set; } = "darbotvarke";

    public double TimeoutSeconds { get; set; } = 60;
}

/// <summary><c>parliament.agenda_snapshot</c>: every agenda page as fetched, kept raw (design 7.2: store, then parse).</summary>
internal sealed class AgendaSnapshot
{
    public Guid Id { get; set; }

    public string Url { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public byte[] Body { get; set; } = [];

    public bool Synthetic { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Why the page could not be read; null when it was.</summary>
    public string? Failure { get; set; }

    public int Rows { get; set; }
}

/// <summary>
/// <c>parliament.planned_item</c> (design 5.1): a bill planned for a sitting. Rows from the agenda page are replaced when
/// the date or stage moves (the old row gets <c>gone_at</c>); a moderator's row (source <c>moderator</c>) takes precedence
/// over scraped ones for the same bill until a moderator changes it again.
/// </summary>
internal sealed class PlannedItem
{
    public Guid Id { get; set; }

    public string BillNumber { get; set; } = "";

    public DateOnly PlannedDate { get; set; }

    public string? Stage { get; set; }

    public string? SourceUrl { get; set; }

    /// <summary><c>agenda</c> or <c>moderator</c>.</summary>
    public string Source { get; set; } = "agenda";

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public DateTimeOffset? GoneAt { get; set; }

    /// <summary>The moderator who entered or confirmed it.</summary>
    public Guid? ConfirmedBy { get; set; }

    public string? Note { get; set; }
}

/// <summary>The agenda watcher (design 8.4, 8.9), run by the worker.</summary>
internal sealed class AgendaWatcher(
    IHttpClientFactory http,
    IOptions<AgendaOptions> options,
    IDbContextFactory<ParliamentDb> contexts,
    TimeProvider clock,
    ILogger<AgendaWatcher> logger) : IAgendaWatch
{
    public const string HttpClientName = "lrs-agenda";
    private const string SyntheticHeader = "X-Lt2-Synthetic";

    public async Task<AgendaOutcome> RunAsync(CancellationToken cancellationToken)
    {
        if (options.Value.BaseAddress is null)
        {
            return new AgendaOutcome("off", 0, 0, null);
        }

        var url = new Uri(options.Value.BaseAddress, options.Value.ListPath);
        string html;
        bool synthetic;
        try
        {
            using var client = http.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            html = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            synthetic = response.Headers.Contains(SyntheticHeader);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Alerting (design 9.4): an error line. The last good dates and the moderators' corrections stay.
            logger.LogError(ex, "Agenda page could not be fetched; planned dates unchanged");
            return new AgendaOutcome("failed", 0, 0, "fetch failed");
        }

        var parse = AgendaParser.Parse(html, url.ToString());
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(html);
        db.AgendaSnapshots.Add(new AgendaSnapshot
        {
            Id = Ids.New(clock),
            Url = url.ToString(),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(body)),
            Body = ParliamentImporter.Compress(body),
            Synthetic = synthetic,
            FetchedAt = now,
            Failure = parse.Failure,
            Rows = parse.Rows.Count,
        });

        if (!parse.Succeeded)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError("Agenda page refused: {Failure}; planned dates unchanged until a moderator or a readable page changes them", parse.Failure);
            return new AgendaOutcome("refused", parse.Rows.Count, 0, parse.Failure);
        }

        var changes = Apply(db, parse.Rows, now);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Agenda read: {Rows} planned items, {Changes} changes", parse.Rows.Count, changes);
        return new AgendaOutcome("succeeded", parse.Rows.Count, changes, null);
    }

    /// <summary>
    /// Brings the agenda rows in line with the page: unchanged rows are seen again, a moved date or stage replaces the
    /// row, and a future item missing from the page is marked gone. Moderator rows are never touched here.
    /// </summary>
    internal int Apply(ParliamentDb db, IReadOnlyList<AgendaRow> rows, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, PlannedAgenda.Vilnius).DateTime);
        var active = db.PlannedItems.Where(p => p.Source == "agenda" && p.GoneAt == null).ToList();
        var changes = 0;
        foreach (var row in rows)
        {
            var same = active.FirstOrDefault(p => p.BillNumber == row.BillNumber && p.PlannedDate == row.SittingDate && p.Stage == row.Stage);
            if (same is not null)
            {
                same.LastSeen = now;
                same.SourceUrl = row.SourceUrl;
                continue;
            }

            db.PlannedItems.Add(new PlannedItem
            {
                Id = Ids.New(clock),
                BillNumber = row.BillNumber,
                PlannedDate = row.SittingDate,
                Stage = row.Stage,
                SourceUrl = row.SourceUrl,
                Source = "agenda",
                FirstSeen = now,
                LastSeen = now,
            });
            changes++;
        }

        foreach (var item in active.Where(p => p.PlannedDate >= today
            && !rows.Any(r => r.BillNumber == p.BillNumber && r.SittingDate == p.PlannedDate && r.Stage == p.Stage)))
        {
            item.GoneAt = now;
            changes++;
        }

        return changes;
    }
}
