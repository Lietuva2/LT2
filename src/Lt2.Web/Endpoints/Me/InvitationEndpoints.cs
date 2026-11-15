using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>GET /api/v1/me/invitation</c> (the caller's own code, made when first needed, with its link and count) and
/// <c>POST /api/v1/me/invitation/replace</c> (a new code; the old one stops working; the count carries over). Design 6.2.
/// The answer is the caller's own and never cached; it holds no names, dates or list.
/// </summary>
internal static class InvitationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.Invitation), async (ClaimsPrincipal user, IInvitations invitations, IConfiguration configuration, ErrorMapper errors, CancellationToken cancellationToken) =>
            {
                var result = await invitations.MineAsync(user.UserId()!.Value, cancellationToken);
                return result.IsSuccess ? Results.Ok(InvitationMapping.Map(result.Value!, configuration)) : errors.ToResult(result.Error!);
            })
            .WithName("GetMyInvitation")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces<MyInvitationResponse>();

        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.ReplaceInvitation), async (ClaimsPrincipal user, IInvitations invitations, IConfiguration configuration, ErrorMapper errors, CancellationToken cancellationToken) =>
            {
                var result = await invitations.ReplaceAsync(user.UserId()!.Value, cancellationToken);
                return result.IsSuccess ? Results.Ok(InvitationMapping.Map(result.Value!, configuration)) : errors.ToResult(result.Error!);
            })
            .WithName("ReplaceMyInvitation")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces<MyInvitationResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}