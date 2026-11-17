using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Digests;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Admin;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// The host's part of MP mail (design 8.19.4, 8.19.5): the linked account's choice goes through Identity's link, read from the
/// database per request (the MP's account or an office member, a passkey session); the administrator's test copy goes to the
/// administrator's own address, which only Identity knows.
/// </summary>
internal sealed class MpMailHost(IMpMail mail, IPoliticians politicians, IIdentityModule identity, IMpMailAdministration admin)
{
    public static readonly Error NotLinked = Error.NotFound("mp.not-linked");

    public static MpMailChoiceResponse Map(MpMailChoice c, bool withExtra = true) =>
        new(c.Slug, c.FirstName, c.LastName, c.Frequency, withExtra ? c.ExtraAddress : null, withExtra && c.ExtraConfirmed);

    /// <summary>The linked account's MP's choice; in a passkey session only, as for speaking for the MP (design 8.19.4).</summary>
    public async Task<Result<MpMailChoiceResponse>> MineAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var speaker = await SpeakerAsync(user, ct);
        if (!speaker.IsSuccess)
        {
            return speaker.Error!;
        }

        var choice = await mail.ForPersonAsync(speaker.Value.SeimasPersonId, ct);
        return choice.IsSuccess ? Map(choice.Value, withExtra: false) : choice.Error!;
    }

    public async Task<Result<MpMailChoiceResponse>> SetMineAsync(ClaimsPrincipal user, string frequency, CancellationToken ct)
    {
        var speaker = await SpeakerAsync(user, ct);
        if (!speaker.IsSuccess)
        {
            return speaker.Error!;
        }

        var choice = await mail.SetForPersonAsync(speaker.Value.SeimasPersonId, frequency, ct);
        return choice.IsSuccess ? Map(choice.Value, withExtra: false) : choice.Error!;
    }

    /// <summary>
    /// An office member's own choice of copies (design 8.19.3, owner 2026-11-17): only for an account in an MP's office, in any
    /// session (the member's own mail; it does not speak for the MP). <c>mp.not-office</c> for anyone else, the MP's own account
    /// included (it gets the summary itself).
    /// </summary>
    public async Task<Result<MpOfficeCopiesResponse>> OfficeCopiesAsync(ClaimsPrincipal user, string? frequency, CancellationToken ct)
    {
        if (await politicians.SpeakerAsync(user, ct) is not { Part: "office" } member)
        {
            return NotOffice;
        }

        var choice = frequency is null
            ? await mail.OfficeCopiesAsync(member.UserId, member.SeimasPersonId, ct)
            : await mail.SetOfficeCopiesAsync(member.UserId, member.SeimasPersonId, frequency, ct);
        return choice.IsSuccess ? new MpOfficeCopiesResponse(choice.Value.Slug, choice.Value.FirstName, choice.Value.LastName, choice.Value.Frequency) : choice.Error!;
    }

    public static readonly Error NotOffice = Error.NotFound("mp.not-office");

    /// <summary>The test copy goes to the signed-in administrator's own address (design 8.19.5).</summary>
    public async Task<Result<Unit>> TestAsync(ClaimsPrincipal user, StaffActor administrator, CancellationToken ct)
    {
        var account = await identity.GetAccountAsync(user, ct);
        return account.IsSuccess ? await admin.SendIntroductionTestAsync(account.Value.Email, administrator, ct) : account.Error!;
    }

    private async Task<Result<MpSpeaker>> SpeakerAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await politicians.SpeakerAsync(user, ct) is not { } speaker ? NotLinked
        : !speaker.MayWrite ? Error.Forbidden(PoliticianAccessCodes.PasskeyRequired)
        : speaker;
}

/// <summary>Server-side <see cref="IMpMailApi"/>: the token pages (server-rendered) and the linked account's island.</summary>
internal sealed class ServerMpMailApi(IMpMail mail, MpMailHost host, IHttpContextAccessor accessor, ErrorMapper errors) : IMpMailApi
{
    public async Task<ApiResult<MpMailChoiceResponse>> GetAsync(string token, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.GetAsync(token, cancellationToken), c => MpMailHost.Map(c));

    public async Task<ApiResult<MpMailChoiceResponse>> SetFrequencyAsync(string token, MpMailFrequencyRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.SetFrequencyAsync(token, request?.Frequency ?? "", cancellationToken), c => MpMailHost.Map(c));

    public async Task<ApiResult> RenewAsync(string token, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.RenewAsync(token, cancellationToken));

    public async Task<ApiResult<MpMailChoiceResponse>> AskExtraAsync(string token, MpMailExtraRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.AskExtraAsync(token, request?.Address ?? "", cancellationToken), c => MpMailHost.Map(c));

    public async Task<ApiResult<MpMailChoiceResponse>> RemoveExtraAsync(string token, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.RemoveExtraAsync(token, cancellationToken), c => MpMailHost.Map(c));

    public async Task<ApiResult<MpMailChoiceResponse>> ConfirmExtraAsync(MpMailTokenRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.ConfirmExtraAsync(request?.Token ?? "", cancellationToken), c => MpMailHost.Map(c));

    public async Task<ApiResult> StopCopiesAsync(MpMailTokenRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await mail.StopCopiesAsync(request?.Token ?? "", cancellationToken));

