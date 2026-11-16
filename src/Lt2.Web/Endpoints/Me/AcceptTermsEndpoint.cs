using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>PUT /api/v1/me/consents/terms</c>: a member accepts the terms of use in force (design 8.22.2), to the version the page
/// showed; <c>terms.outdated</c> for another. One of the writes <see cref="TermsGate"/> lets through before acceptance.
/// </summary>
internal static class AcceptTermsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Me.Terms), HandleAsync)
            .WithName("AcceptTerms")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<TermsAcceptanceRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        TermsAcceptanceRequest request,
        ClaimsPrincipal user,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.AcceptTermsAsync(user, request.TextVersion, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
