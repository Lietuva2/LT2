using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// The body every moderator's action endpoint shares (not an endpoint itself): the moderator of the request (the role that
/// works in this session, the hashed IP address for the audit log), the action, and its result as 204 or a problem. The
/// endpoint's own <c>RequireAuthorization(Policies.Moderator)</c> has let the request in.
/// </summary>
internal static class ModeratorEndpoint
{
    public static async Task<IResult> ActAsync(HttpContext context, Func<StaffActor, Task<Result<Unit>>> action) =>
        Outcome(context, await action(await ModeratorAsync(context)));

    /// <summary>Restricting a writer (an argument's author or a statement's writer, design 8.3): 201 with the restriction's id.</summary>
    public static async Task<IResult> RestrictAsync(HttpContext context, Func<StaffActor, string?, Task<Result<Guid>>> restrict)
    {
        var result = await restrict(await ModeratorAsync(context), Contact(context));
        return result.IsSuccess
            ? TypedResults.Created(Lt2.Api.Contracts.AppealRoutes.Restrictions, new Lt2.Api.Contracts.Appeals.RestrictionCreatedResponse(result.Value))
            : context.RequestServices.GetRequiredService<ErrorMapper>().ToResult(result.Error!);
    }

    /// <summary>The moderator of the request, for an action whose answer is not a plain 204.</summary>
    public static Task<StaffActor> ModeratorAsync(HttpContext context) =>
        AppealMapping.ModeratorAsync(
            context.RequestServices.GetRequiredService<Lt2.Identity.IIdentityModule>(),
            context.User,
            context.RequestServices.GetRequiredService<IpHasher>().Hash(context),
            context.RequestAborted);

    /// <summary>The moderators' contact address for objecting (configuration <c>Moderation:ContactAddress</c>), if set.</summary>
    public static string? Contact(HttpContext context) => AppealMapping.Contact(context.RequestServices.GetRequiredService<IConfiguration>());

    private static IResult Outcome(HttpContext context, Result<Unit> result) =>
        result.IsSuccess ? TypedResults.NoContent() : context.RequestServices.GetRequiredService<ErrorMapper>().ToResult(result.Error!);
}
