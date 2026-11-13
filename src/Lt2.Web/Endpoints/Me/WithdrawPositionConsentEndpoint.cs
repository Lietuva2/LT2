using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>DELETE /api/v1/me/consents/positions</c>: withdraw the consent; every position of the person is deleted in the same transaction (design 6.5).</summary>
internal static class WithdrawPositionConsentEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.PositionConsent), HandleAsync)
            .WithName("WithdrawPositionConsent")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.WithdrawPositionConsentAsync(user, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
