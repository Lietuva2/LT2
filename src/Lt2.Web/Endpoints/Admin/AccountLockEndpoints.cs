using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>POST /api/v1/admin/accounts/lock</c> and <c>.../unlock</c> (design 8.3, "A full lock"): an administrator in a staff-passkey
/// session with a fresh passkey confirmation locks an account, which then cannot sign in, or unlocks it, each with a statement
/// that is audited and emailed to the person. Never a moderator.
/// </summary>
internal static class AccountLockEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        foreach (var (route, locked) in new[] { (ApiRoutes.Admin.LockAccount, true), (ApiRoutes.Admin.UnlockAccount, false) })
        {
            api.MapPost(ApiEndpoints.Relative(route), (AccountLockRequest request, ClaimsPrincipal user, HttpContext context, IIdentityModule identity,
                    IIdentityAdministration administration, IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken cancellationToken) =>
                    HandleAsync(locked, request, user, context, identity, administration, ipHasher, errors, configuration, cancellationToken))
                .WithName(locked ? "LockAccount" : "UnlockAccount")
                .WithTags("Admin")
                .RequireAuthorization(Policies.Administrator)
                .WithValidation<AccountLockRequest>()
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> HandleAsync(
        bool locked,
        AccountLockRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IIdentityModule identity,
        IIdentityAdministration administration,
        IpHasher ipHasher,
        ErrorMapper errors,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (await FreshConfirmation.RequireAsync(identity, user, errors, cancellationToken) is { } refused)
        {
            return refused;
        }

        var administrator = new StaffActor(user.UserId()!.Value, Roles.Administrator, ipHasher.Hash(context));
        var contact = AppealMapping.Contact(configuration);
        var result = locked
            ? await administration.LockAccountAsync(request.Email, request.Statement, administrator, contact, cancellationToken)
            : await administration.UnlockAccountAsync(request.Email, request.Statement, administrator, contact, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
