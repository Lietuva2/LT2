using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>POST /api/v1/admin/accounts/reset</c>: removes the account's passkeys; its staff roles stay inactive until a new
/// activation (design 6.2). Needs an active administrator role and a fresh passkey confirmation.
/// </summary>
internal static class ResetAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Admin.ResetAccount), HandleAsync)
            .WithName("ResetAccount")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .WithValidation<ResetAccountRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        ResetAccountRequest request,
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

        var actor = new Actor(user.UserId()!.Value, Roles.Administrator, ipHasher.Hash(context));
        var result = await administration.ResetAccountAsync(request.Email, actor, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
