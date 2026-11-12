using Microsoft.JSInterop;

namespace Lt2.Web.Client;

/// <summary>Calls the browser's WebAuthn API through <c>js/passkeys.js</c>.</summary>
internal sealed class Passkeys(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<bool> IsSupportedAsync() => await (await ModuleAsync()).InvokeAsync<bool>("isSupported");

    /// <summary>Returns the credential JSON, or <c>null</c> when the person cancelled or the browser refused.</summary>
    public async Task<string?> CreateAsync(string optionsJson) => await CallAsync("create", optionsJson);

    /// <summary>Returns the assertion JSON, or <c>null</c> when the person cancelled or the browser refused.</summary>
    public async Task<string?> GetAsync(string optionsJson) => await CallAsync("get", optionsJson);

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

    private async Task<string?> CallAsync(string function, string optionsJson)
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<string>(function, optionsJson);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/passkeys.js");
}
