using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>DELETE /api/v1/me</c>: deletes the account and everything the person gave (design 6.5): positions and marks go and
/// counts fall; frozen summary snapshots keep their totals. Signs out.
/// </summary>
internal static class DeleteAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.Root), HandleAsync)
            .WithName("DeleteMe")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, Lt2.Web.Services.AnalyticsCopies analytics, CancellationToken cancellationToken)
    {
        // The member's kept analytics comparison goes with their sides and their account (design 8.26.6).
        if (user.UserId() is { } id)
        {
            analytics.Forget(id);
        }

        var result = await identity.DeleteAccountAsync(user, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}