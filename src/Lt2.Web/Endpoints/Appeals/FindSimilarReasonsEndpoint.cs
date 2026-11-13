using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>POST /api/v1/bills/{number}/appeal/reasons/similar</c>: published arguments on the same side that look like the one
/// being written, so the existing one can be offered first (design 8.1). Changes nothing.
/// </summary>
internal static class FindSimilarReasonsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.SimilarReasons), HandleAsync)
            .WithName("FindSimilarReasons")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<ReasonRequest>()
            .Produces<IReadOnlyList<ReasonResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(string number, ReasonRequest request, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await appeals.FindSimilarAsync(number, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value.Select(AppealMapping.Map).ToList()) : errors.ToResult(result.Error!);
    }
}
