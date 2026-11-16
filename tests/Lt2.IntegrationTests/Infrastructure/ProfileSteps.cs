using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts.Me;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Steps for design 8.25: a member's public profile, "Apie mane" and following a member.</summary>
public static class ProfileSteps
{
    /// <summary>Saves "Apie mane" and the switch as the "Profilis" form does, with the consent's version when switching on.</summary>
    public static Task<HttpResponseMessage> SaveProfileAsync(this TestBrowser browser, string? about, bool isPublic, string? consentVersion = ProfileLimits.ConsentVersion) =>
        browser.SendAsync(HttpMethod.Put, ProfileRoutes.Mine, new ProfileRequest { About = about, Public = isPublic, ConsentVersion = isPublic ? consentVersion : null });

    /// <summary>Saves the profile and expects it to succeed; the person's own state after it.</summary>
    public static async Task<MyProfileResponse> SavedProfileAsync(this TestBrowser browser, string? about, bool isPublic)
    {
        using var saved = await browser.SaveProfileAsync(about, isPublic);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return (await saved.Content.ReadFromJsonAsync<MyProfileResponse>(System.Text.Json.JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!;
    }

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
