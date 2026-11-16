using System.Security.Claims;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>POST /api/v1/admin/mps/{slug}/link/end</c> (design 8.5.2, "Ending it"): an administrator in a staff-passkey session
/// with a fresh passkey confirmation ends an MP's link (misuse, a wrong link) with a statement; the office ends with it;
/// audited and told to the official address. The answer does not say which account it was.
/// </summary>
internal static class EndMpLinkEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(MpRoutes.EndLink), HandleAsync)
            .WithName("EndMpLink")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        string slug,
        StatementRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IIdentityModule identity,
        IPoliticians politicians,
        IpHasher ipHasher,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        if (await FreshConfirmation.RequireAsync(identity, user, errors, cancellationToken) is { } refused)
        {
            return refused;
        }

        var result = await politicians.EndByAdministratorAsync(
            slug, request.Statement, new StaffActor(user.UserId()!.Value, Roles.Administrator, ipHasher.Hash(context)), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
