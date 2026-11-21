using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Digests;

// Design 8.7, 8.8, 8.19: the public record of the summaries. Status dry-run | sent | cancelled in public (8.19.1; prepared and
// dropped only on the moderators' screen): a dry run is made and published, never sent. Kind weekly | before-vote | urgent. No
// MP-specific figures, no person, and never which MPs a summary went to.

public sealed record DigestReasonResponse(string Side, string Text, int Endorsements);

/// <summary>
/// An organisation named in a summary under a bill (design 8.14.4): its stance (<c>for</c>, <c>against</c>, <c>remarks</c>), its
/// name and profile address, and the statement its name links to on the bill's page
/// (<c>BillTextLinks.ToStatement</c>). Never the statement's text.
/// </summary>
public sealed record DigestOrganisationResponse(string Stance, Guid OrganisationId, string Slug, string Name, Guid StatementId);

/// <remarks>
/// <c>ForReasonsOf</c> and <c>AgainstReasonsOf</c> (design 8.4, owner 2026-11-20): the published arguments a side has, set only when the
/// arguments listed are not all of them (the heading reads "(iš N)"); null otherwise, and in a snapshot made before it was kept.
/// <c>Organisations</c> (design 8.14.4): the organisations named under the bill, for, against, then remarks, each stance in the
/// order published; empty when none qualified, null in a snapshot made before the line was kept.
/// </remarks>
public sealed record DigestItemResponse(
    string BillNumber,
    string BillTitle,
    string? Stage,
    DateOnly? PlannedDate,
    int For,
    int ForOlderThan30Days,
    int Against,
    int AgainstOlderThan30Days,
    int? SurgeSharePercent,
    int? SurgeWindowHours,
    IReadOnlyList<DigestReasonResponse> Reasons,
    int? ForReasonsOf = null,
    int? AgainstReasonsOf = null,
    IReadOnlyList<DigestOrganisationResponse>? Organisations = null);

/// <summary>
/// A summary as its public copy shows it. <paramref name="CreatedAt"/>: when it was prepared; <paramref name="MpCount"/>: MPs it
/// went to (or would have, a dry run); <paramref name="SentCount"/>: MPs it was sent to; <paramref name="SentAt"/>: when it went,
/// its numbers being of that moment; <paramref name="Statement"/>: a declined summary's statement (never who declined it).
/// </summary>
public sealed record DigestRunResponse(
    Guid Id,
    string Kind,
    DateOnly RunDate,
    DateOnly? SittingDate,
    DateTimeOffset CreatedAt,
    string Status,
    int MpCount,
    IReadOnlyList<DigestItemResponse> Items,
    int SentCount = 0,
    DateTimeOffset? SentAt = null,
    string? Statement = null);

/// <summary>One summary that held a record, with that record as the summary froze it (design 8.7: shown on the record page).</summary>
public sealed record RecordDigestResponse(Guid RunId, string Kind, DateOnly RunDate, DateOnly? SittingDate, string Status, int MpCount, DigestItemResponse Item);

/// <summary>
/// The next summary that would hold a bill's record (design 8.12.2, owner 2026-11-18): the Vilnius day it goes, a prepared
/// summary's own or the one the rules give now; null when none is known (not eligible, held, or no sitting known yet). The
/// bill's page says it in place of the threshold's ruler once the threshold is reached.
/// </summary>
public sealed record RecordNextDigestResponse(DateOnly? Day);

/// <summary>
/// The summary a list row names for a bill whose threshold is reached (design 8.12.2, owner 2026-11-21), in place of "riba
/// pasiekta": <c>Kind</c> <c>planned</c> with the Vilnius day the next summary that would hold it is planned, or <c>sent</c>
/// with the day the last summary really sent to MPs that held it went (<see cref="RowSummaryKinds"/>).
/// </summary>
public sealed record RecordSummaryResponse(string Kind, DateOnly Day);

/// <summary>
/// An argument of a summary on the moderators' screen (design 8.19.2): <paramref name="Id"/> while the summary is prepared (for
/// "Paslėpti"), null once gone; <paramref name="Replacement"/>: it came in for a hidden one ("Įtrauktas vietoj paslėpto").
/// </summary>
public sealed record DigestReviewReasonResponse(Guid? Id, string Side, string Text, int Endorsements, bool Replacement);

