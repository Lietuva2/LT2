using System.Text.Json;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Appeals.Application;

/// <summary>
/// What the summary job last said about why it makes nothing, so each reason is logged once and not on every run (design
/// 8.13). One per process.
/// </summary>
internal sealed class SummaryJobNotices
{
    private readonly Lock gate = new();
    private string? said;

    /// <summary>True the first time a run ends with <paramref name="status"/> after a run that ended otherwise.</summary>
    public bool ShouldSay(string status)
    {
        lock (gate)
        {
            if (said == status)
            {
                return false;
            }

            said = status;
            return true;
        }
    }
}

/// <summary>
/// The summary job (design 8.13): for active bills without a description, those with a planned date first (soonest first),
/// then the most participants, it reads the official text through <see cref="IBillTextSource"/> and asks the assistant
/// for two to four sentences with its own public prompt (<c>Ai/description-summary.prompt.md</c>); at most <c>Ai:SummariesPerDay</c> asked a Vilnius day. A summary is
/// stored as the bill's description with the source <c>ai</c>, shown at once, in the public history, audited with the
/// system as the actor and never with its text. A bill whose text is unavailable or not enough is tried again after seven
/// days. A description a person ever wrote, corrected or removed is never written over.
/// </summary>
internal sealed class DescriptionSummaries(
    IDbContextFactory<AppealsDb> contexts,
    AppealReads reads,
    BillRecords records,
    IPlannedAgenda agenda,
    IAiAssistant assistant,
    IBillTextSource texts,
    ISettings settings,
    IAuditLog audit,
    SummaryJobNotices notices,
    TimeProvider clock,
    ILogger<DescriptionSummaries> logger) : IDescriptionSummaries
{
    public async Task<SummaryRun> RunAsync(CancellationToken cancellationToken)
    {
        var perDay = await settings.GetAsync(SettingKeys.AiSummariesPerDay, cancellationToken);
        if (perDay == 0)
        {
            return Quiet("off", "AI summaries are switched off (Ai:SummariesPerDay is 0)");
        }

        if (!texts.CanRead)
        {
            return Quiet("no-reader", "AI summaries: nothing to read, no reader of the bills' official texts exists; no summary is made");
        }

        if (!assistant.IsConfigured)
        {
            return Quiet("no-provider", "AI summaries: no AI provider is configured; no summary is made");
        }

        _ = notices.ShouldSay("done");
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var dayStart = VilniusTime.StartOf(VilniusTime.DateOf(now));
        var askedToday = await db.SummaryAttempts.CountAsync(a => a.AskedProvider && a.TriedAt >= dayStart, cancellationToken);
        var left = SummaryRules.LeftToday(perDay, askedToday);
        if (left == 0)
        {
            return new SummaryRun("done", 0, 0);
        }

        var asked = 0;
        var made = 0;
        foreach (var bill in await DueAsync(db, now, cancellationToken))
        {
            if (asked >= left)
            {
                break;
            }

            var text = await texts.ReadAsync(bill.Number, cancellationToken);
            if (text is null || string.IsNullOrWhiteSpace(text.Text) || text.Text.Length > AiDraftRules.SourceMaxLength)
            {
                // Over-long text is not cut (design 8.11.4); like a missing one, it waits seven days.
                await AttemptAsync(bill.Number, SummaryOutcomes.Unavailable, askedProvider: false, cancellationToken);
                continue;
            }

            asked++;
            var answer = await assistant.DraftDescriptionAsync(new DraftSource(bill.Number, bill.Title, text.Text.Trim(), DraftPurpose.Summary), cancellationToken);
            if (!answer.IsSuccess || AppealRules.CheckDescription(answer.Value) is not null)
            {
                logger.LogWarning("AI summary for bill {Bill} not made: the provider gave no usable answer", bill.Number);
                await AttemptAsync(bill.Number, SummaryOutcomes.Failed, askedProvider: true, cancellationToken);
                continue;
            }

            if (SummaryRules.IsNotEnough(answer.Value!))
            {
                logger.LogInformation("AI summary for bill {Bill} not made: the official text is not enough", bill.Number);
                await AttemptAsync(bill.Number, SummaryOutcomes.Insufficient, askedProvider: true, cancellationToken);
                continue;
            }

            if (await StoreAsync(bill.Number, AppealRules.NormaliseText(answer.Value!), text.Address, cancellationToken))
            {
                made++;
            }
        }

        logger.LogInformation("AI summaries: {Asked} asked of the provider, {Made} made", asked, made);
        return new SummaryRun("done", asked, made);
    }

    /// <summary>Logs why nothing is made, once until the reason changes, and returns that run.</summary>
    private SummaryRun Quiet(string status, string message)
    {
        if (notices.ShouldSay(status))
        {
#pragma warning disable CA2254 // A fixed text per status, with no values in it.
            logger.LogInformation(message);
#pragma warning restore CA2254
        }

        return new SummaryRun(status, 0, 0);
    }

    /// <summary>Active bills the job may take now, in the order of design 8.13.</summary>
    private async Task<IReadOnlyList<BillFacts>> DueAsync(AppealsDb db, DateTimeOffset now, CancellationToken ct)
    {
        var active = await records.ActiveAsync(db, ct);
        var numbers = active.Select(b => b.Number).ToList();
        var onBills = await db.BillRecords.AsNoTracking().Records()
            .Where(a => numbers.Contains(a.BillNumber))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, ct);
        var recordIds = onBills.Values.Select(a => a.Id).ToList();
        var touched = (await db.Revisions.AsNoTracking()
                .Where(r => r.EntityKind == "record" && r.Field == "description" && r.ChangedByUserId != null && recordIds.Contains(r.EntityId))
                .Select(r => r.EntityId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        var attempts = await db.SummaryAttempts.AsNoTracking()
            .Where(a => numbers.Contains(a.BillNumber))
            .ToDictionaryAsync(a => a.BillNumber, a => a.RetryAfter, StringComparer.Ordinal, ct);
        var counts = await reads.CountsAsync(db, onBills.Values.Where(a => a.Status == BillRecordStatuses.Open).Select(a => a.Id).ToList(), ct);
        var planned = await agenda.ForBillsAsync(numbers, ct);

        var due = active.Where(b =>
        {
            var record = onBills.GetValueOrDefault(b.Number);
            return SummaryRules.IsDue(
                record?.Status, record?.Description, record is not null && touched.Contains(record.Id), attempts.TryGetValue(b.Number, out var retry) ? retry : null, now);
        });
        return SummaryRules.Order(
            due,
            b => planned.GetValueOrDefault(b.Number)?.Date,
            b => onBills.GetValueOrDefault(b.Number) is { } r && counts.TryGetValue(r.Id, out var c) ? c.Total : 0,
            b => b.Number);
    }

    /// <summary>
    /// Stores the summary as the bill's description, creating the record if needed, unless a person wrote or removed one
    /// meanwhile. Under the bill's lock, so a moderator's description cannot be written over (design 6.8). Audited with
    /// the system as the actor: the bill, the model, the time; never the text.
    /// </summary>
    private async Task<bool> StoreAsync(string billNumber, string summary, string address, CancellationToken ct)
    {
        var model = await settings.GetAsync(SettingKeys.AiDraftModel, ct);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var ensured = await records.EnsureAsync(db, billNumber, ct, forWrite: true);
        var record = ensured.IsSuccess ? ensured.Value : null;
        var touched = record is not null && await db.Revisions.AnyAsync(
            r => r.EntityKind == "record" && r.EntityId == record.Id && r.Field == "description" && r.ChangedByUserId != null, ct);
        if (record is null || !SummaryRules.IsDue(record.Status, record.Description, touched, null, now))
        {
            await transaction.RollbackAsync(ct);
            await AttemptAsync(billNumber, SummaryOutcomes.Overtaken, askedProvider: true, ct);
            return false;
        }

        db.Revisions.Add(AppealsService.Revise("record", record.Id, "description", null, summary, null, now));
        record.Description = summary;
        record.DescriptionSource = DescriptionSources.Ai;
        record.DescriptionUrl = address.Length <= 500 ? address : null;
        record.DescriptionAt = now;
        record.ChangedAt = now;
        Upsert(db, await db.SummaryAttempts.FirstOrDefaultAsync(a => a.BillNumber == billNumber, ct), billNumber, SummaryOutcomes.Made, true, now);
        await db.SaveChangesAsync(ct);
        var details = JsonSerializer.SerializeToElement(new { bill = billNumber, model }, JsonSerializerOptions.Web);
        await audit.WriteAsync(new AuditEntry("description.summary.made", null, "system", "bill:" + billNumber, details, null), transaction.GetDbTransaction(), ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("AI summary made for bill {Bill} with model {Model}", billNumber, model);
        return true;
    }

    private async Task AttemptAsync(string billNumber, string outcome, bool askedProvider, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        Upsert(db, await db.SummaryAttempts.FirstOrDefaultAsync(a => a.BillNumber == billNumber, ct), billNumber, outcome, askedProvider, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    private static void Upsert(AppealsDb db, SummaryAttempt? attempt, string billNumber, string outcome, bool askedProvider, DateTimeOffset now)
    {
        if (attempt is null)
        {
            attempt = new SummaryAttempt { BillNumber = billNumber };
            db.SummaryAttempts.Add(attempt);
        }

        attempt.TriedAt = now;
        attempt.Outcome = outcome;
        attempt.AskedProvider = askedProvider;
        attempt.RetryAfter = SummaryRules.RetryAfter(outcome, now);
    }
}
