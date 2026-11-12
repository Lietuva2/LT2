using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Lt2.Api.Contracts;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Security;

/// <summary>Authorization policy names (design 6.3).</summary>
internal static class Policies
{
    public const string Member = "member";

    /// <summary>Moderator or administrator, signed in with a passkey as the second factor (design 6.2).</summary>
    public const string Moderator = "moderator";

    /// <summary>Administrator, signed in with a passkey as the second factor (design 6.2).</summary>
    public const string Administrator = "administrator";
}

/// <summary>Rate-limit policy names and limits (design 6.4). Limits are per client IP; see <see cref="RateLimitOptions"/>.</summary>
internal static class RateLimitPolicies
{
    public const string CodeRequest = "auth-code";
    public const string Registration = "auth-register";
    public const string SignIn = "auth-sign-in";
}

internal sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public FixedWindow CodeRequest { get; set; } = new() { PermitLimit = 10, WindowSeconds = 15 * 60 };

    public FixedWindow Registration { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    public FixedWindow SignIn { get; set; } = new() { PermitLimit = 30, WindowSeconds = 15 * 60 };

    internal sealed class FixedWindow
    {
        public int PermitLimit { get; set; }

        public int WindowSeconds { get; set; }
    }
}

internal static class SecurityServices
{
    public static IServiceCollection AddLt2Security(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddForwardedHeadersFromConfiguration(configuration);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = ApiHeaders.Antiforgery;
            options.Cookie.Name = "__Host-lt2.af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.SuppressXFrameOptionsHeader = true;
        });
        services.AddScoped<AntiforgeryAndOriginFilter>();

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365 * 2);
            options.IncludeSubDomains = true;
            options.Preload = true;
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Member, p => p.RequireAuthenticatedUser().RequireRole(Roles.Member))
            .AddPolicy(Policies.Moderator, p => p.RequireAuthenticatedUser()
                .RequireRole(Roles.Moderator, Roles.Administrator)
                .RequireClaim(AuthenticationMethods.ClaimType, AuthenticationMethods.Passkey))
            .AddPolicy(Policies.Administrator, p => p.RequireAuthenticatedUser()
                .RequireRole(Roles.Administrator)
                .RequireClaim(AuthenticationMethods.ClaimType, AuthenticationMethods.Passkey));

        var limits = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var l = http.RequestServices.GetRequiredService<IStringLocalizer<Services.ProblemText>>();
                await TypedResults.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: l["Title.429"],
                        detail: l["rate-limited"],
                        extensions: new Dictionary<string, object?> { ["code"] = "rate-limited" })
                    .ExecuteAsync(http);
            };
            options.AddPolicy(RateLimitPolicies.CodeRequest, context => PerIp(context, limits.CodeRequest));
            options.AddPolicy(RateLimitPolicies.Registration, context => PerIp(context, limits.Registration));
            options.AddPolicy(RateLimitPolicies.SignIn, context => PerIp(context, limits.SignIn));
        });

        _ = environment;
        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext context, RateLimitOptions.FixedWindow limit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            });

    internal static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();
}
