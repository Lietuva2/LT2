using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts.Me;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Steps for design 8.25 and 8.28.5: the "Profilis" form, a member's public profile and following a member.</summary>
public static class ProfileSteps
{
    /// <summary>
    /// The form as the "Profilis" screen starts from it (design 8.28.5): what the state holds, sent back unchanged; the boxes of
    /// a consent in force stay ticked with their versions.
    /// </summary>
    public static ProfileRequest FormOf(MyProfileResponse state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ProfileRequest
        {
            DisplayName = state.DisplayName,
            About = state.About,
            Public = state.ProfileId is not null,
            PublicConsentVersion = state.ProfileId is not null ? ProfileLimits.ConsentVersion : null,
            Party = state.Party,
            PartyPublic = state.PartyPublic,
            PartyPublicConsentVersion = state.PartyPublic ? PartyPublicLimits.ConsentVersion : null,
        };
    }

    /// <summary>The caller's form as it stands (an empty one when the state cannot be read: a visitor, a pending account).</summary>
    public static async Task<ProfileRequest> FormAsync(this TestBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        using var mine = await browser.Client.GetAsync(ProfileRoutes.Mine, TestContext.Current.CancellationToken);
        return mine.IsSuccessStatusCode
            ? FormOf((await mine.Content.ReadFromJsonAsync<MyProfileResponse>(System.Text.Json.JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!)
            : new ProfileRequest();
    }

    /// <summary>Saves the whole form as it stands with <paramref name="change"/> made to it, as the "Profilis" screen does.</summary>
    public static async Task<HttpResponseMessage> SaveFormAsync(this TestBrowser browser, Func<ProfileRequest, ProfileRequest> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return await browser.SendAsync(HttpMethod.Put, ProfileRoutes.Mine, change(await browser.FormAsync()));
    }

    /// <summary>Saves the form with <paramref name="change"/> and expects it to succeed; the state after it.</summary>
    public static async Task<MyProfileResponse> SavedFormAsync(this TestBrowser browser, Func<ProfileRequest, ProfileRequest> change)
    {
        using var saved = await browser.SaveFormAsync(change);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return (await saved.Content.ReadFromJsonAsync<MyProfileResponse>(System.Text.Json.JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Saves "Apie mane" and the switch with the rest of the form as it stands, with the consent's version when public; with the
    /// profile off the party's box is not offered, so it goes unticked, as on the screen.
    /// </summary>
    public static Task<HttpResponseMessage> SaveProfileAsync(this TestBrowser browser, string? about, bool isPublic, string? consentVersion = ProfileLimits.ConsentVersion) =>
        browser.SaveFormAsync(f => f with
        {
            About = about,
            Public = isPublic,
            PublicConsentVersion = isPublic ? consentVersion : null,
            PartyPublic = isPublic && f.PartyPublic,
        });

    /// <summary>Saves the profile and expects it to succeed; the person's own state after it.</summary>
    public static async Task<MyProfileResponse> SavedProfileAsync(this TestBrowser browser, string? about, bool isPublic)
    {
        using var saved = await browser.SaveProfileAsync(about, isPublic);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return (await saved.Content.ReadFromJsonAsync<MyProfileResponse>(System.Text.Json.JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!;
    }

    /// <summary>Saves the form with another party (null: none), its consent given with it and the party's box unticked.</summary>
    public static Task<HttpResponseMessage> SavePartyAsync(this TestBrowser browser, string? party) =>
        browser.SaveFormAsync(f => f with { Party = party, PartyConsent = party is not null, PartyPublic = false, PartyPublicConsentVersion = null });

    /// <summary>Sets the party and expects it to succeed.</summary>
    public static async Task SetPartyAsync(this TestBrowser browser, string? party)
    {
        using var set = await browser.SavePartyAsync(party);
        set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
    }

    /// <summary>Ticks "Rodyti viešame profilyje" and saves the form, with the version of the text the form shows.</summary>
    public static Task<HttpResponseMessage> ShowPartyAsync(this TestBrowser browser, string? consentVersion = PartyPublicLimits.ConsentVersion) =>
        browser.SaveFormAsync(f => f with { PartyPublic = true, PartyPublicConsentVersion = consentVersion });

    /// <summary>The caller's own form state.</summary>
    public static Task<MyProfileResponse> MyProfileAsync(this TestBrowser browser) => browser.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine);

    /// <summary>A member with a public profile: the browser, the address and the public id.</summary>
    public static async Task<(TestBrowser Browser, string Email, string ProfileId)> PublicMemberAsync(
        this Lt2Factory factory, string displayName = "Rūta Mockutė", string? about = "Mokytoja iš Kauno.")
    {
        var (browser, email) = await factory.MemberAsync(displayName);
        var state = await browser.SavedProfileAsync(about, isPublic: true);
        return (browser, email, state.ProfileId.ShouldNotBeNull());
    }

    public static async Task FollowMemberAsync(this TestBrowser browser, string profileId)
    {
        using var followed = await browser.SendAsync(HttpMethod.Put, ProfileRoutes.FollowOf(profileId));
        followed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await followed.Content.ReadAsStringAsync());
    }

    /// <summary>A profile id that has the right shape and was never issued.</summary>
    public static string UnknownProfileId() => "zzzz" + Random.Shared.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture)
        .Replace('0', 'a').Replace('1', 'b');
}
