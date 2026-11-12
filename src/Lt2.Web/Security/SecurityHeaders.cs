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
                // API answers are not stored anywhere unless an endpoint has said it is public (the parliament
                // reads of design 7.4 set "public, max-age=60" with an ETag); personal answers never set it.
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(headers.CacheControl))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
}
