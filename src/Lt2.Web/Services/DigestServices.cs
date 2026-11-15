using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

internal static class DigestMapping
{
    public static DigestRunResponse Map(DigestRunInfo r) => new(
        r.Id, r.Kind, r.RunDate, r.SittingDate, r.CreatedAt, r.Status, r.MpCount, r.Items.Select(Map).ToList());

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

/// <summary>Server-side <see cref="IDigestsApi"/> for the pages; the preview needs an active moderator role.</summary>
internal sealed class ServerDigestsApi(
    IDigests digests, Lt2.Appeals.IAppealsModule appeals, ISettings settings, IHttpContextAccessor accessor, ErrorMapper errors) : IDigestsApi
{
    public async Task<ApiResult<DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        ApiResult<DigestRulesResponse>.Ok(await RulesAsync(settings, cancellationToken));

    /// <summary>The current values of every rule that decides what MPs would receive.</summary>
    public static async Task<DigestRulesResponse> RulesAsync(ISettings settings, CancellationToken ct)
    {
        var minimum = await settings.GetAsync(SettingKeys.DigestThresholdMinimum, ct);
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
            GroupMinimum: 20,
            OlderThanDays: 30,
            ReasonsPerSide: 2);
    }

    public async Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default) =>
        ApiResult<IReadOnlyList<DigestRunResponse>>.Ok((await digests.ListAsync(cancellationToken)).Select(DigestMapping.Map).ToList());

    public async Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        ApiResult<IReadOnlyList<DigestRunResponse>>.Ok((await digests.OnDateAsync(date, cancellationToken)).Select(DigestMapping.Map).ToList());

    public async Task<ApiResult<IReadOnlyList<RecordDigestResponse>>> GetRecordDigestsAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await DigestMapping.ForRecordAsync(appeals, digests, number, cancellationToken), list => (IReadOnlyList<RecordDigestResponse>)list);

    public async Task<ApiResult<IReadOnlyList<DigestMpResponse>>> GetMpsAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<IReadOnlyList<DigestMpResponse>>.Ok((await digests.MpsAsync(runId, cancellationToken)).Select(m => new DigestMpResponse(m.MpSlug, m.Status)).ToList())
            : ApiResult<IReadOnlyList<DigestMpResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<DigestPreviewResponse>> GetPreviewAsync(Guid runId, string slug, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? errors.ToApiResult(await digests.PreviewAsync(runId, slug, cancellationToken), p => new DigestPreviewResponse(p.Subject, p.Text))
            : ApiResult<DigestPreviewResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

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
