using Lt2.Appeals.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Appeals.Application;

/// <summary>
/// The module's transaction advisory locks, each held until its transaction ends. When more than one is needed they are
/// taken in this order: a person's (or one account's comments under one text), then an argument's, then a bill's or an
/// organisation's.
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

    /// <summary>The refresh of the bill search copy (design 8.12.5): one at a time; it takes no bill's lock.</summary>
    public static Task BillCardsAsync(AppealsDb db, CancellationToken ct) => TakeAsync(db, "lt2.bill-cards", ct);

    /// <summary>An organisation: its representatives and statements change one at a time.</summary>
    public static Task OrganisationAsync(AppealsDb db, Guid organisationId, CancellationToken ct) =>
        TakeAsync(db, "lt2.organisation:" + organisationId.ToString("N"), ct);

    /// <summary>One moderator's AI draft requests, so the hourly limit holds under parallel requests.</summary>
    public static Task AiDraftsAsync(AppealsDb db, Guid moderatorUserId, CancellationToken ct) =>
        TakeAsync(db, "lt2.ai-draft:" + moderatorUserId.ToString("N"), ct);

    /// <summary>One MP's response on one bill (design 8.5.5): the MP's account, the office members and a moderator change it one at a time.</summary>
    public static Task MpResponseAsync(AppealsDb db, long seimasPersonId, string billNumber, CancellationToken ct) =>
        TakeAsync(db, "lt2.mp-response:" + seimasPersonId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + AppealReads.NormaliseBillNumber(billNumber), ct);

    /// <summary>
    /// One account's comments under one text (design 8.23.4): the ceiling per person per text holds under concurrent requests.
    /// No bill lock: a comment changes nothing on the bill's record.
    /// </summary>
    public static Task CommentsAsync(AppealsDb db, Guid userId, Guid targetId, CancellationToken ct) =>
        TakeAsync(db, "lt2.comments:" + userId.ToString("N") + ":" + targetId.ToString("N"), ct);

    /// <summary>
    /// One argument (design 8.2, owner 2026-11-17): its author's edit and a reaction to it ("Man svarbu", a comment under it) happen
    /// one at a time, so an edit never lands after a reaction. Taken after the person's or the comments' lock, never before.
    /// </summary>
    public static Task ReasonAsync(AppealsDb db, Guid reasonId, CancellationToken ct) =>
        TakeAsync(db, "lt2.reason:" + reasonId.ToString("N"), ct);

    /// <summary>One comment: editing, deleting, hiding, restoring and agreeing with it happen one at a time.</summary>
    public static Task CommentAsync(AppealsDb db, Guid commentId, CancellationToken ct) =>
        TakeAsync(db, "lt2.comment:" + commentId.ToString("N"), ct);

    private static Task<int> TakeAsync(AppealsDb db, string key, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(Sql, [new NpgsqlParameter("key", key)], ct);
}
