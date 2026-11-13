using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>POST /api/v1/moderation/appeals/{id}/return</c>: return a new appeal to its initiator with a note (design 8.3). Audited; the author is told why by email.</summary>
internal static class ReturnAppealEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.ReturnAppeal), HandleAsync)
            .WithName("ReturnAppeal")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id,
        StatementRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation,
        IpHasher ipHasher,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await moderation.ReturnAppealAsync(id, request.Statement, moderator, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
