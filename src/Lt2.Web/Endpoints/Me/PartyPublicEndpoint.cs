using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// The party shown on the public profile (design 8.25.9, owner 2026-11-17). <c>PUT /api/v1/me/party-support/public</c> ticks
/// "Rodyti viešame profilyje": the consent <c>party-public</c> against the version the form showed, only with a party and a
/// public profile (<c>party.consent-outdated</c>, <c>party.none</c>, <c>party.profile-off</c>, <c>mp.takes-no-part</c>).
/// <c>DELETE</c> unticks it: withdrawn at once, open behind the terms (design 8.22.2). Audited without the party.
/// </summary>
internal static class PartyPublicEndpoint
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Me.PartyPublic), ShowAsync)
            .WithName("ShowPartyOnProfile")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent);

        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.PartyPublic), HideAsync)
            .WithName("HidePartyOnProfile")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ShowAsync(
        PartyPublicRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.ShowPartyAsync(user, request.ConsentVersion, ipHasher.Hash(context), ct);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> HideAsync(ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.HidePartyAsync(user, ipHasher.Hash(context), ct);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
