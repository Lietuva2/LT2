using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// The two withdrawals of the party (design 5.2, 8.25.9), requests of their own beside the "Profilis" form, which gives and
/// changes both (<c>PUT /me/profile</c>, design 8.28.5). Both are open behind the terms (design 8.22.2, owner 2026-11-17;
/// <c>TermsExemptions</c>) and audited without the party.
/// <c>DELETE /api/v1/me/party-support</c>: the party removed, its consent withdrawn, the party no longer shown.
/// <c>DELETE /api/v1/me/party-support/public</c>: "Rodyti viešame profilyje" unticked, the consent <c>party-public</c> withdrawn
/// at once.
/// </summary>
internal static class PartyWithdrawalEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.PartySupport), WithdrawAsync)
            .WithName("WithdrawPartySupport")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent);

        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.PartyPublic), HideAsync)
            .WithName("HidePartyOnProfile")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> WithdrawAsync(ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.WithdrawPartySupportAsync(user, ipHasher.Hash(context), ct);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> HideAsync(ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.HidePartyAsync(user, ipHasher.Hash(context), ct);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
