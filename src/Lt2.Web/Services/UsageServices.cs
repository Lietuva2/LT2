using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Hosting;
using Lt2.SharedKernel;
using Lt2.Usage;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// Design 8.27: hands every answered request to the Usage module, which counts it if it is a page view. The path goes with
/// a link's token replaced (<see cref="SecretPaths"/>) and without its query; the address is the one <see cref="TrustedProxy"/>
/// let through; a signed-in session gives its account. Nothing is written to the response or the browser.
/// </summary>
internal static class UsageCounting
{
    public static IApplicationBuilder UseUsageCounting(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next(context);

            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method))
            {
                return;
            }

            context.RequestServices.GetRequiredService<IUsageCounter>().Count(new PageView(
                request.Method,
                SecretPaths.Redact(request.Path.Value) ?? "/",
                context.Response.StatusCode,
                context.Response.ContentType,
                IsPrefetch(request),
                context.Connection.RemoteIpAddress,
                request.Headers.UserAgent.ToString(),
                context.User.UserId(),
                request.Headers.Referer.ToString(),
                request.Host.Host));
        });

    /// <summary>A page the browser fetched on its own, ahead of a click (<c>Sec-Purpose</c>, or the older <c>Purpose</c>).</summary>
    private static bool IsPrefetch(HttpRequest request) =>
        request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
        || request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase);
}

internal static class UsageMapping
{
    public static UsageResponse Map(UsageReport report) => new(
        report.Online.Visitors,
        report.Online.Members,
        report.Days.Select(Map).ToList(),
        report.Months.Select(Map).ToList(),
        report.Years.Select(Map).ToList(),
        report.RangeDays,
        report.Pages.Select(i => new UsageItemResponse(i.Name, i.Views)).ToList(),
        report.Referrers.Select(i => new UsageItemResponse(i.Name, i.Views)).ToList());

    private static UsagePeriodResponse Map(UsageTotal t) => new(t.Starts, t.Visitors, t.Members, t.VisitorViews, t.MemberViews, t.CrawlerViews);
}

/// <summary>Server-side <see cref="IUsageApi"/> for the screen's first render: the same answer, the same administrator check.</summary>
internal sealed class ServerUsageApi(IUsageReport report, IHttpContextAccessor accessor, ErrorMapper errors) : IUsageApi
{
    public async Task<ApiResult<UsageResponse>> GetAsync(int days = UsageRoutes.DefaultRange, CancellationToken cancellationToken = default)
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return ApiResult<UsageResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, http, Policies.Administrator)).Succeeded
            ? ApiResult<UsageResponse>.Ok(UsageMapping.Map(await report.ReportAsync(days, cancellationToken)))
            : ApiResult<UsageResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
    }
}
