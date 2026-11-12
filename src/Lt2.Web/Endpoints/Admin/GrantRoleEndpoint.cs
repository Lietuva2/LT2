using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>POST /api/v1/admin/roles</c>: grants a staff role and returns its one-time activation code, once (design 6.2, 8.8).
/// Needs an active administrator role and a fresh passkey confirmation.
/// </summary>
internal static class GrantRoleEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Admin.Roles), HandleAsync)
            .WithName("GrantRole")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .WithValidation<GrantRoleRequest>()
            .Produces<GrantRoleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        GrantRoleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IIdentityModule identity,
        IIdentityAdministration administration,
        IpHasher ipHasher,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        if (await FreshConfirmation.RequireAsync(identity, user, errors, cancellationToken) is { } refused)
        {
            return refused;
        }

        var actor = new Actor(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), Roles.Administrator, ipHasher.Hash(context));
        var result = await administration.GrantRoleAsync(request.Email, request.Role, actor, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(new GrantRoleResponse { ActivationCode = result.Value.ActivationCode, ExpiresAt = result.Value.ExpiresAt })
            : errors.ToResult(result.Error!);
    }
}
