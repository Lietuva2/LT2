using Lt2.Api.Contracts.Meta;
using Microsoft.AspNetCore.Components.Endpoints;

namespace Lt2.Web.Components.Layout;

/// <summary>
/// Whether the layout shows the invented-data notice, read before the page renders. Blazor renders an error page
/// (<c>/klaida</c>) and a page re-executed for a status code (<c>/nerasta</c>) in one pass without waiting for the components'
/// asynchronous work (ASP.NET Core 10: no quiescence for error-handler and re-executed requests), so a layout that read the
/// state in <c>OnInitializedAsync</c> lost the notice on those pages whenever the answer was not already cached (the first
/// request after the start, and after each minute the cache expires). The middleware awaits the answer for every page request,
/// re-executed ones included (it stands after routing, which the re-execution runs again), and the layout reads it at once.
/// </summary>
internal static class InventedDataNotice
{
    private const string Key = "lt2.parliament-data-invented";

    public static IApplicationBuilder UseInventedDataNotice(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null)
        {
            var meta = context.RequestServices.GetRequiredService<IMetaApi>();
            context.Items[Key] = (await meta.GetImportsAsync(context.RequestAborted)).Value?.ParliamentDataInvented == true;
        }

        await next(context);
    });

    /// <summary>The answer the middleware read for this request; null when it read none.</summary>
    public static bool? Of(HttpContext? context) => context is not null && context.Items.TryGetValue(Key, out var value) && value is bool shown ? shown : null;
}
