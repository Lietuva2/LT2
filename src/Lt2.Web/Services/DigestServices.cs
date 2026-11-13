using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

internal static class DigestMapping
{
    public static DigestRunResponse Map(DigestRunInfo r) => new(
        r.Id, r.Kind, r.RunDate, r.SittingDate, r.CreatedAt, r.Status, r.MpCount,
        r.Items.Select(i => new DigestItemResponse(
            i.BillNumber, i.BillTitle, i.Stage, i.PlannedDate, i.For, i.ForOlderThan30Days, i.Against, i.AgainstOlderThan30Days,
            i.SurgeSharePercent, i.SurgeWindowHours,
            i.Reasons.Select(x => new DigestReasonResponse(x.Side, x.Text, x.Endorsements)).ToList())).ToList());
}

/// <summary>Server-side <see cref="IDigestsApi"/> for the pages; the preview needs an active moderator role.</summary>
internal sealed class ServerDigestsApi(IDigests digests, ISettings settings, IHttpContextAccessor accessor, ErrorMapper errors) : IDigestsApi
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
