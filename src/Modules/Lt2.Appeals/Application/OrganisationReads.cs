using System.Text.Json;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>Where a bill stands for statements (design 8.14.14): its facts, its record's status and whether the Seimas finished it.</summary>
internal sealed record BillForStatements(BillFacts Bill, BillRecord? Record, bool Finished)
{
    /// <summary>The bill takes statements: active, and its record (if any) open.</summary>
    public bool Open => OrganisationRules.CheckBillOpen(Record?.Status, Finished) is null;

    /// <summary>Taking part was closed by the moderators: nothing of the organisations is shown either.</summary>
    public bool Hidden => Record?.Status == BillRecordStatuses.Rejected;
}

/// <summary>Reads shared by the organisation handlers. Nothing here returns who represents an organisation.</summary>
internal sealed class OrganisationReads(IBillCatalogue bills, AppealReads reads, BillRecords records, IParticipants participants)
{
    /// <summary>The order of organisations' names everywhere they are listed: the Lithuanian alphabet, without case.</summary>
    public static readonly StringComparer NameOrder = StringComparer.Create(new System.Globalization.CultureInfo("lt-LT"), ignoreCase: true);

    public static OrganisationLabel Label(Organisation o) =>
        o.ConfirmedBy is null ? OrganisationLabel.None : new OrganisationLabel(o.ConfirmedBy, o.ConfirmedBy == ConfirmationKinds.Domain ? o.ConfirmedDomain : null);

    public static Task<Organisation?> BySlugAsync(AppealsDb db, string slug, bool tracking, CancellationToken ct)
    {
        var key = (slug ?? "").Trim().ToLowerInvariant();
        return (tracking ? db.Organisations : db.Organisations.AsNoTracking()).FirstOrDefaultAsync(o => o.Slug == key, ct);
    }

    public static Task<int> ActiveRepresentativesAsync(AppealsDb db, Guid organisationId, CancellationToken ct) =>
        db.OrganisationRepresentatives.CountAsync(r => r.OrganisationId == organisationId && r.EndedAt == null, ct);

    public static Task<bool> IsRepresentativeAsync(AppealsDb db, Guid organisationId, Guid userId, CancellationToken ct) =>
        db.OrganisationRepresentatives.AnyAsync(r => r.OrganisationId == organisationId && r.UserId == userId && r.EndedAt == null, ct);

    /// <summary>The bill for statements, or null when it is not in the catalogue.</summary>
    public async Task<BillForStatements?> BillAsync(AppealsDb db, string billNumber, CancellationToken ct)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        var bill = await bills.FindAsync(number, ct);
        if (bill is null)
        {
            return null;
        }

