using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// Runtime settings as an administrator changes them (design 5.6). Change a setting through the same application that reads
/// it: each application keeps its own short cache of settings, and only a change made through it clears that cache.
/// </summary>
public static class SettingsSteps
{
    /// <summary>Sets <paramref name="key"/> to <paramref name="value"/>; asserts the change was accepted.</summary>
    public static async Task SetSettingAsync(this TestBrowser administrator, string key, object value)
    {
        using var response = await administrator.SendAsync(
            HttpMethod.Put, ApiRoutes.Admin.SettingByKey(key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(value) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{key}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Sets <paramref name="key"/> for the rest of a <c>using</c> block and puts back the value it had before (not a value
    /// written into the test), so a changed default in <c>Settings.cs</c> can never be overwritten by an old number.
    /// </summary>
    public static async Task<IAsyncDisposable> WithSettingAsync(this TestBrowser administrator, string key, object value)
    {
        var before = (await administrator.GetJsonAsync<List<SettingResponse>>(ApiRoutes.Admin.Settings)).Single(s => s.Key == key).Value.Clone();
        await administrator.SetSettingAsync(key, value);
        return new Restore(administrator, key, before);
    }

    /// <summary>
    /// Puts <paramref name="key"/> back to the design's default as the settings answer states it, for the <c>finally</c> of a
    /// test that changed it; never a number written into the test.
    /// </summary>
    public static async Task ResetSettingAsync(this TestBrowser administrator, string key)
    {
        var setting = (await administrator.GetJsonAsync<List<SettingResponse>>(ApiRoutes.Admin.Settings)).Single(s => s.Key == key);
        await administrator.SetSettingAsync(key, setting.Default ?? throw new InvalidOperationException($"{key} has no default."));
    }

    private sealed class Restore(TestBrowser administrator, string key, JsonElement before) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await administrator.SetSettingAsync(key, before);
    }
}
