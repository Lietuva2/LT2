using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Members;

/// <summary>
/// Public profiles (design 8.25.6): the caller's own <c>GET|PUT /me/profile</c>; the public <c>GET /members/{id}</c> (404 never
/// issued, 410 retired) and <c>GET /members/{id}/texts?shown=</c>; <c>PUT|DELETE /members/{id}/follow</c>;
/// <c>POST /members/{id}/report</c>; the moderators' <c>GET /moderation/profiles</c> and <c>POST …/{id}/hide|keep|restore</c>;
/// and how many follow a bill, <c>GET /bills/{number}/followers</c> (design 8.11.2). No answer says who follows anyone, and
/// no public answer carries an account id.
/// </summary>
internal static class MemberEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ProfileRoutes.Mine), async (ClaimsPrincipal user, IMemberProfiles profiles, ErrorMapper errors, CancellationToken ct) =>
                Ok(await profiles.MineAsync(user, ct), ProfileMapping.Map, errors))
            .WithName("GetMyProfile").WithTags("Me").RequireAuthorization(Policies.Member).Produces<MyProfileResponse>();

        api.MapPut(ApiEndpoints.Relative(ProfileRoutes.Mine), async (ProfileRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ip, IMemberProfiles profiles, ErrorMapper errors, CancellationToken ct) =>
                Ok(await profiles.ChangeAsync(user, ProfileMapping.ToModule(request), ip.Hash(context), ct), ProfileMapping.Map, errors))
            .WithName("SaveMyProfile").WithTags("Me").RequireAuthorization(Policies.Member)
            .WithValidation<ProfileRequest>().RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<MyProfileResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(ProfileRoutes.Member), async (string id, HttpContext http, MemberReads reads, ErrorMapper errors, CancellationToken ct) =>
                NoCache(http, Ok(await reads.MemberAsync(id, ct), x => x, errors)))
            .WithName("GetMember").WithTags("Members").AllowAnonymous()
            .Produces<MemberProfileResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status410Gone);

        api.MapGet(ApiEndpoints.Relative(ProfileRoutes.Texts), async (string id, int? shown, HttpContext http, MemberReads reads, ErrorMapper errors, CancellationToken ct) =>
                NoCache(http, Ok(await reads.TextsAsync(id, shown, ct), x => x, errors)))
            .WithName("GetMemberTexts").WithTags("Members").AllowAnonymous()
            .Produces<MemberTextsResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status410Gone);

        api.MapPut(ApiEndpoints.Relative(ProfileRoutes.Follow), (string id, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, userId => f.FollowMemberAsync(userId, id, ct)))
            .WithName("FollowMember").WithTags("Me").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapDelete(ApiEndpoints.Relative(ProfileRoutes.Follow), (string id, ClaimsPrincipal user, IFollows f, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, userId => f.UnfollowMemberAsync(userId, id, ct)))
            .WithName("UnfollowMember").WithTags("Me").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent);

        api.MapPost(ApiEndpoints.Relative(ProfileRoutes.Report), (string id, MemberReportRequest request, ClaimsPrincipal user, IMemberProfiles profiles, ErrorMapper e, CancellationToken ct) =>
                RunAsync(user, e, userId => profiles.ReportAsync(userId, id, request.Ground, request.Text, ct)))
            .WithName("ReportMember").WithTags("Members").RequireAuthorization(Policies.Member)
            .WithValidation<MemberReportRequest>().RequireRateLimiting(RateLimitPolicies.Report)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(FollowRoutes.BillFollowers), async (string number, IFollowerCounts followers, CancellationToken ct) =>
                TypedResults.Ok(new FollowersResponse(await followers.BillAsync(number, ct))))
            .WithName("GetBillFollowers").WithTags("Appeals").AllowAnonymous().Produces<FollowersResponse>();

        api.MapGet(ApiEndpoints.Relative(ProfileRoutes.Moderation), async (IMemberProfiles profiles, CancellationToken ct) =>
                TypedResults.Ok(ProfileMapping.Map(await profiles.QueueAsync(ct))))
            .WithName("GetProfileModeration").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<IReadOnlyList<ProfileModerationResponse>>();

        Act<StatementRequest>(api, ProfileRoutes.Hide, "HideProfileAbout", (p, id, r, m, c, ct) => p.HideAboutAsync(id, r.Statement, m, c, ct));
        Act<ModerationRequest>(api, ProfileRoutes.Keep, "KeepProfile", (p, id, r, m, _, ct) => p.KeepAsync(id, r.Statement, m, ct));
        Act<StatementRequest>(api, ProfileRoutes.Restore, "RestoreProfileAbout", (p, id, r, m, c, ct) => p.RestoreAboutAsync(id, r.Statement, m, c, ct));
    }

    private delegate Task<Result<Unit>> ProfileAction<in T>(IMemberProfiles profiles, Guid id, T request, StaffActor moderator, string? contact, CancellationToken ct);

    private static void Act<T>(IEndpointRouteBuilder api, string route, string name, ProfileAction<T> action) =>
        api.MapPost(ApiEndpoints.Relative(route), (Guid id, T request, HttpContext context, IMemberProfiles profiles, CancellationToken ct) =>
                ModeratorEndpoint.ActAsync(context, m => action(profiles, id, request, m, ModeratorEndpoint.Contact(context), ct)))
            .WithName(name).WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<T>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static IResult Ok<TIn, TOut>(Result<TIn> result, Func<TIn, TOut> map, ErrorMapper errors) =>
        result.IsSuccess ? TypedResults.Ok(map(result.Value!)) : errors.ToResult(result.Error!);

    /// <summary>A public page's answers are asked again each time: a profile switched off must go at once (design 8.25.1).</summary>
    private static IResult NoCache(HttpContext http, IResult result)
    {
        http.Response.Headers.CacheControl = "no-cache";
        return result;
    }

    private static async Task<IResult> RunAsync(ClaimsPrincipal user, ErrorMapper errors, Func<Guid, Task<Result<Unit>>> action)
    {
        var result = await action(user.UserId()!.Value);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
