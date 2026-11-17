using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Digests;

// Design 8.7, 8.8, 8.19: the public record of the summaries. Status dry-run | waiting | sent | cancelled | expired (8.19.1):
// a dry run is made and published, never sent. Kind weekly | before-vote | urgent. No MP-specific figures, no person, and never
// which MPs a summary went to.

public sealed record DigestReasonResponse(string Side, string Text, int Endorsements);

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
    IReadOnlyList<DigestReasonResponse> Reasons);

/// <summary>
/// A summary as its public copy shows it. <paramref name="MpCount"/>: MPs it would go to when made; <paramref name="SentCount"/>:
/// MPs it was sent to; <paramref name="DecidedAt"/>: when it was sent, declined or expired; <paramref name="Statement"/>: a
/// declined summary's statement (never who declined it).
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
    DateTimeOffset? DecidedAt = null,
    string? Statement = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>One summary that held a record, with that record as the summary froze it (design 8.7: shown on the record page).</summary>
public sealed record RecordDigestResponse(Guid RunId, string Kind, DateOnly RunDate, DateOnly? SittingDate, string Status, int MpCount, DigestItemResponse Item);

/// <summary>What one MP would read; moderators only.</summary>
public sealed record DigestPreviewResponse(string Subject, string Text);

/// <summary>An MP of a summary and what happened, or would happen, for the MP's own address (design 8.19.3); staff only.</summary>
public sealed record DigestMpResponse(string Slug, string Status, string FirstName = "", string LastName = "");

/// <summary>
/// The rules that decide what MPs receive, with their current values (design 8.4: shown on the methodology page).
/// Times are Vilnius "HH:mm" or "off"; shares are fractions. <paramref name="Sending"/> is sending as it is now (design 8.19.1:
/// <c>off</c>, <c>approval</c>, <c>automatic</c>), <paramref name="SendingSetting"/> what the setting says, and
/// <paramref name="ApprovalHours"/> how long a summary waits for a moderator. <paramref name="GroupMinimum"/> is the setting
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
    int ApprovalHours = 24);

/// <summary>
/// The moderators' summaries screen (design 8.19.2): sending as it is now with its lock (<c>no-introduction</c>,
/// <c>not-caught</c> or null), and the summaries, waiting ones first.
/// </summary>
public sealed record DigestModerationResponse(string Sending, string SendingSetting, string? Lock, IReadOnlyList<DigestRunResponse> Runs);

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
    public const string Mps = ApiRoutes.Prefix + "/moderation/digests/{id:guid}/mps";
    public const string Preview = ApiRoutes.Prefix + "/moderation/digests/{id:guid}/mps/{slug}";
    public const string Urgent = ApiRoutes.Prefix + "/moderation/records/{id:guid}/urgent";

    /// <summary><c>GET</c>, moderators: the summaries screen (design 8.19.2).</summary>
    public const string Moderation = ApiRoutes.Prefix + "/moderation/digests";

    /// <summary><c>POST</c>, moderators in a staff-passkey session: "Siųsti".</summary>
    public const string Send = Moderation + "/{id:guid}/send";

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

    /// <summary>The public summary page of a day, at one bill's item in one run.</summary>
    public static string PageAt(DateOnly date, Guid runId, string number) =>
        "/suvestines/" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "#" + ItemAnchor(runId, number);

    /// <summary>The element id of one bill's item on a summary page.</summary>
    public static string ItemAnchor(Guid runId, string number) => $"item-{runId:N}-{Uri.EscapeDataString(number)}";

    /// <summary>
    /// The element id of one argument on a summary page: the run, the bill, the side and its place on that side (1, 2 …),
    /// as the snapshot holds them (design 8.19.3, owner 2026-11-16). The MPs' email links "Visas argumentas" here; the
    /// Notifications module writes the same (<c>MpMailPaths.ReasonOnPublicCopy</c>, pinned by a unit test).
    /// </summary>
    public static string ReasonAnchor(Guid runId, string number, string side, int place) =>
        $"reason-{runId:N}-{Uri.EscapeDataString(number)}-{side}-{place.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string MpsOf(Guid id) => $"{ApiRoutes.Prefix}/moderation/digests/{id}/mps";

    public static string PreviewOf(Guid id, string slug) => $"{MpsOf(id)}/{Uri.EscapeDataString(slug)}";

    public static string SendOf(Guid id) => $"{Moderation}/{id}/send";

    public static string CancelOf(Guid id) => $"{Moderation}/{id}/cancel";
}

/// <summary>One version of the introduction letter as sent (design 8.19.5): public, with its date and signer.</summary>
public sealed record IntroductionLetterResponse(int Version, string Subject, string Text, string SignerName, string SignerRole, DateTimeOffset? SentAt);

/// <summary>
/// Design 8.8, 8.19: GET /digests, GET /digests/{date} and a record's summaries; the introduction letter; and the moderators'
/// screen, preview, "Siųsti" and "Nesiųsti".
/// </summary>
public interface IDigestsApi
{
    Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The summaries that held the published record on a bill, newest first (empty until the first).</summary>
    Task<ApiResult<IReadOnlyList<RecordDigestResponse>>> GetRecordDigestsAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<DigestMpResponse>>> GetMpsAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<ApiResult<DigestPreviewResponse>> GetPreviewAsync(Guid runId, string slug, CancellationToken cancellationToken = default);

    /// <summary>The versions of the introduction letter sent, newest first; empty before the first (public).</summary>
    Task<ApiResult<IReadOnlyList<IntroductionLetterResponse>>> GetIntroductionAsync(CancellationToken cancellationToken = default);

    /// <summary>The moderators' summaries screen: sending as it is now and the summaries, waiting ones first.</summary>
    Task<ApiResult<DigestModerationResponse>> GetModerationAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRunResponse>> SendAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRunResponse>> CancelAsync(Guid runId, CancelDigestRequest request, CancellationToken cancellationToken = default);
}
