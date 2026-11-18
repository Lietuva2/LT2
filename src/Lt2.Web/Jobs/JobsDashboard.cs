using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace Lt2.Web.Jobs;

/// <summary>
/// The Hangfire dashboard of design 3 and 6.3 ("Foniniai darbai", owner 2026-11-18): the worker's recurring jobs, their runs
/// and failures, and "Trigger now", at <see cref="Path"/>. It reads the same Hangfire storage the worker writes (the
/// <c>hangfire</c> schema, through the web host's own connection pool as <c>lt2_app</c>); the web host runs no Hangfire
/// server, so nothing is executed here: a job triggered here is enqueued for the worker. The job types come from
/// <c>Lt2.Jobs</c>, which Hangfire must load to enqueue a recurring job.
/// <para>
/// Only an administrator in a staff-passkey session (<see cref="Policies.Administrator"/>) gets it: the endpoint requires the
/// policy, and <see cref="AdministratorDashboardFilter"/> evaluates it again inside Hangfire. Every write (a form the dashboard
/// posts) passes the Origin check and ASP.NET Core antiforgery, with the token Hangfire puts in the page; Hangfire checks the
/// token once more itself. The pages are <c>no-store</c> and <c>noindex</c> (<see cref="SecurityHeaders"/>), with the one
/// relaxation of the Content Security Policy that <see cref="SecurityHeaders.JobsDashboardContentSecurityPolicy"/> names.
/// </para>
/// </summary>
internal static class JobsDashboard
{
    public const string Path = "/administravimas/darbai";

    /// <summary>The audit entry of an administrator's action through the dashboard (a trigger, a retry, a deletion): no details.</summary>
    public const string AuditAction = "jobs.dashboard-action";

    /// <summary>The schema of the worker's Hangfire storage (<c>DatabaseRoles.HangfireSchema</c>).</summary>
    private const string Schema = "hangfire";

    public static IServiceCollection AddLt2JobsDashboard(this IServiceCollection services)
    {
        // The same serializer settings as the worker's, so a job enqueued here is the job the worker would have enqueued. The
        // schema is the worker's: it installs and upgrades the tables, never the web host.
        services.AddHangfire((provider, hangfire) => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseConnectionFactory(new DataSourceConnections(provider.GetRequiredService<NpgsqlDataSource>())),
                new PostgreSqlStorageOptions { SchemaName = Schema, PrepareSchemaIfNecessary = false }));
        return services;
    }

    public static void MapLt2JobsDashboard(this IEndpointRouteBuilder app)
    {
        app.MapHangfireDashboard(Path, new DashboardOptions
            {
                // Hangfire's default filter (local requests only) is replaced, not added to: the policy decides.
                Authorization = [],
                AsyncAuthorization = [new AdministratorDashboardFilter()],
                IsReadOnlyFunc = context => !AdministratorDashboardFilter.Allowed(context),
                IgnoreAntiforgeryToken = false,
                DashboardTitle = "Foniniai darbai",
                AppPath = "/administravimas/nustatymai",
                DisplayStorageConnectionString = false,
                StatsPollingInterval = 5000,
            })
            .RequireAuthorization(Policies.Administrator)
            .ExcludeFromDescription();
    }

    /// <summary>The dashboard's pages, scripts and polls: under <see cref="Path"/>.</summary>
    public static bool IsDashboard(PathString path) => path.StartsWithSegments(Path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Hangfire's own check (design 6.3): <see cref="Policies.Administrator"/> evaluated for this request, so an administrator in a
/// staff-passkey session and nobody else. A write must also come from this site (the Origin check of every write, design 6.4)
/// with a valid antiforgery token, and is audited as <see cref="JobsDashboard.AuditAction"/> without details; the dashboard's
/// read-only poll of its counters (<c>POST …/stats</c>) is not audited.
/// </summary>
internal sealed class AdministratorDashboardFilter : IDashboardAsyncAuthorizationFilter
{
    private const string AllowedKey = "lt2.jobs.administrator";

    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        if (context is not AspNetCoreDashboardContext { HttpContext: var http })
        {
            return false;
        }

        var services = http.RequestServices;
        var policy = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, http, Policies.Administrator);
        if (!policy.Succeeded)
        {
            return false;
        }

        var method = http.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            if (!AntiforgeryAndOriginFilter.IsSameOrigin(http.Request)
                || !await services.GetRequiredService<IAntiforgery>().IsRequestValidAsync(http))
            {
                return false;
            }

            if (!http.Request.Path.StartsWithSegments("/stats", StringComparison.OrdinalIgnoreCase))
            {
                await services.GetRequiredService<IAuditLog>().WriteAsync(
                    new AuditEntry(JobsDashboard.AuditAction, http.User.UserId(), Roles.Administrator, Target: null, Details: null, services.GetRequiredService<IpHasher>().Hash(http)),
                    transaction: null,
                    http.RequestAborted);
            }
        }

        http.Items[AllowedKey] = true;
        return true;
    }

    /// <summary>Whether this request passed the filter: the dashboard shows its buttons only then (otherwise it is read-only).</summary>
    public static bool Allowed(DashboardContext context) =>
        context is AspNetCoreDashboardContext { HttpContext: var http } && http.Items.TryGetValue(AllowedKey, out var allowed) && allowed is true;
}

/// <summary>Hangfire's connections from the web host's own pool (design 9.5: no second pool for the dashboard).</summary>
internal sealed class DataSourceConnections(NpgsqlDataSource dataSource) : IConnectionFactory
{
    public NpgsqlConnection GetOrCreateConnection() => dataSource.CreateConnection();
}
