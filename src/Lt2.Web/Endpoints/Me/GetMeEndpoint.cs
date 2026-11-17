using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>GET /api/v1/me</c>: the signed-in person's account.</summary>
internal static class GetMeEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.Root), HandleAsync)
            .WithName("GetMe")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces<MeResponse>();

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, Lt2.Notifications.IDigests digests, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.GetAccountAsync(user, cancellationToken);
        if (!result.IsSuccess)
        {
            return errors.ToResult(result.Error!);
        }

        // Design 8.19.2 (owner, 2026-11-17): a staff account (by its role, in any session) is told on every page of a prepared
        // summary nobody approved or declined; js/lt2-account.js shows the box from this answer. Never for anyone else.
        var me = Mapping.ToResponse(result.Value);
        if (result.Value.StaffAccount && await digests.PendingAsync(cancellationToken) is { } pending)
        {
            me = me with { DigestReview = new Lt2.Api.Contracts.Digests.DigestReviewNoticeResponse(pending.DueAt, pending.Bills, pending.Reasons) };
        }

        return TypedResults.Ok(me);
    }
}
