namespace Lt2.Api.Contracts.Digests;

// Design 8.7, 8.8: the public record of the summaries. In M2a every summary is a dry run (status "dry-run"): made and
// published here, never sent. Kind weekly | before-vote | urgent. No MP-specific figures, no person.

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

public sealed record DigestRunResponse(
    Guid Id, string Kind, DateOnly RunDate, DateOnly? SittingDate, DateTimeOffset CreatedAt, string Status, int MpCount, IReadOnlyList<DigestItemResponse> Items);

/// <summary>One summary that held an appeal, with that appeal as the summary froze it (design 8.7: shown on the appeal page).</summary>
public sealed record AppealDigestResponse(Guid RunId, string Kind, DateOnly RunDate, DateOnly? SittingDate, string Status, int MpCount, DigestItemResponse Item);

/// <summary>What one MP would read; moderators only.</summary>
public sealed record DigestPreviewResponse(string Subject, string Text);

public sealed record DigestMpResponse(string Slug, string Status);

/// <summary>
/// The rules that decide what MPs would receive, with their current values (design 8.4: shown on the methodology page).
/// Times are Vilnius "HH:mm" or "off"; shares are fractions.
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
    int ReasonsPerSide);

/// <summary>Approving one urgent summary for an appeal (design 8.4); the reason is audited.</summary>
public sealed record UrgentDigestRequest
{
    public string Statement { get; init; } = "";
}

public static class DigestRoutes
{
    public const string Digests = ApiRoutes.Prefix + "/digests";
    public const string Rules = Digests + "/rules";
    public const string Digest = Digests + "/{date}";
    public const string OfAppeal = ApiRoutes.Prefix + "/bills/{number}/appeal/digests";
    public const string Mps = ApiRoutes.Prefix + "/moderation/digests/{id:guid}/mps";
    public const string Preview = ApiRoutes.Prefix + "/moderation/digests/{id:guid}/mps/{slug}";
    public const string Urgent = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/urgent";

    public static string DigestOn(DateOnly date) => Digests + "/" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string OfAppealOn(string number) => AppealRoutes.AppealOf(number) + "/digests";

    /// <summary>The public summary page of a day, at one bill's item in one run.</summary>
    public static string PageAt(DateOnly date, Guid runId, string number) =>
        "/suvestines/" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "#" + ItemAnchor(runId, number);

    /// <summary>The element id of one bill's item on a summary page.</summary>
    public static string ItemAnchor(Guid runId, string number) => $"item-{runId:N}-{Uri.EscapeDataString(number)}";

    public static string MpsOf(Guid id) => $"{ApiRoutes.Prefix}/moderation/digests/{id}/mps";

    public static string PreviewOf(Guid id, string slug) => $"{MpsOf(id)}/{Uri.EscapeDataString(slug)}";
}

/// <summary>Design 8.8: GET /digests, GET /digests/{date} and an appeal's summaries; and the moderators' preview of any MP's summary.</summary>
public interface IDigestsApi
{
    Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The summaries that held the published appeal on a bill, newest first (empty until the first).</summary>
    Task<ApiResult<IReadOnlyList<AppealDigestResponse>>> GetAppealDigestsAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<DigestMpResponse>>> GetMpsAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<ApiResult<DigestPreviewResponse>> GetPreviewAsync(Guid runId, string slug, CancellationToken cancellationToken = default);
}
