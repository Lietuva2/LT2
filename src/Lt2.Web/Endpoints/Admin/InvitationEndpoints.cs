using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>GET /api/v1/admin/invitations?days=&amp;code=</c> (numbers per day, per code and per level; never who invited whom)
/// and <c>POST /api/v1/admin/invitations/{id}/switch-off|switch-on</c> (with a statement; audited). An administrator
/// in a staff-passkey session (design 6.2).
/// </summary>
internal static class InvitationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Admin.Invitations), async (int? days, string? code, IInvitations invitations, Lt2.Appeals.IOrganisations organisations, CancellationToken cancellationToken) =>
                TypedResults.Ok(await InvitationMapping.MapAsync(await invitations.StatsAsync(days ?? 60, code, cancellationToken), organisations, cancellationToken)))
            .WithName("GetInvitationStats")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .Produces<InvitationStatsResponse>();

        foreach (var (route, on) in new[] { (ApiRoutes.Admin.SwitchOffInvitation, false), (ApiRoutes.Admin.SwitchOnInvitation, true) })
        {
            api.MapPost(ApiEndpoints.Relative(route), async (Guid id, StatementRequest request, ClaimsPrincipal user, HttpContext context, IInvitations invitations, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken) =>
                {
                    var result = await invitations.SwitchAsync(id, on, request.Statement, AppealMapping.UserId(user)!.Value, Roles.Administrator, ipHasher.Hash(context), cancellationToken);
                    return result.IsSuccess ? Results.NoContent() : errors.ToResult(result.Error!);
                })
                .WithName(on ? "SwitchOnInvitation" : "SwitchOffInvitation")
                .WithTags("Admin")
                .RequireAuthorization(Policies.Administrator)
                .WithValidation<StatementRequest>()
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