        var record = await AppealReads.FindRecordAsync(db, number, tracking: false, ct);
        return new BillForStatements(bill, record, await records.IsFinishedAsync(record, number, ct));
    }

    /// <summary>
    /// <see cref="BillAsync"/> for many bills in a few queries: bills not in the catalogue are left out. Finished is read from
    /// the active bills, the same rule as <see cref="BillRecords.IsFinishedAsync"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, BillForStatements>> BillsAsync(AppealsDb db, IReadOnlyCollection<string> billNumbers, CancellationToken ct)
    {
        var numbers = billNumbers.Select(AppealReads.NormaliseBillNumber).Distinct().ToList();
        if (numbers.Count == 0)
        {
            return new Dictionary<string, BillForStatements>(StringComparer.Ordinal);
        }

        var facts = await bills.FindManyAsync(numbers, ct);
        var active = (await records.ActiveAsync(db, ct)).Select(b => b.Number).ToHashSet(StringComparer.Ordinal);
        var onBills = await db.BillRecords.AsNoTracking().Records().Where(a => numbers.Contains(a.BillNumber)).ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, ct);
        return numbers.Where(facts.ContainsKey).ToDictionary(
            n => n,
            n =>
            {
                var record = onBills.GetValueOrDefault(n);
                return new BillForStatements(facts[n], record, RecordRules.IsFinished(record?.Status, finalVoteRecorded: !active.Contains(n)));
            },
            StringComparer.Ordinal);
    }

    /// <summary>The counts each bill's page shows (frozen once closed), for the goals' progress; zero without a record.</summary>
    public async Task<Dictionary<string, SideCounts>> CountsAsync(AppealsDb db, IReadOnlyCollection<string> billNumbers, CancellationToken ct)
    {
        var numbers = billNumbers.Distinct().ToList();
        var onBills = await db.BillRecords.AsNoTracking()
            .Where(a => numbers.Contains(a.BillNumber) && (a.Status == BillRecordStatuses.Open || a.Status == BillRecordStatuses.Closed))
            .ToListAsync(ct);
        var shown = await reads.ShownCountsAsync(db, onBills, ct);
        var result = numbers.ToDictionary(n => n, _ => SideCounts.None, StringComparer.Ordinal);
        foreach (var r in onBills)
        {
            result[r.BillNumber] = shown[r.Id];
        }

        return result;
    }

    public static StatementInfo Info(OrganisationStatement s, SideCounts? counts) => new(
        s.Id,
        s.Stance,
        s.Text,
        s.Link,
        s.Link is { } link ? HttpsAddress.Host(link) : null,
        s.Goal,
        counts is { } c ? OrganisationRules.Progress(s.Stance, s.Goal, c.For, c.Against) : null,
        s.PublishedAt,
        s.EditedAt,
        s.Status);

    /// <summary>
    /// A statement's version as kept in the public history (<c>revision</c>, field <c>version</c>). Only a version that was
    /// public is kept (design 6.8, <see cref="OrganisationRules.KeepsVersion"/>), and is marked so.
    /// </summary>
    public static string VersionJson(OrganisationStatement s) =>
        JsonSerializer.Serialize(new VersionRow(s.Stance, s.Text, s.Link, s.Goal, WasPublic: true), JsonSerializerOptions.Web);

    /// <summary>A kept version as shown, or null when it is not to be shown (design 6.8: never a text that was not public).</summary>
    public static StatementVersion? Version(DateTimeOffset replacedAt, string? json, DateTimeOffset? firstPublicAt)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var row = JsonSerializer.Deserialize<VersionRow>(json, JsonSerializerOptions.Web);
        if (row is null || !OrganisationRules.ShowsVersion(row.WasPublic, replacedAt, firstPublicAt, RuleBasedPreScreening.Screen(row.Text).Count > 0))
        {
            return null;
        }

        return new StatementVersion(replacedAt, row.Stance, row.Text, row.Link, row.Goal);
    }

    public Task<IReadOnlyDictionary<string, BillFacts>> BillFactsAsync(IReadOnlyCollection<string> numbers, CancellationToken ct) => reads.BillsAsync(numbers, ct);

    /// <summary>
    /// The current representatives' addresses, for a moderator's notice (design 8.14.14). Used only to write the outbox
    /// rows; never logged, never returned. At most ten people: <see cref="IParticipants"/> has no lookup of many.
    /// </summary>
    public async Task<IReadOnlyList<string>> RepresentativeAddressesAsync(AppealsDb db, Guid organisationId, CancellationToken ct)
    {
        var users = await db.OrganisationRepresentatives.AsNoTracking()
            .Where(r => r.OrganisationId == organisationId && r.EndedAt == null)
            .Select(r => r.UserId)
            .ToListAsync(ct);
        var people = await participants.FindManyAsync(users, ct);
        return users.Select(user => people.GetValueOrDefault(user)?.Email).OfType<string>().ToList();
    }

    /// <summary>
    /// A version kept in the history. <see cref="WasPublic"/> is true on every version kept since 2026-11-15; versions kept
    /// before then do not say, and <see cref="OrganisationRules.ShowsVersion"/> decides for them.
    /// </summary>
    private sealed record VersionRow(string Stance, string Text, string? Link, int? Goal, bool? WasPublic = null);
}
