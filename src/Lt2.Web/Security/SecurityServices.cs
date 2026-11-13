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

    /// <summary>An active moderator or administrator role: a session signed in with the staff passkey (design 6.2).</summary>
    public const string Moderator = "moderator";

    /// <summary>An active administrator role: a session signed in with the staff passkey (design 6.2).</summary>
    public const string Administrator = "administrator";

    /// <summary>
    /// The active moderator role itself, not an administrator's: restricting an account from writing is a moderator's
    /// action (design 8.3); lifting it is open to both (<see cref="Moderator"/>).
    /// </summary>
    public const string ModeratorRole = "moderator-role";
}

/// <summary>Rate-limit policy names and limits (design 6.4). Limits are per client IP; see <see cref="RateLimitOptions"/>.</summary>
internal static class RateLimitPolicies
{
    public const string CodeRequest = "auth-code";
    public const string Registration = "auth-register";
    public const string SignIn = "auth-sign-in";

    /// <summary>Opening appeals, per account (design 6.4).</summary>
    public const string OpenAppeal = "appeal-open";

    /// <summary>Writing arguments, per account (design 6.4).</summary>
    public const string WriteReason = "appeal-reason";

    /// <summary>Reports, per account (design 6.4).</summary>
    public const string Report = "appeal-report";
}

internal sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public FixedWindow CodeRequest { get; set; } = new() { PermitLimit = 10, WindowSeconds = 15 * 60 };

    public FixedWindow Registration { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    public FixedWindow SignIn { get; set; } = new() { PermitLimit = 30, WindowSeconds = 15 * 60 };

    public FixedWindow OpenAppeal { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 * 60 };

    public FixedWindow WriteReason { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    public FixedWindow Report { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    internal sealed class FixedWindow
    {
        public int PermitLimit { get; set; }

        public int WindowSeconds { get; set; }
    }
}

internal static class SecurityServices
{
    public static IServiceCollection AddLt2Security(this IServiceCollection services, IConfiguration configuration)
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

        services.AddScoped<IAuthorizationHandler, StaffRoleHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Member, p => p.RequireAuthenticatedUser().RequireRole(Roles.Member))
            .AddPolicy(Policies.Moderator, p => p.RequireAuthenticatedUser()
                .AddRequirements(new StaffRoleRequirement(Roles.Moderator, Roles.Administrator)))
            .AddPolicy(Policies.Administrator, p => p.RequireAuthenticatedUser()
                .AddRequirements(new StaffRoleRequirement(Roles.Administrator)))
            .AddPolicy(Policies.ModeratorRole, p => p.RequireAuthenticatedUser()
                .AddRequirements(new StaffRoleRequirement(Roles.Moderator)));

        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.Section));
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
            options.AddPolicy(RateLimitPolicies.CodeRequest, context => PerIp(context, Limits(context).CodeRequest));
            options.AddPolicy(RateLimitPolicies.Registration, context => PerIp(context, Limits(context).Registration));
            options.AddPolicy(RateLimitPolicies.SignIn, context => PerIp(context, Limits(context).SignIn));
            options.AddPolicy(RateLimitPolicies.OpenAppeal, context => PerAccount(context, Limits(context).OpenAppeal));
            options.AddPolicy(RateLimitPolicies.WriteReason, context => PerAccount(context, Limits(context).WriteReason));
            options.AddPolicy(RateLimitPolicies.Report, context => PerAccount(context, Limits(context).Report));
        });

        return services;
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>().Value;

    private static RateLimitPartition<string> PerIp(HttpContext context, RateLimitOptions.FixedWindow limit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            });

    /// <summary>Per signed-in account (these endpoints need one); the address only if somehow there is none.</summary>
    private static RateLimitPartition<string> PerAccount(HttpContext context, RateLimitOptions.FixedWindow limit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is { } account ? "account:" + account : "ip:" + ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            });

    internal static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();
}
