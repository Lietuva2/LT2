using System.Security.Claims;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.MpMail;

/// <summary>
/// The MP's choices without an account (design 8.19.4, 8.19.7): <c>GET|PUT /api/v1/mp-mail/{token}</c>, <c>POST …/renew</c>,
/// <c>PUT|DELETE …/extra</c>, <c>POST /api/v1/mp-mail/extra/confirm</c> and <c>…/extra/stop</c> (tokens in the body), and the
/// linked account's <c>GET|PUT /api/v1/me/mp/mail</c>. Every one of these writes goes through the API group's same-origin and
/// antiforgery filter; only the one-click stop (<see cref="OneClickStopEndpoint"/>) is mapped outside it. Token routes are
/// limited per client address and per token, answer the same for an unknown and a wrong token, and are never cached.
/// </summary>
internal static class MpMailEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(MpMailRoutes.Token), GetAsync)
            .WithName("GetMpMail").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(MpMailRoutes.Token), SetAsync)
            .WithName("SetMpMail").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .WithValidation<MpMailFrequencyRequest>()
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(MpMailRoutes.Renew), RenewAsync)
            .WithName("RenewMpMail").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(MpMailRoutes.Extra), AskExtraAsync)
            .WithName("AskMpMailExtra").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .WithValidation<MpMailExtraRequest>()
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapDelete(ApiEndpoints.Relative(MpMailRoutes.Extra), RemoveExtraAsync)
            .WithName("RemoveMpMailExtra").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(MpMailRoutes.ConfirmExtra), ConfirmExtraAsync)
            .WithName("ConfirmMpMailExtra").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .WithValidation<MpMailTokenRequest>()
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(MpMailRoutes.StopCopies), StopCopiesAsync)
            .WithName("StopMpMailCopies").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .WithValidation<MpMailTokenRequest>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(MpMailRoutes.Mine), MineAsync)
            .WithName("GetMyMpMail").WithTags("MpMail").RequireAuthorization(Policies.Member)
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(MpMailRoutes.Mine), SetMineAsync)
            .WithName("SetMyMpMail").WithTags("MpMail").RequireAuthorization(Policies.Member)
            .WithValidation<MpMailFrequencyRequest>()
            .Produces<MpMailChoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        // Design 8.19.3 (owner, 2026-11-17): an office member's own copies of the MP's summaries.
        api.MapGet(ApiEndpoints.Relative(MpMailRoutes.OfficeCopies), OfficeCopiesAsync)
            .WithName("GetMyOfficeCopies").WithTags("MpMail").RequireAuthorization(Policies.Member)
            .Produces<MpOfficeCopiesResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(MpMailRoutes.OfficeCopies), SetOfficeCopiesAsync)
            .WithName("SetMyOfficeCopies").WithTags("MpMail").RequireAuthorization(Policies.Member)
            .WithValidation<MpMailFrequencyRequest>()
            .Produces<MpOfficeCopiesResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> OfficeCopiesAsync(ClaimsPrincipal user, MpMailHost host, ErrorMapper errors, CancellationToken ct)
    {
        var result = await host.OfficeCopiesAsync(user, null, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> SetOfficeCopiesAsync(ClaimsPrincipal user, MpMailFrequencyRequest request, MpMailHost host, ErrorMapper errors, CancellationToken ct)
    {
        var result = await host.OfficeCopiesAsync(user, request.Frequency, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> GetAsync(string token, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Choice(await mail.GetAsync(token, ct), errors);

    private static async Task<IResult> SetAsync(string token, MpMailFrequencyRequest request, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Choice(await mail.SetFrequencyAsync(token, request.Frequency, ct), errors);

    private static async Task<IResult> RenewAsync(string token, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Done(await mail.RenewAsync(token, ct), errors);

    private static async Task<IResult> AskExtraAsync(string token, MpMailExtraRequest request, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Choice(await mail.AskExtraAsync(token, request.Address, ct), errors);

    private static async Task<IResult> RemoveExtraAsync(string token, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Choice(await mail.RemoveExtraAsync(token, ct), errors);

    private static async Task<IResult> ConfirmExtraAsync(MpMailTokenRequest request, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Choice(await mail.ConfirmExtraAsync(request.Token, ct), errors);

    private static async Task<IResult> StopCopiesAsync(MpMailTokenRequest request, IMpMail mail, ErrorMapper errors, CancellationToken ct) =>
        Done(await mail.StopCopiesAsync(request.Token, ct), errors);

    private static async Task<IResult> MineAsync(ClaimsPrincipal user, MpMailHost host, ErrorMapper errors, CancellationToken ct)
    {
        var result = await host.MineAsync(user, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> SetMineAsync(ClaimsPrincipal user, MpMailFrequencyRequest request, MpMailHost host, ErrorMapper errors, CancellationToken ct)
    {
        var result = await host.SetMineAsync(user, request.Frequency, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static IResult Choice(Result<MpMailChoice> result, ErrorMapper errors) =>
        result.IsSuccess ? TypedResults.Ok(MpMailHost.Map(result.Value)) : errors.ToResult(result.Error!);

    private static IResult Done(Result<Unit> result, ErrorMapper errors) =>
        result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
}

/// <summary>
/// <c>POST /api/v1/mp-mail/{token}/stop</c>: the one-click stop of RFC 8058, which mail programs send for
/// <c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c> (design 8.19.4). It is the one write mapped outside the API group's
/// same-origin and antiforgery filter, because a mail program has neither; it reads no body and can do nothing but set
/// "Nesiųsti" for the token's MP. Limited per address and per token like the other token routes.
/// </summary>
internal static class OneClickStopEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(MpMailRoutes.Stop, HandleAsync)
            .WithName("OneClickStopMpMail").WithTags("MpMail").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(string token, IMpMail mail, ErrorMapper errors, CancellationToken ct)
    {
        var result = await mail.StopAsync(token, ct);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