    public async Task<ApiResult<MpMailChoiceResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
        accessor.HttpContext?.User is { } user
            ? errors.ToApiResult(await host.MineAsync(user, cancellationToken), c => c)
            : ApiResult<MpMailChoiceResponse>.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));

    public async Task<ApiResult<MpMailChoiceResponse>> SetMineAsync(MpMailFrequencyRequest request, CancellationToken cancellationToken = default) =>
        accessor.HttpContext?.User is { } user
            ? errors.ToApiResult(await host.SetMineAsync(user, request?.Frequency ?? "", cancellationToken), c => c)
            : ApiResult<MpMailChoiceResponse>.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));

    public async Task<ApiResult<MpOfficeCopiesResponse>> GetOfficeCopiesAsync(CancellationToken cancellationToken = default) =>
        accessor.HttpContext?.User is { } user
            ? errors.ToApiResult(await host.OfficeCopiesAsync(user, null, cancellationToken), c => c)
            : ApiResult<MpOfficeCopiesResponse>.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));

    public async Task<ApiResult<MpOfficeCopiesResponse>> SetOfficeCopiesAsync(MpMailFrequencyRequest request, CancellationToken cancellationToken = default) =>
        accessor.HttpContext?.User is { } user
            ? errors.ToApiResult(await host.OfficeCopiesAsync(user, request?.Frequency ?? "", cancellationToken), c => c)
            : ApiResult<MpOfficeCopiesResponse>.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));
}

/// <summary>Server-side <see cref="IMpSendingAdminApi"/> for the administrators' screens' first render.</summary>
internal sealed class ServerMpSendingAdminApi(
    IMpMailAdministration admin, MpMailHost host, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IMpSendingAdminApi
{
    public async Task<ApiResult<IntroductionResponse>> GetIntroductionAsync(CancellationToken cancellationToken = default) =>
        await AdministratorAsync() is null
            ? Refused<IntroductionResponse>()
            : ApiResult<IntroductionResponse>.Ok(MpSendingMapping.Map(await admin.IntroductionAsync(cancellationToken)));

    public async Task<ApiResult<IntroductionVersionResponse>> SaveIntroductionAsync(IntroductionRequest request, CancellationToken cancellationToken = default) =>
        await AdministratorAsync() is { } actor
            ? errors.ToApiResult(await admin.SaveIntroductionAsync(MpSendingMapping.Draft(request), actor, cancellationToken), DigestMapping.Map)
            : Refused<IntroductionVersionResponse>();

    public async Task<ApiResult> SendIntroductionTestAsync(CancellationToken cancellationToken = default) =>
        await AdministratorAsync() is { } actor
            ? errors.ToApiResult(await host.TestAsync(accessor.HttpContext!.User, actor, cancellationToken))
            : ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<IntroductionSentResponse>> SendIntroductionAsync(CancellationToken cancellationToken = default)
    {
        if (await AdministratorAsync() is not { } actor)
        {
            return Refused<IntroductionSentResponse>();
        }

        if (await FreshConfirmation.MissingAsync(identity, accessor.HttpContext!.User, cancellationToken) is { } missing)
        {
            return ApiResult<IntroductionSentResponse>.Fail(errors.ToProblem(missing));
        }

        return errors.ToApiResult(await admin.SendIntroductionAsync(actor, cancellationToken), n => new IntroductionSentResponse(n));
    }

    public async Task<ApiResult<SuppressionResponse>> GetSuppressionAsync(CancellationToken cancellationToken = default) =>
        await AdministratorAsync() is null
            ? Refused<SuppressionResponse>()
            : ApiResult<SuppressionResponse>.Ok(MpSendingMapping.Map(await admin.SuppressionAsync(cancellationToken)));

    public async Task<ApiResult> SuppressAsync(SuppressRequest request, CancellationToken cancellationToken = default) =>
        await AdministratorAsync() is { } actor
            ? errors.ToApiResult(await admin.SuppressAsync(request?.Address ?? "", request?.Statement ?? "", actor, cancellationToken))
            : ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult> UnsuppressAsync(UnsuppressRequest request, CancellationToken cancellationToken = default) =>
        await AdministratorAsync() is { } actor
            ? errors.ToApiResult(await admin.UnsuppressAsync(request?.EntryId, request?.Address, request?.Statement ?? "", actor, cancellationToken))
            : ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    private ApiResult<T> Refused<T>() => ApiResult<T>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    private async Task<StaffActor?> AdministratorAsync()
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return null;
        }

        var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, http, Policies.Administrator)).Succeeded
            ? new StaffActor(http.User.UserId()!.Value, Roles.Administrator, ipHasher.Hash(http))
            : null;
    }
}

internal static class MpSendingMapping
{
    public static IntroductionResponse Map(IntroductionState s) =>
        new(s.Current is { } c ? DigestMapping.Map(c) : null, s.Sent.Select(DigestMapping.Map).ToList(), s.WouldGoTo, s.MailMayLeave);

    public static SuppressionResponse Map(SuppressionView v) =>
        new(v.Mps.Select(m => new SuppressedMpResponse(m.EntryId, m.Slug, m.FirstName, m.LastName, m.Reason, m.Since)).ToList(), v.Others);

    public static IntroductionDraft Draft(IntroductionRequest? r) =>
        new(r?.Subject ?? "", r?.Text ?? "", r?.SignerName ?? "", r?.SignerRole ?? "");
}
