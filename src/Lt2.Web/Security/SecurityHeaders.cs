namespace Lt2.Web.Security;

/// <summary>
/// Response headers of design 6.4. The Content Security Policy allows Blazor WebAssembly
/// (<c>'wasm-unsafe-eval'</c>) and nothing inline: no inline scripts or styles, no third-party origins.
/// </summary>
internal static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=(), " +
        "interest-cohort=(), browsing-topics=()";

    public static IApplicationBuilder UseLt2SecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers["Referrer-Policy"] = "no-referrer";
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                // A server error is never kept by a browser or a proxy: the next try asks again.
                if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    headers.CacheControl = "no-store";
                }

                // API answers are not stored anywhere unless an endpoint has said it is public (the parliament
                // reads of design 7.4 set "public, max-age=60" with an ETag); personal answers never set it.
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(headers.CacheControl))
                {
                    headers.CacheControl = "no-store";
                }

                // Design 6.8: a page answered to a signed-in session can hold the person's own side or invitation code,
                // so no browser or proxy keeps it. A visitor's page is the same for everyone and may be kept.
                if (context.User.Identity?.IsAuthenticated == true
                    && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    headers.CacheControl = "no-store";
                }

                // Design 8.19.4: an MP's link opens a page for that MP only: never indexed, never stored (no referrer is
                // sent from any page, above). A member's stop link the same (design 8.24.1).
                if (IsMpLinkPage(context.Request.Path) || IsStopLinkPage(context.Request.Path))
                {
                    headers["X-Robots-Tag"] = "noindex, nofollow";
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            return next(context);
        });

    /// <summary>The pages an MP's link opens (design 8.19.4): the preferences, the extra recipient's confirmation and a copy's stop.</summary>
    public static bool IsMpLinkPage(PathString path) =>
        path.StartsWithSegments(Lt2.Api.Contracts.Digests.MpMailRoutes.PreferencesPage, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(Lt2.Api.Contracts.Digests.MpMailRoutes.CopiesPage, StringComparison.OrdinalIgnoreCase);

    /// <summary>The page a member's stop link opens (design 8.24.1).</summary>
    public static bool IsStopLinkPage(PathString path) =>
        path.StartsWithSegments(Lt2.Api.Contracts.Follows.NoticeStopRoutes.Page, StringComparison.OrdinalIgnoreCase);
}
