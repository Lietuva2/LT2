using System.Security.Claims;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Mps;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Mps;

/// <summary>
/// MPs' responses (design 8.5.5 to 8.5.7, 8.8): <c>GET /api/v1/bills/{number}/responses</c> and <c>GET /api/v1/mps/{slug}/responses</c>
/// (public); <c>GET|PUT|DELETE /api/v1/bills/{number}/responses/mine</c> (a linked account; writing in a passkey session, the
/// mandate not ended); <c>GET /api/v1/me/mp/bills</c> (8.5.7); and the moderators' <c>GET /api/v1/moderation/responses</c>,
/// <c>POST …/{id}/keep|hide|restore</c>. Who may speak for an MP is read from the database on every request.
/// </summary>
internal static class MpResponseEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(MpResponseRoutes.OnBill), async (HttpContext http, string number, MpResponseReads reads, ErrorMapper errors, CancellationToken ct) =>
                PublicJson.From(http, errors, await reads.OnBillAsync(number, ct), maxAgeSeconds: 0))
            .WithName("GetBillResponses").WithTags("Mps").AllowAnonymous()
            .Produces<BillMpResponsesResponse>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(MpResponseRoutes.OfMp), async (HttpContext http, string slug, MpResponseReads reads, ErrorMapper errors, CancellationToken ct) =>
                PublicJson.From(http, errors, await reads.OfMpAsync(slug, ct), maxAgeSeconds: 0))
            .WithName("GetMpResponses").WithTags("Mps").AllowAnonymous()
            .Produces<MpAnswersResponse>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(MpResponseRoutes.Mine), async (string number, ClaimsPrincipal user, MpResponseReads reads, ErrorMapper errors, CancellationToken ct) =>
                Ok(await reads.MineAsync(user, number, ct), errors))
            .WithName("GetMyMpResponse").WithTags("Mps").RequireAuthorization(Policies.Member)
            .Produces<MyMpResponseResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(MpResponseRoutes.Mine), async (string number, MpResponseRequest request, ClaimsPrincipal user, MpResponseReads reads, ErrorMapper errors, CancellationToken ct) =>
                Ok(await reads.SaveAsync(user, number, request, ct), errors))
            .WithName("SaveMpResponse").WithTags("Mps").RequireAuthorization(Policies.Member)
            .WithValidation<MpResponseRequest>().RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<MpResponseSavedResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(MpResponseRoutes.Mine), async (string number, ClaimsPrincipal user, MpResponseReads reads, ErrorMapper errors, CancellationToken ct) =>
                Done(await reads.WithdrawAsync(user, number, ct), errors))
            .WithName("WithdrawMpResponse").WithTags("Mps").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(MpResponseRoutes.MyBills), async (ClaimsPrincipal user, MpResponseReads reads, ErrorMapper errors, CancellationToken ct) =>
                Ok(await reads.MyBillsAsync(user, ct), errors))
            .WithName("GetMyMpBills").WithTags("Mps").RequireAuthorization(Policies.Member)
            .Produces<MpBillsResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(MpResponseRoutes.Moderation), async (MpResponseReads reads, CancellationToken ct) => TypedResults.Ok(await reads.ModerationAsync(ct)))
            .WithName("GetResponseModeration").WithTags("Moderation").RequireAuthorization(Policies.Moderator).Produces<MpResponseModerationResponse>();

        Act<ModerationRequest, FluentValidation.IValidator<ModerationRequest>>(api, MpResponseRoutes.Keep, "KeepMpResponse", (m, id, r, a, _, ct) => m.KeepAsync(id, r.Statement, a, ct));

        // Design 8.3 (owner, 2026-11-22): a hidden response's statement is shown in its place to everyone (8.5.5), so its refusal
        // names nobody; restoring emails the statement to the MP alone, so its refusal names the author.
        Act<StatementRequest, FluentValidation.IValidator<StatementRequest>>(api, MpResponseRoutes.Hide, "HideMpResponse", (m, id, r, a, c, ct) => m.HideAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest, StatementToAuthorRequestValidator>(api, MpResponseRoutes.Restore, "RestoreMpResponse", (m, id, r, a, c, ct) => m.RestoreAsync(id, r.Statement, a, c, ct));
    }

    private delegate Task<Result<Unit>> Action<in T>(IMpResponseModeration moderation, Guid id, T request, StaffActor moderator, string? contact, CancellationToken ct);

    private static void Act<T, TValidator>(IEndpointRouteBuilder api, string route, string name, Action<T> action) where TValidator : FluentValidation.IValidator<T> =>
        api.MapPost(ApiEndpoints.Relative(route), (Guid id, T request, HttpContext context, IMpResponseModeration moderation, CancellationToken ct) =>
                ModeratorEndpoint.ActAsync(context, m => action(moderation, id, request, m, ModeratorEndpoint.Contact(context), ct)))
            .WithName(name).WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<T, TValidator>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static IResult Ok<T>(Result<T> result, ErrorMapper errors) => result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);

    private static IResult Done(Result<Unit> result, ErrorMapper errors) => result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
}
