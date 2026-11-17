using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>PUT /api/v1/reasons/{id}</c>: the author edits their argument's text and side while the record is open and nobody has
/// reacted to it (design 8.2, owner 2026-11-17): after a "Man svarbu" or a comment under it, 409 <c>reason.reacted</c>; someone
/// else's, 403. The answer is the status after the edit: <c>pending</c> when pre-screening flagged the new text.
/// </summary>
internal static class EditReasonEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.Reason), HandleAsync)
            .WithName("EditReason")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<ReasonRequest>()
            .RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<ReasonCreatedResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute(Name = "id")] Guid reasonId, ReasonRequest request, ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await appeals.EditReasonAsync(reasonId, user.UserId()!.Value, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(new ReasonCreatedResponse(result.Value.Id, result.Value.Status))
            : errors.ToResult(result.Error!);
    }
}