/// <summary>
/// A bill of a summary on the moderators' screen: for a prepared summary its numbers now, <paramref name="Held"/> (it drops out
/// unless released) and <paramref name="Gone"/> (no longer open: it drops out); <paramref name="RecordId"/> for "Sulaikyti".
/// <paramref name="Organisations"/>: the organisations' line (design 8.14.4), for a prepared summary as it would go now (a
/// moderator may keep one out, "Neįtraukti į suvestines"), for a gone one as it went; null in a snapshot made before it was kept.
/// </summary>
public sealed record DigestReviewItemResponse(
    Guid RecordId,
    string BillNumber,
    string BillTitle,
    string? Stage,
    DateOnly? PlannedDate,
    int For,
    int ForOlderThan30Days,
    int Against,
    int AgainstOlderThan30Days,
    int? SurgeSharePercent,
    int? SurgeWindowHours,
    bool Held,
    bool Gone,
    IReadOnlyList<DigestReviewReasonResponse> Reasons,
    int? ForReasonsOf = null,
    int? AgainstReasonsOf = null,
    IReadOnlyList<DigestOrganisationResponse>? Organisations = null);

/// <summary>
/// A summary on the moderators' screen (design 8.19.2, owner 2026-11-17): its common text once, as MPs read it. Status
/// <c>prepared</c> (goes at <paramref name="DueAt"/>; <paramref name="ApprovedAt"/> once a moderator pressed "Patvirtinti"),
/// <c>dry-run</c>, <c>sent</c>, <c>cancelled</c> (with the <paramref name="Statement"/>) or <c>dropped</c> (no bill was left at
/// its time). <paramref name="MpCount"/>: MPs it would go to now (prepared) or went to.
/// </summary>
public sealed record DigestReviewRunResponse(
    Guid Id,
    string Kind,
    DateOnly RunDate,
    DateOnly? SittingDate,
    DateTimeOffset PreparedAt,
    DateTimeOffset? DueAt,
    string Status,
    int MpCount,
    DateTimeOffset? SentAt,
    DateTimeOffset? ApprovedAt,
    string? Statement,
    IReadOnlyList<DigestReviewItemResponse> Items);

/// <summary>
/// The prepared summary nobody approved or declined yet that goes first (design 8.19.2): in <c>GET /me</c> for a staff account
/// only, from the browser only, for the box on every page.
/// </summary>
public sealed record DigestReviewNoticeResponse(DateTimeOffset DueAt, int Bills, int Reasons);

/// <summary>
/// The rules that decide what MPs receive, with their current values (design 8.4: shown on the methodology page).
/// Times are Vilnius "HH:mm" or "off"; shares are fractions. <paramref name="Sending"/> is sending as it is now (design 8.19.1:
/// <c>off</c> or <c>automatic</c>), <paramref name="SendingSetting"/> what the setting says, and <paramref name="ReviewHours"/>
/// how long before its time a summary is prepared for review. <paramref name="GroupMinimum"/> is the setting
/// <c>Digest:PartyGroupMinimum</c> (design 8.4): the smallest party group on a side whose number is shown; 0 or 1 = any group
/// of one or more.
/// </summary>
public sealed record DigestRulesResponse(
    int ThresholdDefault,
    int ThresholdMinimum,
    string Weekly,
    string BeforeVote,
    int MaxPerWeek,
    decimal NotableChange,
    decimal SurgeMinimumShare,
    int SurgeMinimumAccounts,
    int SurgeWindowHours,
    int GroupMinimum,
    int OlderThanDays,
    int ReasonsPerSide,
    string Sending = "off",
    string SendingSetting = "off",
    int ReviewHours = 24);

/// <summary>
/// The moderators' summaries screen (design 8.19.2): sending as it is now with its lock (<c>no-introduction</c>,
/// <c>not-caught</c> or null), and the summaries, prepared ones first.
/// </summary>
public sealed record DigestModerationResponse(string Sending, string SendingSetting, string? Lock, IReadOnlyList<DigestReviewRunResponse> Runs);

/// <summary>Approving one urgent summary for a record (design 8.4); the reason is audited.</summary>
public sealed record UrgentDigestRequest
{
    public string Statement { get; init; } = "";
}

/// <summary>"Nesiųsti" (design 8.19.2): the statement, at least 20 characters, shown on the summary's public copy.</summary>
public sealed record CancelDigestRequest
{
    public string Statement { get; init; } = "";
}

public sealed class CancelDigestRequestValidator : AbstractValidator<CancelDigestRequest>
{
    public const int Minimum = 20;

    public CancelDigestRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Statement).Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Statement.Required"])
            .Must(t => t.Trim().Length >= Minimum).WithMessage(l["Digest.CancelTooShort"])
            .Must(t => t.Trim().Length <= Appeals.AppealLimits.StatementMaxLength).WithMessage(l["Statement.TooLong"]);
}

public static class DigestRoutes
{
    public const string Digests = ApiRoutes.Prefix + "/digests";
    public const string Rules = Digests + "/rules";
    public const string Digest = Digests + "/{date}";
    public const string OfRecord = ApiRoutes.Prefix + "/bills/{number}/record/digests";
    public const string NextOfRecord = OfRecord + "/next";
    public const string Urgent = ApiRoutes.Prefix + "/moderation/records/{id:guid}/urgent";

