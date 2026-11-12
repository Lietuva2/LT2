using Lt2.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Security;

/// <summary>
/// Every state-changing API call must come from this site's own pages (design 6.4): the <c>Origin</c> header must equal
/// the site's origin, and the antiforgery token header must match the antiforgery cookie. CORS is not enabled, so other
/// sites can neither read the token nor send the header.
/// </summary>
internal sealed class AntiforgeryAndOriginFilter(IAntiforgery antiforgery, IStringLocalizer<ProblemText> l) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
        {
            return await next(context);
        }

        if (!IsSameOrigin(http.Request))
        {
            return Reject("origin.invalid");
        }

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Reject("antiforgery.invalid");
        }

        return await next(context);
    }

    internal static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var expected = $"{request.Scheme}://{request.Host.Value}";
        return Uri.TryCreate(expected, UriKind.Absolute, out var expectedUri)
            && Uri.Compare(originUri, expectedUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private ProblemHttpResult Reject(string code) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: l["Title.403"],
            detail: l[code],
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
