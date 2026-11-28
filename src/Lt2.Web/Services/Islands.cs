using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Lt2.Web.Services;

/// <summary>
/// How a public page renders its islands (owner, 2026-11-28; design 2, "only signed-in features use WebAssembly"): in a
/// signed-in session they run in WebAssembly; for a visitor they are plain server-rendered HTML, the same markup an island
/// prerenders for a visitor (sign-in links, how to report, the texts), so a visitor's browser never downloads or starts the
/// WebAssembly runtime. Signing in and out loads the page in full, so the choice is made again.
/// </summary>
internal sealed class Islands(IHttpContextAccessor accessor)
{
    /// <summary>True in a signed-in session: the page's islands run in WebAssembly.</summary>
    public bool On => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    /// <summary>The render mode for a public page's island: WebAssembly when <see cref="On"/>, none (static) for a visitor.</summary>
    public IComponentRenderMode? Mode => On ? RenderMode.InteractiveWebAssembly : null;
}
