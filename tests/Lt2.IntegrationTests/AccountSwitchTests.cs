using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2 (owner, 2026-11-17): a sign-in never silently switches the account. Every sign-in starts a new session under a
/// new key; in a browser signed in to another account that session ends as by signing out (its row is deleted, its cookie
/// leads nowhere) and the answer says the account changed, so the page can say so.
/// </summary>
public sealed class AccountSwitchTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_passkey_sign_in_in_a_browser_signed_in_to_another_account_ends_that_session_and_says_so()
    {
        var (first, firstEmail) = await Factory.MemberAsync("Pirmas Narys");
        var (second, secondKey, secondEmail) = await Factory.PasskeyMemberAsync("Antras Narys");
        using var _s = second;
        using var _k = secondKey;
        var (browser, cookies) = BrowserWithCookies();
        using var _b = browser;
        using var _f = first;
        await SignInByCodeAsync(browser, firstEmail);
        var before = (await SessionsAsync(browser)).Single(s => s.IsCurrent);
        var oldCookie = SessionCookie(cookies);

        using (var signIn = await browser.SignInWithPasskeyAsync(secondKey))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
            (await signIn.Content.ReadFromJsonAsync<SignInResponse>())!.AccountChanged.ShouldBeTrue();
        }

        (await browser.MeAsync()).Email.ShouldBe(secondEmail);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE id = @id", ("id", before.Id)))
            .ShouldBe(0, "the other account's session ended, as by signing out");
        var after = (await SessionsAsync(browser)).Single(s => s.IsCurrent);
        after.Id.ShouldNotBe(before.Id, "a new session under a new key");
        after.Method.ShouldBe("pop");
        SessionCookie(cookies).ShouldNotBe(oldCookie);
        await OldCookieIsAVisitorsAsync(oldCookie);
        await first.DeleteAccountAsync();
        await second.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_code_sign_in_in_a_browser_signed_in_to_another_account_ends_that_session_and_says_so()
    {
        var (first, firstEmail) = await Factory.MemberAsync("Pirmas Kodu");
        var (second, secondEmail) = await Factory.MemberAsync("Antras Kodu");
        using var _f = first;
        using var _s = second;
        var before = (await SessionsAsync(first)).Single(s => s.IsCurrent);

        using (var asked = await first.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = secondEmail }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var signIn = await first.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = secondEmail, Code = (await Factory.LatestCodeAsync(secondEmail))! }))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
            (await signIn.Content.ReadFromJsonAsync<SignInResponse>())!.AccountChanged.ShouldBeTrue();
        }

        (await first.MeAsync()).Email.ShouldBe(secondEmail);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE id = @id", ("id", before.Id))).ShouldBe(0);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.session WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", firstEmail)))
            .ShouldBe(0, "no session of the first account was handed to the second");
        await first.DeleteAccountAsync();
        using var again = await Factory.CodeSessionAsync(firstEmail);
        await again.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_sign_in_to_the_same_account_says_nothing_changed_and_still_takes_a_new_key()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Tas Pats Narys");
        using var _b = browser;
        using var _k = key;
        var before = (await SessionsAsync(browser)).Single(s => s.IsCurrent);

        using (var signIn = await browser.SignInWithPasskeyAsync(key))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await signIn.Content.ReadFromJsonAsync<SignInResponse>())!.AccountChanged.ShouldBeFalse();
        }

        (await browser.MeAsync()).Email.ShouldBe(email);
        var after = (await SessionsAsync(browser)).Single(s => s.IsCurrent);
        after.Id.ShouldNotBe(before.Id);
        (await SessionsAsync(browser)).ShouldNotContain(s => s.Id == before.Id, "one session for this browser, not two");

        // A visitor's sign-in has no other account to end either.
        using var visitor = NewBrowser();
        using var fresh = await visitor.SignInWithPasskeyAsync(key);
        (await fresh.Content.ReadFromJsonAsync<SignInResponse>())!.AccountChanged.ShouldBeFalse();
        await browser.DeleteAccountAsync();
    }

    /// <summary>A browser whose cookies the test can read.</summary>
    private (TestBrowser Browser, CookieContainer Cookies) BrowserWithCookies()
    {
        var cookies = new CookieContainer();
        var client = Factory.CreateDefaultClient(new Uri(Lt2Factory.Origin), new CookieContainerHandler(cookies));
        client.DefaultRequestHeaders.Add("Origin", Lt2Factory.Origin);
        return (new TestBrowser(client, Factory), cookies);
    }

    private static string SessionCookie(CookieContainer cookies) =>
        cookies.GetCookies(new Uri(Lt2Factory.Origin))[Lt2.Identity.IdentityModule.SessionCookieName]?.Value.ShouldNotBeNull()!;

    private async Task OldCookieIsAVisitorsAsync(string cookie)
    {
        using var stale = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin), HandleCookies = false, AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Me.Root);
        request.Headers.Add("Cookie", $"{Lt2.Identity.IdentityModule.SessionCookieName}={cookie}");
        using var response = await stale.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task SignInByCodeAsync(TestBrowser browser, string email)
    {
        using (var asked = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await browser.SignInWithCodeAsync(email);
    }

    private static async Task<List<SessionResponse>> SessionsAsync(TestBrowser browser) =>
        (await browser.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!;
}
