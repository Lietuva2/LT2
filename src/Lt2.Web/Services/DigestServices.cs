using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

internal static class DigestMapping
{
    public static DigestRunResponse Map(DigestRunInfo r) => new(
        r.Id, r.Kind, r.RunDate, r.SittingDate, r.CreatedAt, r.Status, r.MpCount, r.Items.Select(Map).ToList(), r.SentCount, r.SentAt, r.Statement);

    public static DigestReviewRunResponse Map(DigestReview r) => new(
        r.Id, r.Kind, r.RunDate, r.SittingDate, r.PreparedAt, r.DueAt, r.Status, r.MpCount, r.SentAt, r.ApprovedAt, r.Statement,
        r.Items.Select(i => new DigestReviewItemResponse(
            i.RecordId, i.BillNumber, i.BillTitle, i.Stage, i.PlannedDate, i.For, i.ForOlderThan30Days, i.Against, i.AgainstOlderThan30Days,
            i.SurgeSharePercent, i.SurgeWindowHours, i.Held, i.Gone,
            i.Reasons.Select(x => new DigestReviewReasonResponse(x.Id, x.Side, x.Text, x.Endorsements, x.Replacement)).ToList())).ToList());

    public static IntroductionLetterResponse Public(IntroductionVersion v) => new(v.Version, v.Subject, v.Text, v.SignerName, v.SignerRole, v.SentAt);

    public static IntroductionVersionResponse Map(IntroductionVersion v) => new(v.Version, v.Subject, v.Text, v.SignerName, v.SignerRole, v.WrittenAt, v.SentAt);

    /// <summary>The moderators' summaries screen (design 8.19.2): sending now, the prepared summaries first, then the latest gone.</summary>
    public static async Task<DigestModerationResponse> ModerationAsync(IDigests digests, CancellationToken ct)
    {
        var sending = await digests.SendingAsync(ct);
        var runs = await digests.ReviewAsync(ct);
        return new DigestModerationResponse(sending.Mode, sending.Setting, sending.Lock, runs.Select(Map).ToList());
    }

    /// <summary>
    /// The summaries that held the bill on <paramref name="number"/> (design 8.7); none for an active bill without a record,
    /// not found like the bill itself, and not found for a hidden record.
    /// </summary>
    public static async Task<Result<List<RecordDigestResponse>>> ForRecordAsync(
        Lt2.Appeals.IAppealsModule appeals, IDigests digests, string number, CancellationToken ct)
    {
        var record = await appeals.GetAsync(number, ct);
        if (!record.IsSuccess)
        {
            return record.Error!;
        }

        if (record.Value.Status is not ("open" or "closed"))
        {
            return Error.NotFound("record.not-found");
        }

        if (record.Value.Id is not { } id)
        {
            return new List<RecordDigestResponse>();
        }

        // Each run of ForRecordAsync carries exactly this record's item.
        return (await digests.ForRecordAsync(id, ct))
            .Select(r => new RecordDigestResponse(r.Id, r.Kind, r.RunDate, r.SittingDate, r.Status, r.MpCount, Map(r.Items.Single())))
            .ToList();
    }

    private static DigestItemResponse Map(DigestItemInfo i) => new(
        i.BillNumber, i.BillTitle, i.Stage, i.PlannedDate, i.For, i.ForOlderThan30Days, i.Against, i.AgainstOlderThan30Days,
        i.SurgeSharePercent, i.SurgeWindowHours,
        i.Reasons.Select(x => new DigestReasonResponse(x.Side, x.Text, x.Endorsements)).ToList());
}

