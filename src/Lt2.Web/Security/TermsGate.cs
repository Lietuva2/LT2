using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Security;

/// <summary>
/// Design 8.22.2: a member who has not accepted the terms of use in force reads everything, and every other write of the API is
/// refused with <c>terms.required</c> (403) until they accept. The one place this is decided: it runs after authentication and
/// before authorization, for every request to <c>/api</c> that is not a read (GET, HEAD, OPTIONS) and comes from a signed-in
/// session, so a route added later is covered without anyone remembering it. Only <see cref="TermsExemptions.Routes"/> pass.
/// <para>
/// The server-rendered forms that write as the account do not go through the API: "Atsijungti" needs no terms, and the feedback
/// form asks <see cref="IIdentityModule.TermsRequiredAsync"/> itself (<see cref="ServerFeedbackApi"/>).
/// </para>
/// </summary>
internal sealed class TermsGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IIdentityModule identity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(identity);
        if (Applies(context) && await identity.TermsRequiredAsync(context.User, context.RequestAborted))
        {
            var errors = context.RequestServices.GetRequiredService<ErrorMapper>();
            await errors.ToResult(Error.Forbidden(TermsOfUse.RequiredCode)).ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    /// <summary>A write to the API from a signed-in session, not on <see cref="TermsExemptions.Routes"/>.</summary>
    private static bool Applies(HttpContext context)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || !TermsExemptions.IsWrite(request.Method)
            || context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return !(context.GetEndpoint() is RouteEndpoint endpoint && TermsExemptions.IsExempt(request.Method, endpoint.RoutePattern.RawText));
    }
}
