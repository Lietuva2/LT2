using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>PUT /api/v1/bills/{number}/appeal</c>: the initiator corrects a pending or returned appeal and sends it to the moderators again.</summary>
internal static class EditAppealEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.Appeal), HandleAsync)
            .WithName("EditAppeal")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<EditAppealRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string number, EditAppealRequest request,
        ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = AppealMapping.UserId(user)!.Value;
        var result = await appeals.EditAsync(number, id, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
