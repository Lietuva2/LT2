using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>PUT /api/v1/me/party-support</c> (design 8.8): sets the party the person supports, with explicit consent, or with
/// none removes it. Private; used only for group numbers of at least 20 in summaries to MPs.
/// </summary>
internal static class SetPartySupportEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Me.PartySupport), HandleAsync)
            .WithName("SetPartySupport")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<PartySupportRequest>()
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        PartySupportRequest request, ClaimsPrincipal user, IIdentityModule identity, IMpDirectory mps, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.SetPartySupportAsync(user, request.Party, request.Consent, await mps.PartiesAsync(cancellationToken), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}