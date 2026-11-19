using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Components;

/// <summary>
/// The caller's own account (<c>GET /me</c>) as the browser reads it, asked once and shared by the islands that need to know
/// whether it is an MP's own account (design 8.5.4, owner 2026-11-17: the MP's own account takes no part, so its forms are not
/// shown). Never asked while the server prerenders: the server's own answer leaves the MP's link out (design 8.5.4), so the
/// served HTML is the same for every member, and the islands hide their forms once they run. A failed answer is not kept.
/// </summary>
public sealed class MyAccountState(IMeApi me)
{
    private Task<MeResponse?>? _me;

    /// <summary>
    /// Whether the caller is an MP's own linked account (not an office member's); always false before the island runs
    /// (<paramref name="interactive"/> false) and for a visitor.
    /// </summary>
    public async Task<bool> IsMpAccountAsync(bool interactive) =>
        interactive && (await MeAsync())?.Mp is { Part: "mp" };

    /// <summary>
    /// The address part (<c>/p/{slug}</c>) of the MP the caller speaks for (the MP's own account or an office member), or null;
    /// always null before the island runs (<paramref name="interactive"/> false) and for a visitor.
    /// </summary>
    public async Task<string?> SpeaksForAsync(bool interactive) =>
        interactive ? (await MeAsync())?.Mp?.Slug : null;

    /// <summary>The caller's own <c>GET /me</c>, asked once for the page; null when it failed. Only in the browser.</summary>
    public Task<MeResponse?> MeAsync()
    {
        if (_me is null || _me.IsFaulted || (_me.IsCompletedSuccessfully && _me.Result is null))
        {
            _me = FetchAsync();
        }

        return _me;
    }

    private async Task<MeResponse?> FetchAsync()
    {
        try
        {
            return (await me.GetAsync()).Value;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
