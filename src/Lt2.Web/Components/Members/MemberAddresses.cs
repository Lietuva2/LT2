using Lt2.SharedKernel;

namespace Lt2.Web.Components.Members;

/// <summary>
/// A member's public page answers at one address only (design 8.25.2, owner 2026-11-16): <c>/nariai/{name-slug}-{id}</c> with
/// the current display name. The id decides; an address with the right id and any other name part (a changed name, a typo,
/// the id alone) is answered with a permanent redirect to the current one, the query kept. A retired id (410) and an unknown
/// one (404) are left to the page, whatever the name part.
/// </summary>
internal static class MemberAddresses
{
    public static IApplicationBuilder UseMemberAddresses(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value;
        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && path is not null
            && path.StartsWith(MemberAddress.Prefix, StringComparison.Ordinal)
            && path[MemberAddress.Prefix.Length..] is { Length: > 0 } segment
            && !segment.Contains('/', StringComparison.Ordinal))
        {
            var profiles = context.RequestServices.GetRequiredService<IPublicProfiles>();
            var (state, profile) = await profiles.FindAsync(MemberAddress.IdOf(segment), context.RequestAborted);
            if (state == ProfileIdState.Public && profile is not null && MemberAddress.Segment(profile.ProfileId, profile.DisplayName) is var current && current != segment)
            {
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Redirect(MemberAddress.Prefix + current + context.Request.QueryString, permanent: true);
                return;
            }
        }

        await next(context);
    });
}
