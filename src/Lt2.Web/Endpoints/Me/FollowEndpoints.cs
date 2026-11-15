using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// Following (design 8.11.2), a member's own: <c>GET /me/follows</c> (followed bills, MPs and organisations; never cached, as every <c>/api</c> answer that does not say otherwise),
/// <c>PUT/DELETE /bills/{number}/follow</c>, <c>PUT/DELETE /mps/{slug}/follow</c>, <c>PUT/DELETE /organisations/{slug}/follow</c>,
/// <c>GET/PUT /me/notices</c>. No endpoint answers who follows anything.
/// </summary>
internal static class FollowEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(FollowRoutes.Mine), MineAsync)
            .WithName("GetMyFollows").WithTags("Me").RequireAuthorization(Policies.Member).Produces<MyFollowsResponse>();
        api.MapPut(ApiEndpoints.Relative(FollowRoutes.Bill), (string number, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.FollowBillAsync(id, number, ct)))
            .WithName("FollowBill").WithTags("Me").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapDelete(ApiEndpoints.Relative(FollowRoutes.Bill), (string number, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.UnfollowBillAsync(id, number, ct)))
            .WithName("UnfollowBill").WithTags("Me").RequireAuthorization(Policies.Member).Produces(StatusCodes.Status204NoContent);
        api.MapPut(ApiEndpoints.Relative(FollowRoutes.Mp), (string slug, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.FollowMpAsync(id, slug, ct)))
            .WithName("FollowMp").WithTags("Me").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapDelete(ApiEndpoints.Relative(FollowRoutes.Mp), (string slug, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.UnfollowMpAsync(id, slug, ct)))
            .WithName("UnfollowMp").WithTags("Me").RequireAuthorization(Policies.Member).Produces(StatusCodes.Status204NoContent);
        api.MapPut(ApiEndpoints.Relative(FollowRoutes.Organisation), (string slug, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.FollowOrganisationAsync(id, slug, ct)))
            .WithName("FollowOrganisation").WithTags("Me").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapDelete(ApiEndpoints.Relative(FollowRoutes.Organisation), (string slug, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.UnfollowOrganisationAsync(id, slug, ct)))
            .WithName("UnfollowOrganisation").WithTags("Me").RequireAuthorization(Policies.Member).Produces(StatusCodes.Status204NoContent);
        api.MapGet(ApiEndpoints.Relative(FollowRoutes.Notices), NoticesAsync)
            .WithName("GetNotices").WithTags("Me").RequireAuthorization(Policies.Member).Produces<NoticeSettingsResponse>();
        api.MapPut(ApiEndpoints.Relative(FollowRoutes.Notices), (NoticeSettingsRequest request, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, id => f.SetNoticesAsync(id, new NoticeSettings(request.Planned, request.Voted, request.Statements), ct)))
            .WithName("SetNotices").WithTags("Me").RequireAuthorization(Policies.Member).Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> MineAsync(ClaimsPrincipal user, IFollows follows, CancellationToken ct) =>
        TypedResults.Ok(await FollowMapping.MineAsync(follows, user.UserId()!.Value, ct));

    private static async Task<IResult> NoticesAsync(ClaimsPrincipal user, IFollows follows, CancellationToken ct)
    {
        var n = await follows.NoticesAsync(user.UserId()!.Value, ct);
        return TypedResults.Ok(new NoticeSettingsResponse(n.Planned, n.Voted, n.Statements));
    }

    private static async Task<IResult> RunAsync(ClaimsPrincipal user, ErrorMapper errors, Func<Guid, Task<Result<Unit>>> action)
    {
        var result = await action(user.UserId()!.Value);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
