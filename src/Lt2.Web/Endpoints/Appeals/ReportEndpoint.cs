using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>POST /api/v1/reports</c>: report an appeal or an argument (design 8.3, Digital Services Act notice and action).</summary>
internal static class ReportEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.Reports), HandleAsync)
            .WithName("Report")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<ReportRequest>()
            .RequireRateLimiting(RateLimitPolicies.Report)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        ReportRequest request,
        ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = AppealMapping.UserId(user)!.Value;
        var result = await appeals.ReportAsync(id, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
