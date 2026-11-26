using Lt2.Api.Contracts.Meta;
using Microsoft.AspNetCore.Components.Endpoints;

namespace Lt2.Web.Components.Layout;

/// <summary>Which invented-data notice every page shows (design 8.26.5, 9.2).</summary>
internal enum InventedData
{
    /// <summary>Nothing is invented: no notice.</summary>
    None,

    /// <summary>The parliament data came from the stand-in ("Notice.InventedParliament"); it says the most, so it wins.</summary>
    Parliament,

    /// <summary>The parliament data is real and the accounts hold invented members, the trial ("Notice.InventedMembers").</summary>
    Members,
}

/// <summary>
/// Whether the layout shows the invented-data notice, and which, read before the page renders. Blazor renders an error page
/// (<c>/klaida</c>) and a page re-executed for a status code (<c>/nerasta</c>) in one pass without waiting for the components'
/// asynchronous work (ASP.NET Core 10: no quiescence for error-handler and re-executed requests), so a layout that read the
/// state in <c>OnInitializedAsync</c> lost the notice on those pages whenever the answer was not already cached (the first
/// request after the start, and after each minute the cache expires). The middleware awaits the answer for every page request,
/// re-executed ones included (it stands after routing, which the re-execution runs again), and the layout reads it at once.
/// </summary>
internal static class InventedDataNotice
{
    private const string Key = "lt2.invented-data";

    public static IApplicationBuilder UseInventedDataNotice(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null)
        {
            var meta = context.RequestServices.GetRequiredService<IMetaApi>();
            context.Items[Key] = Of((await meta.GetImportsAsync(context.RequestAborted)).Value);
        }

        await next(context);
    });

    /// <summary>The answer the middleware read for this request; null when it read none.</summary>
    public static InventedData? Of(HttpContext? context) => context is not null && context.Items.TryGetValue(Key, out var value) && value is InventedData shown ? shown : null;

    /// <summary>The notice an import state calls for: the parliament's first, then the members'.</summary>
    public static InventedData Of(ImportsResponse? imports) =>
        imports?.ParliamentDataInvented == true ? InventedData.Parliament
        : imports?.MembersInvented == true ? InventedData.Members
        : InventedData.None;

    /// <summary>The notice for this request: the middleware's answer, or read now for a render without it.</summary>
    public static async Task<InventedData> ReadAsync(HttpContext? context, IMetaApi meta) =>
        Of(context) ?? Of((await meta.GetImportsAsync()).Value);
}