/// <summary>Server-side <see cref="IDigestsApi"/> for the pages; the moderators' calls need an active moderator role.</summary>
internal sealed class ServerDigestsApi(
    IDigests digests, IMpMailAdministration mpMail, Lt2.Appeals.IAppealsModule appeals, ISettings settings, IHttpContextAccessor accessor, ErrorMapper errors) : IDigestsApi
{
    public async Task<ApiResult<DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        ApiResult<DigestRulesResponse>.Ok(await RulesAsync(settings, digests, cancellationToken));

    /// <summary>The current values of every rule that decides what MPs receive, and sending as it is now (design 8.19.1).</summary>
    public static async Task<DigestRulesResponse> RulesAsync(ISettings settings, IDigests digests, CancellationToken ct)
    {
        var minimum = await settings.GetAsync(SettingKeys.DigestThresholdMinimum, ct);
        var sending = await digests.SendingAsync(ct);
        return new DigestRulesResponse(
            Math.Max(await settings.GetAsync(SettingKeys.DigestThresholdDefault, ct), minimum),
            minimum,
            await settings.GetAsync(SettingKeys.DigestWeekly, ct),
            await settings.GetAsync(SettingKeys.DigestBeforeVote, ct),
            await settings.GetAsync(SettingKeys.DigestMaxPerWeek, ct),
            await settings.GetAsync(SettingKeys.DigestNotableChange, ct),
            await settings.GetAsync(SettingKeys.SurgeMinimumShare, ct),
            await settings.GetAsync(SettingKeys.SurgeMinimumAccounts, ct),
            await settings.GetAsync(SettingKeys.SurgeWindowHours, ct),
            GroupMinimum: await settings.GetAsync(SettingKeys.DigestPartyGroupMinimum, ct),
            OlderThanDays: 30,
            ReasonsPerSide: 2,
            Sending: sending.Mode,
            SendingSetting: sending.Setting,
            ReviewHours: await settings.GetAsync(SettingKeys.DigestReviewHours, ct));
    }

    public async Task<ApiResult<IReadOnlyList<IntroductionLetterResponse>>> GetIntroductionAsync(CancellationToken cancellationToken = default) =>
        ApiResult<IReadOnlyList<IntroductionLetterResponse>>.Ok(
            (await mpMail.SentIntroductionsAsync(cancellationToken)).Select(DigestMapping.Public).ToList());

    public async Task<ApiResult<DigestModerationResponse>> GetModerationAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<DigestModerationResponse>.Ok(await DigestMapping.ModerationAsync(digests, cancellationToken))
            : ApiResult<DigestModerationResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<DigestRunResponse>> ApproveAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await ModeratorAsync() is { } moderator
            ? errors.ToApiResult(await digests.ApproveAsync(runId, moderator, cancellationToken), DigestMapping.Map)
            : ApiResult<DigestRunResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<DigestRunResponse>> CancelAsync(Guid runId, CancelDigestRequest request, CancellationToken cancellationToken = default) =>
        await ModeratorAsync() is { } moderator
            ? errors.ToApiResult(await digests.CancelAsync(runId, request?.Statement ?? "", moderator, cancellationToken), DigestMapping.Map)
            : ApiResult<DigestRunResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    private async Task<StaffActor?> ModeratorAsync() =>
        accessor.HttpContext is { } http && await IsModeratorAsync()
            ? await Endpoints.Moderation.ModeratorEndpoint.ModeratorAsync(http)
            : null;

    public async Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default) =>
        ApiResult<IReadOnlyList<DigestRunResponse>>.Ok((await digests.ListAsync(cancellationToken)).Select(DigestMapping.Map).ToList());

    public async Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        ApiResult<IReadOnlyList<DigestRunResponse>>.Ok((await digests.OnDateAsync(date, cancellationToken)).Select(DigestMapping.Map).ToList());

    public async Task<ApiResult<IReadOnlyList<RecordDigestResponse>>> GetRecordDigestsAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await DigestMapping.ForRecordAsync(appeals, digests, number, cancellationToken), list => (IReadOnlyList<RecordDigestResponse>)list);

    private async Task<bool> IsModeratorAsync()
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return false;
        }

        var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, http, Policies.Moderator)).Succeeded;
    }
}
