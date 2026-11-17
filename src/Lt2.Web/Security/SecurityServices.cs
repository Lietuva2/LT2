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

}

/// <summary>Rate-limit policy names and limits (design 6.4). Limits are per client IP; see <see cref="RateLimitOptions"/>.</summary>
internal static class RateLimitPolicies
{
    public const string CodeRequest = "auth-code";
    public const string Registration = "auth-register";
    public const string SignIn = "auth-sign-in";

    /// <summary>Registering an organisation, per account (design 8.14.14).</summary>
    public const string RegisterOrganisation = "organisation-register";

    /// <summary>Writing arguments, per account (design 6.4).</summary>
    public const string WriteReason = "appeal-reason";

    /// <summary>Writing and deleting comments, per account (design 8.23.4): their own allowance, apart from arguments'.</summary>
    public const string WriteComment = "comment-write";

    /// <summary>Reports, per account (design 6.4).</summary>
    public const string Report = "appeal-report";

    /// <summary>A member's own answers a page asks for in one go (design 8.7, <c>/me/bills</c>), per account.</summary>
    public const string OwnRead = "own-read";

    /// <summary>
    /// Taking, changing or withdrawing a side, following and unfollowing (bills, MPs, organisations), "Man svarbu" (mark
    /// and unmark) and changing the display name (design 6.2), one shared limit per account (design 8.11.2): a guard against a runaway script, not a product rule.
    /// </summary>
    public const string Participation = "participation";

    /// <summary>
    /// The MP's link (design 8.19.4, 8.19.7): the token routes and pages, per client address, like the sign-in ones; each token
    /// has its own limit too (<see cref="MpMailTokens"/>, the global limiter).
    /// </summary>
    public const string MpMail = "mp-mail";
}

/// <summary>
/// Design 8.19.7: the token routes and pages of MP mail are limited per token as well as per client address. The token is read
/// from the path (the API's <c>/api/v1/mp-mail/{token}</c> and the pages), and only its hash is kept as the partition's key.
/// </summary>
internal static class MpMailTokens
{
    private static readonly System.Text.RegularExpressions.Regex TokenInPath = new(
        "^/(?:api/v1/mp-mail|suvestines/nustatymai(?:/patvirtinti)?|suvestines/kopijos)/(?<token>[A-Za-z0-9_-]{20,100})(?:/|$)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The partition of a request: the token's hash for a token route or page, none otherwise.</summary>
    public static string? Partition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var match = TokenInPath.Match(context.Request.Path.Value ?? "");
        return match.Success
            ? "mp-mail-token:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(match.Groups["token"].Value)))
            : null;
    }
}

internal sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public FixedWindow CodeRequest { get; set; } = new() { PermitLimit = 10, WindowSeconds = 15 * 60 };

    public FixedWindow Registration { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    public FixedWindow SignIn { get; set; } = new() { PermitLimit = 30, WindowSeconds = 15 * 60 };

    public FixedWindow RegisterOrganisation { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 * 60 };

    public FixedWindow WriteReason { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    /// <summary>Design 8.23.4: 20 an hour, writing and deleting together.</summary>
    public FixedWindow WriteComment { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    public FixedWindow Report { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 * 60 };

    /// <summary>Generous for a person moving between pages; it stops a script asking about every bill one by one.</summary>
    public FixedWindow OwnRead { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    /// <summary>No person presses sides, follows and marks this often; a script that does is stopped.</summary>
    public FixedWindow Participation { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>The MP's link pages and routes per client address (design 8.19.7): enough for an office, not for guessing.</summary>
    public FixedWindow MpMail { get; set; } = new() { PermitLimit = 60, WindowSeconds = 15 * 60 };

    /// <summary>The same per token: a person using one link never comes near it.</summary>
    public FixedWindow MpMailToken { get; set; } = new() { PermitLimit = 30, WindowSeconds = 15 * 60 };

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
        TrustedProxy.AddTrustedProxy(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = ApiHeaders.Antiforgery;
            options.Cookie.Name = BrowserStorage.AntiforgeryCookieName;
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
                .AddRequirements(new StaffRoleRequirement(Roles.Administrator)));

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
            options.AddPolicy(RateLimitPolicies.RegisterOrganisation, context => PerAccount(context, Limits(context).RegisterOrganisation));
            options.AddPolicy(RateLimitPolicies.WriteReason, context => PerAccount(context, Limits(context).WriteReason));
            options.AddPolicy(RateLimitPolicies.WriteComment, context => PerAccount(context, Limits(context).WriteComment));
            options.AddPolicy(RateLimitPolicies.Report, context => PerAccount(context, Limits(context).Report));
            options.AddPolicy(RateLimitPolicies.OwnRead, context => PerAccount(context, Limits(context).OwnRead));
            options.AddPolicy(RateLimitPolicies.Participation, context => PerAccount(context, Limits(context).Participation));
            options.AddPolicy(RateLimitPolicies.MpMail, context => PerIp(context, Limits(context).MpMail));

            // Design 8.19.7: each MP link's token has its own limit too, wherever it is used.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                MpMailTokens.Partition(context) is { } token
                    ? RateLimitPartition.GetFixedWindowLimiter(token, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Limits(context).MpMailToken.PermitLimit,
                        Window = TimeSpan.FromSeconds(Limits(context).MpMailToken.WindowSeconds),
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("none"));
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
            context.User.UserId() is { } account ? "account:" + account.ToString("N", CultureInfo.InvariantCulture) : "ip:" + ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            });

    internal static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();
}
