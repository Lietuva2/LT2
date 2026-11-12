using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>PUT /api/v1/me/registration</c>: once the address is proven, the display name (optional) and the declaration of
/// being 18 or older complete the registration; the invitation use is counted and the account becomes a member
/// (design 6.2).
/// </summary>
internal static class CompleteRegistrationEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Me.Registration), HandleAsync)
            .WithName("CompleteRegistration")
            .WithTags("Me")
            .RequireAuthorization()
            .WithValidation<CompleteRegistrationRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        CompleteRegistrationRequest request,
        ClaimsPrincipal user,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.CompleteRegistrationAsync(user, Mapping.ToCompletion(request), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
