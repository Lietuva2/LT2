using Lt2.Api.Contracts.Me;
using Lt2.Web.Client.Components;
using Microsoft.AspNetCore.Components;

namespace Lt2.Web.Client.Pages;

/// <summary>
/// What the four account screens share (design 8.21): the account's own answer (<c>GET /me</c>), read once on the server and
/// handed to the browser, and the way out for anyone who is not a member yet (a pending account finishes its registration on
/// the sign-in page first, design 6.2).
/// </summary>
public abstract class AccountScreenBase : InteractiveBase<UiText>
{
    [Inject]
    protected IMeApi MeApi { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [PersistentState]
    public MeResponse? Me { get; set; }

    /// <summary>
    /// Reads the account once; false when the page has sent the person elsewhere (not signed in, or a pending account), and
    /// the screen then loads nothing more.
    /// </summary>
    protected async Task<bool> LoadMeAsync()
    {
        if (Me is null)
        {
            var me = await MeApi.GetAsync();
            if (!me.IsSuccess)
            {
                Navigation.NavigateTo("/prisijungti", forceLoad: true);
                return false;
            }

            Me = me.Value;
        }

        if (!Me!.RegistrationComplete)
        {
            Navigation.NavigateTo("/prisijungti", forceLoad: true);
            return false;
        }

        return true;
    }

    protected async Task ReloadMeAsync()
    {
        var me = await MeApi.GetAsync();
        if (me.IsSuccess)
        {
            Me = me.Value;
        }
    }
}