    /// <summary><c>GET</c>, moderators: the summaries screen (design 8.19.2).</summary>
    public const string Moderation = ApiRoutes.Prefix + "/moderation/digests";

    /// <summary>
    /// <c>POST</c>, moderators in a staff-passkey session: "Patvirtinti" (owner, 2026-11-17): the prepared summary is marked
    /// reviewed; it still goes at its own time. No fresh passkey confirmation: it sends nothing.
    /// </summary>
    public const string Approve = Moderation + "/{id:guid}/approve";

    /// <summary><c>POST</c>, moderators in a staff-passkey session: "Nesiųsti" with a statement.</summary>
    public const string Cancel = Moderation + "/{id:guid}/cancel";

    /// <summary><c>GET</c>, public: the introduction letter as sent, with earlier versions (design 8.19.5).</summary>
    public const string Introduction = Digests + "/introduction";

    /// <summary>The moderators' summaries screen.</summary>
    public const string ModerationPage = "/moderavimas/suvestines";

    /// <summary>The public page of the introduction letter.</summary>
    public const string IntroductionPage = "/suvestines/pristatymas";

    public static string DigestOn(DateOnly date) => Digests + "/" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string OfRecordOn(string number) => AppealRoutes.RecordOf(number) + "/digests";

    public static string NextOfRecordOn(string number) => OfRecordOn(number) + "/next";

    /// <summary>The query parameter that puts one bill's item first on a summary page (owner, 2026-11-17).</summary>
    public const string BillParameter = "projektas";

    /// <summary>
    /// The public summary page of a day with one bill's item first (owner, 2026-11-17): <c>?projektas=</c>, not a fragment, so the
    /// page opens at its title and the item is the first thing under it, nothing scrolled away. The day's summaries that hold
    /// the bill come before those that do not, so <paramref name="runId"/> is not in the address; it stays in the signature,
    /// which the bill's page calls.
    /// </summary>
    public static string PageAt(DateOnly date, Guid runId, string number) =>
        PageOf(date) + "?" + BillParameter + "=" + Uri.EscapeDataString(number);

    /// <summary>The public summary page of a day.</summary>
    public static string PageOf(DateOnly date) =>
        "/suvestines/" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The element id of one bill's item on a summary page.</summary>
    public static string ItemAnchor(Guid runId, string number) => $"item-{runId:N}-{Uri.EscapeDataString(number)}";

    /// <summary>
    /// The element id of one argument on a summary page: the run, the bill, the side and its place on that side (1, 2 …),
    /// as the snapshot holds them (design 8.19.3, owner 2026-11-16). The MPs' email links "Visas argumentas" here; the
    /// Notifications module writes the same (<c>MpMailPaths.ReasonOnPublicCopy</c>, pinned by a unit test).
    /// </summary>
    public static string ReasonAnchor(Guid runId, string number, string side, int place) =>
        $"reason-{runId:N}-{Uri.EscapeDataString(number)}-{side}-{place.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string ApproveOf(Guid id) => $"{Moderation}/{id}/approve";

    public static string CancelOf(Guid id) => $"{Moderation}/{id}/cancel";
}

/// <summary>One version of the introduction letter as sent (design 8.19.5): public, with its date and signer.</summary>
public sealed record IntroductionLetterResponse(int Version, string Subject, string Text, string SignerName, string SignerRole, DateTimeOffset? SentAt);

/// <summary>
/// Design 8.8, 8.19: GET /digests, GET /digests/{date} and a record's summaries; the introduction letter; and the moderators'
/// screen, "Patvirtinti" and "Nesiųsti".
/// </summary>
public interface IDigestsApi
{
    Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The summaries that held the published record on a bill, newest first (empty until the first).</summary>
    Task<ApiResult<IReadOnlyList<RecordDigestResponse>>> GetRecordDigestsAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The day the next summary holding the bill's record goes, or none known; not found like the record itself.</summary>
    Task<ApiResult<RecordNextDigestResponse>> GetRecordNextDigestAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The versions of the introduction letter sent, newest first; empty before the first (public).</summary>
    Task<ApiResult<IReadOnlyList<IntroductionLetterResponse>>> GetIntroductionAsync(CancellationToken cancellationToken = default);

    /// <summary>The moderators' summaries screen: sending as it is now and the summaries, prepared ones first.</summary>
    Task<ApiResult<DigestModerationResponse>> GetModerationAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRunResponse>> ApproveAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRunResponse>> CancelAsync(Guid runId, CancelDigestRequest request, CancellationToken cancellationToken = default);
}
