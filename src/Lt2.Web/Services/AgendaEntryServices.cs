using System.Globalization;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Identity;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// Design 8.4, "Entering a day's agenda": the host's side of <see cref="IAgendaEntries"/>, shared by the endpoints and the
/// server-side <see cref="IAgendaEntryApi"/>. A save that created bills makes the Appeals module's search copy of the bills
/// current at once (design 8.12.5), so a created bill is found as soon as its page exists; a failure there is logged and
/// never fails the save (the next import or search makes the copy again).
/// </summary>
internal sealed class AgendaEntryWrites(IAgendaEntries entries, Lt2.Appeals.IBillCards billCards, ILogger<AgendaEntryWrites> logger)
{
    /// <summary>The day in the address: <c>yyyy-MM-dd</c>, nothing else.</summary>
    public static bool TryDay(string? text, out DateOnly day) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    public async Task<AgendaEntryDaysResponse> DaysAsync(CancellationToken ct) =>
        new((await entries.DaysAsync(ct)).Select(d => new AgendaEntryDayResponse(d.Day, d.Bills, d.LastSaved, d.SourceUrl)).ToList());

    public async Task<Result<AgendaEntryResponse>> PreviewAsync(AgendaEntryPreviewRequest request, CancellationToken ct)
    {
        var result = await entries.PreviewAsync(request.Day, request.SourceUrl, request.Lines, ct);
        return result.IsSuccess ? Map(result.Value) : result.Error!;
    }

    public async Task<Result<AgendaEntryResponse>> SaveAsync(DateOnly day, AgendaEntryRequest request, StaffActor staff, CancellationToken ct)
    {
        var result = await entries.SaveAsync(day, request.SourceUrl, request.Lines, staff, ct);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        if (result.Value.Created > 0)
        {
            try
            {
                await billCards.RefreshAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The search copy of the bills could not be refreshed after an entered agenda created bills");
            }
        }

        return Map(result.Value);
    }

    public static AgendaEntryResponse Map(AgendaEntryOutcome o) =>
        new(
            o.Day,
            o.SourceUrl,
            o.Saved,
            o.Count(AgendaEntryChanges.Added),
            o.Count(AgendaEntryChanges.StageChanged),
            o.Count(AgendaEntryChanges.Unchanged),
            o.Removed.Count,
            o.Created,
            o.Bills.Select(b => new AgendaEntryBillResponse(b.Line, b.Number, b.Stage, b.Title, b.Created, b.Change)).ToList(),
            o.Removed,
            o.Refused.Select(r => new AgendaEntryRefusalResponse(r.Line, r.Reason)).ToList());
}

/// <summary>
/// Server-side <see cref="IAgendaEntryApi"/> (design 8.4), for the entry page prerendered for a moderator: the same policy as
/// the endpoints, applied to the current request, and a save run as that moderator.
/// </summary>
internal sealed class ServerAgendaEntryApi(
    AgendaEntryWrites writes, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IAgendaEntryApi
{
    public async Task<ApiResult<AgendaEntryDaysResponse>> GetDaysAsync(CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator)
            ? ApiResult<AgendaEntryDaysResponse>.Ok(await writes.DaysAsync(cancellationToken))
            : ApiResult<AgendaEntryDaysResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<AgendaEntryResponse>> PreviewAsync(AgendaEntryPreviewRequest request, CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator)
            ? errors.ToApiResult(await writes.PreviewAsync(request, cancellationToken), r => r)
            : ApiResult<AgendaEntryResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<AgendaEntryResponse>> SaveAsync(DateOnly day, AgendaEntryRequest request, CancellationToken cancellationToken = default)
    {
        if (!await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator))
        {
            return ApiResult<AgendaEntryResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        var staff = await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), http.RequestAborted);
        return errors.ToApiResult(await writes.SaveAsync(day, request, staff, cancellationToken), r => r);
    }
}
