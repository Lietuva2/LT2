using Lt2.Api.Contracts;
using Microsoft.JSInterop;

namespace Lt2.Web.Client;

/// <summary>
/// The site's own script <c>js/lt2-botcheck.js</c> (design 6.4): it fetches a proof-of-work challenge and solves it in the
/// background, so the person never waits at the button. Nothing is kept in the browser. Whatever fails here (no module, no
/// <c>crypto.subtle</c>, no network) gives no solution, and the server's refusal is what the person is told.
/// </summary>
internal sealed class BotCheckBrowser(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>Starts the work for the next request, if none is under way; returns at once.</summary>
    public async Task PrepareAsync()
    {
        try
        {
            await (await ModuleAsync()).InvokeVoidAsync("prepare", ApiRoutes.BotCheck.Challenge);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // Nothing to prepare: the request will be asked for its solution, and be refused without one.
        }
    }

    /// <summary>The solution to send, waiting for the work if it is not done; null when none is needed or the browser could not make one.</summary>
    public async Task<string?> TakeAsync()
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<string?>("take", ApiRoutes.BotCheck.Challenge);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/lt2-botcheck.js");
}
