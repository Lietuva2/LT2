using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>PUT /api/v1/me/consents/positions</c>: the explicit consent before a first position, to the text version in force (design 6.5).</summary>
internal static class GivePositionConsentEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Me.PositionConsent), HandleAsync)
            .WithName("GivePositionConsent")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<PositionConsentRequest>()
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        PositionConsentRequest request,
        ClaimsPrincipal user,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.GivePositionConsentAsync(user, request.TextVersion, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
