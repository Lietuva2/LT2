using System.Text.Json;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>Where a bill stands for statements (design 8.14.14): its facts, its record's status and whether the Seimas finished it.</summary>
internal sealed record BillForStatements(BillFacts Bill, Appeal? Record, bool Finished)
{
    /// <summary>The bill takes statements: active, and its record (if any) open.</summary>
    public bool Open => OrganisationRules.CheckBillOpen(Record?.Status, Finished) is null;

    /// <summary>Taking part was closed by the moderators: nothing of the organisations is shown either.</summary>
    public bool Hidden => Record?.Status == AppealStatuses.Rejected;
}

/// <summary>Reads shared by the organisation handlers. Nothing here returns who represents an organisation.</summary>
internal sealed class OrganisationReads(IBillCatalogue bills, AppealReads reads, IParticipants participants)
{
    /// <summary>Takes the organisation's lock for the transaction: representatives and statements change one at a time.</summary>
    public static Task LockAsync(AppealsDb db, Guid organisationId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.organisation:" + organisationId.ToString("N")], ct);

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

        // A closed record is a finished bill; without a record the final vote decides.
        var finished = record?.Status == AppealStatuses.Closed || await bills.FinalVoteAsync(number, ct) is not null;
        return new BillForStatements(bill, record, finished);
    }

    /// <summary>The counts each bill's page shows (frozen once closed), for the goals' progress; zero without a record.</summary>
    public async Task<Dictionary<string, SideCounts>> CountsAsync(AppealsDb db, IReadOnlyCollection<string> billNumbers, CancellationToken ct)
    {
        var numbers = billNumbers.Distinct().ToList();
        var records = await db.Appeals.AsNoTracking()
            .Where(a => numbers.Contains(a.BillNumber) && (a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed))
            .ToListAsync(ct);
        var live = await reads.CountsAsync(db, records.Select(r => r.Id).ToList(), ct);
        var result = numbers.ToDictionary(n => n, _ => SideCounts.None, StringComparer.Ordinal);
        foreach (var r in records)
        {
            result[r.BillNumber] = ClosingRules.Shown(live[r.Id], r.ClosedFor, r.ClosedForOlderThan30Days, r.ClosedAgainst, r.ClosedAgainstOlderThan30Days);
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

    /// <summary>A statement's version as kept in the public history (<c>revision</c>, field <c>version</c>).</summary>
    public static string VersionJson(OrganisationStatement s) =>
        JsonSerializer.Serialize(new VersionRow(s.Stance, s.Text, s.Link, s.Goal), JsonSerializerOptions.Web);

    public static StatementVersion? Version(DateTimeOffset replacedAt, string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var row = JsonSerializer.Deserialize<VersionRow>(json, JsonSerializerOptions.Web);
        return row is null ? null : new StatementVersion(replacedAt, row.Stance, row.Text, row.Link, row.Goal);
    }

    public Task<IReadOnlyDictionary<string, BillFacts>> BillsAsync(IReadOnlyCollection<string> numbers, CancellationToken ct) => reads.BillsAsync(numbers, ct);

    /// <summary>
    /// The current representatives' addresses, for a moderator's notice (design 8.14.14). Used only to write the outbox
    /// rows; never logged, never returned.
    /// </summary>
    public async Task<IReadOnlyList<string>> RepresentativeAddressesAsync(AppealsDb db, Guid organisationId, CancellationToken ct)
    {
        var users = await db.OrganisationRepresentatives.AsNoTracking()
            .Where(r => r.OrganisationId == organisationId && r.EndedAt == null)
            .Select(r => r.UserId)
            .ToListAsync(ct);
        var addresses = new List<string>();
        foreach (var user in users)
        {
            if (await participants.FindAsync(user, ct) is { } person)
            {
                addresses.Add(person.Email);
            }
        }

        return addresses;
    }

    /// <summary>A moderator's writing restriction holds for the person now (design 8.3).</summary>
    public static async Task<bool> RestrictedAsync(AppealsDb db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.WritingRestrictions.AsNoTracking()
            .Where(w => w.UserId == userId && w.LiftedAt == null)
            .Select(w => new { w.EndsAt, w.LiftedAt })
            .ToListAsync(ct);
        return !WritingRules.MayWrite(rows.Select(r => (r.EndsAt, r.LiftedAt)), now);
    }

    private sealed record VersionRow(string Stance, string Text, string? Link, int? Goal);
}
