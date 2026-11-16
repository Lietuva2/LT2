using Lt2.Appeals.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Appeals.Application;

/// <summary>
/// The module's transaction advisory locks, each held until its transaction ends. When more than one is needed they are
/// taken in this order: a person's, then a bill's or an organisation's.
/// </summary>
internal static class AdvisoryLocks
{
    /// <summary>The statement every lock runs, with the key as the parameter <c>@key</c>.</summary>
    public const string Sql = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";

    /// <summary>
    /// One person's participation (positions, arguments, endorsements, consent withdrawal, organisations registered,
    /// statements written): per-person limits and the consent check hold under concurrent requests.
    /// </summary>
    public static string PersonKey(Guid userId) => "lt2.participation:" + userId.ToString("N");

    public static Task PersonAsync(AppealsDb db, Guid userId, CancellationToken ct) => TakeAsync(db, PersonKey(userId), ct);

    /// <summary>A bill's record (design 6.8): creating it, and every write to it by a moderator, the summary job or the closing job.</summary>
    public static Task BillAsync(AppealsDb db, string billNumber, CancellationToken ct) => TakeAsync(db, "lt2.appeal-bill:" + AppealReads.NormaliseBillNumber(billNumber), ct);

    /// <summary>An organisation: its representatives and statements change one at a time.</summary>
    public static Task OrganisationAsync(AppealsDb db, Guid organisationId, CancellationToken ct) =>
        TakeAsync(db, "lt2.organisation:" + organisationId.ToString("N"), ct);

    /// <summary>One moderator's AI draft requests, so the hourly limit holds under parallel requests.</summary>
    public static Task AiDraftsAsync(AppealsDb db, Guid moderatorUserId, CancellationToken ct) =>
        TakeAsync(db, "lt2.ai-draft:" + moderatorUserId.ToString("N"), ct);

    /// <summary>One MP's response on one bill (design 8.5.5): the MP's account, the office members and a moderator change it one at a time.</summary>
    public static Task MpResponseAsync(AppealsDb db, long seimasPersonId, string billNumber, CancellationToken ct) =>
        TakeAsync(db, "lt2.mp-response:" + seimasPersonId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + AppealReads.NormaliseBillNumber(billNumber), ct);

    private static Task<int> TakeAsync(AppealsDb db, string key, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(Sql, [new NpgsqlParameter("key", key)], ct);
}
