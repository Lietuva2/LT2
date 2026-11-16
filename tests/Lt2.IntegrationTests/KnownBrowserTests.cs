using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2, 8.20.2: a browser that signed in to an account is known to it by the <c>__Host-lt2.device</c> cookie. It has its
/// own five codes an hour and ten a day (the same two settings), counted apart from the limit shared by every other browser; a code it asked for is accepted only from it, and
/// requests and wrong guesses elsewhere neither use it up nor cancel it. The person sees and forgets known browsers, at most
/// twenty; they are exported and deleted with the account. The cookie gives no access by itself, and no answer says whether a
/// browser is known.
/// </summary>
public sealed class KnownBrowserTests(DatabaseFixture database) : ApiTest(database)
{
    private const string DeviceCookie = "__Host-lt2.device";

    /// <summary>A member registered and signed in from <paramref name="browser"/>, which is then signed out: the usual browser.</summary>
    private static async Task<string> UsualBrowserAsync(TestBrowser browser)
    {
        var email = await browser.RegisterAndSignInAsync();
        using var signedOut = await browser.PostAsync(ApiRoutes.Auth.SignOut);
        signedOut.IsSuccessStatusCode.ShouldBeTrue(await signedOut.Content.ReadAsStringAsync());
        return email;
    }

    private static async Task RequestCodeAsync(TestBrowser browser, string email)
    {
        using var requested = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await requested.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    private static Task<HttpResponseMessage> SignInAsync(TestBrowser browser, string email, string code) =>
        browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });

    /// <summary>The address's shared limits used up for the day: ten codes from browsers it does not know.</summary>
    private Task UseUpSharedLimitsAsync(string email) =>
        Database.ExecuteAsync(
            """
            INSERT INTO identity.sign_in_code (id, user_id, code_hash, created_at, expires_at, failed_attempts, consumed_at)
            SELECT gen_random_uuid(), id, '\x00'::bytea, now() - interval '3 hours', now() - interval '3 hours', 0, now()
            FROM identity."user", generate_series(1, 10)
            WHERE email = @e
            """,
            ("e", email));

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task With_the_shared_limit_used_up_from_elsewhere_the_usual_browser_still_gets_a_code_and_signs_in()
    {
        using var usual = NewBrowser();
        var email = await UsualBrowserAsync(usual);
        await UseUpSharedLimitsAsync(email);
        var sent = await Factory.OutboxCountAsync(email);

        // A stranger's browser gets nothing more, and the answer does not say so.
        using var stranger = NewBrowser();
        await RequestCodeAsync(stranger, email);
        (await Factory.OutboxCountAsync(email)).ShouldBe(sent, "the shared limits hold for every browser the account does not know");

        // The usual browser has its own allowance: five codes an hour, and no more.
        for (var i = 0; i < 6; i++)
        {
            await RequestCodeAsync(usual, email);
        }

        (await Factory.OutboxCountAsync(email)).ShouldBe(sent + 5, "five codes an hour for the known browser");
        await usual.SignInWithCodeAsync(email);
        (await usual.MeAsync()).Email.ShouldBe(email);
    }

    [Fact]
    public async Task The_eleventh_code_of_a_day_for_a_known_browser_is_refused_as_the_shared_limit_refuses_and_other_browsers_keep_theirs()
    {
        using var usual = NewBrowser();
        var email = await UsualBrowserAsync(usual);

        // Ten codes of the usual browser earlier today, outside the last hour: its own day is used up.
        await Database.ExecuteAsync(
            """
            INSERT INTO identity.sign_in_code (id, user_id, code_hash, created_at, expires_at, failed_attempts, consumed_at, known_device_id)
            SELECT gen_random_uuid(), d.user_id, '\x00'::bytea, now() - interval '3 hours', now() - interval '3 hours', 0, now(), d.id
            FROM identity.known_device d JOIN identity."user" u ON u.id = d.user_id, generate_series(1, 10)
            WHERE u.email = @e
            """,
            ("e", email));
        var sent = await Factory.OutboxCountAsync(email);
        using var refusedKnown = await usual.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });
        (await Factory.OutboxCountAsync(email)).ShouldBe(sent, "the eleventh code of the day for the known browser is not sent");

        // Another browser's shared allowance is untouched by them.
        using var stranger = NewBrowser();
        await RequestCodeAsync(stranger, email);
        (await Factory.OutboxCountAsync(email)).ShouldBe(sent + 1);

        // Refused in the same way the shared limit refuses: the same status, the same empty body, nothing about the browser.
        await UseUpSharedLimitsAsync(email);
        using var refusedShared = await stranger.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });
        (await Factory.OutboxCountAsync(email)).ShouldBe(sent + 1);
        refusedKnown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        refusedKnown.StatusCode.ShouldBe(refusedShared.StatusCode);
        (await refusedKnown.Content.ReadAsStringAsync()).ShouldBe(await refusedShared.Content.ReadAsStringAsync());
        (await refusedKnown.Content.ReadAsStringAsync()).ShouldBeEmpty();
        refusedKnown.Headers.Contains("Set-Cookie").ShouldBe(refusedShared.Headers.Contains("Set-Cookie"));
    }
    [Fact]
    public async Task A_code_asked_for_by_a_known_browser_is_refused_elsewhere_and_guesses_and_requests_there_do_not_cancel_it()
    {
        using var usual = NewBrowser();
        var email = await UsualBrowserAsync(usual);
        await RequestCodeAsync(usual, email);
        var code = (await Factory.LatestCodeAsync(email))!;

        using var stranger = NewBrowser();
        await (await SignInAsync(stranger, email, code)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        for (var i = 0; i < 5; i++)
        {
            await (await SignInAsync(stranger, email, Wrong(code))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        // A code asked for elsewhere replaces only the codes any browser may use.
        await RequestCodeAsync(stranger, email);
        var strangers = (await Factory.LatestCodeAsync(email))!;

        using (var signedIn = await SignInAsync(usual, email, code))
        {
            signedIn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await signedIn.Content.ReadAsStringAsync());
        }

        // A code any browser may use still works anywhere, the known browser included (design 6.2).
        using var phone = NewBrowser();
        (await SignInAsync(phone, email, strangers)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Wrong_guesses_in_the_known_browser_use_up_only_its_own_code_and_the_answers_look_the_same_everywhere()
    {
        using var usual = NewBrowser();
        var email = await UsualBrowserAsync(usual);
        await RequestCodeAsync(usual, email);
        var code = (await Factory.LatestCodeAsync(email))!;
        using var stranger = NewBrowser();
        using var fromStranger = await SignInAsync(stranger, email, Wrong(code));
        using var fromUsual = await SignInAsync(usual, email, Wrong(code));
        fromStranger.StatusCode.ShouldBe(fromUsual.StatusCode);
        (await fromStranger.ProblemAsync()).GetProperty("code").GetString().ShouldBe((await fromUsual.ProblemAsync()).GetProperty("code").GetString());
        for (var i = 0; i < 4; i++)
        {
            (await SignInAsync(usual, email, Wrong(code))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        await (await SignInAsync(usual, email, code)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
    }

    [Fact]
    public async Task The_person_sees_the_known_browsers_and_a_forgotten_one_is_a_stranger_again()
    {
        using var usual = NewBrowser();
        var email = await usual.RegisterAndSignInAsync();
        var list = await usual.GetJsonAsync<List<KnownBrowserResponse>>(ApiRoutes.Me.KnownBrowsers);
        var mine = list.ShouldHaveSingleItem();
        mine.IsCurrent.ShouldBeTrue();
        mine.FirstSeenAt.ShouldBe(mine.LastSeenAt);
        using (var raw = await usual.Client.GetAsync(ApiRoutes.Me.KnownBrowsers))
        {
            using var document = JsonDocument.Parse(await raw.Content.ReadAsStringAsync());
            document.RootElement[0].EnumerateObject().Select(p => p.Name).ShouldBe(["id", "device", "firstSeenAt", "lastSeenAt", "isCurrent"], ignoreOrder: true);
        }

        // Another browser signing in is listed too, not as this one.
        using var phone = NewBrowser();
        await RequestCodeAsync(phone, email);
        await phone.SignInWithCodeAsync(email);
        list = await usual.GetJsonAsync<List<KnownBrowserResponse>>(ApiRoutes.Me.KnownBrowsers);
        list.Count.ShouldBe(2);
        list.Count(b => b.IsCurrent).ShouldBe(1);

        // "Pamiršti": the usual browser becomes a stranger; with the shared limit used up it gets no code any more.
        (await usual.SendAsync(HttpMethod.Delete, ApiRoutes.Me.KnownBrowserById(mine.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await usual.SendAsync(HttpMethod.Delete, ApiRoutes.Me.KnownBrowserById(mine.Id))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "known-browser.not-found");
        (await usual.GetJsonAsync<List<KnownBrowserResponse>>(ApiRoutes.Me.KnownBrowsers)).ShouldHaveSingleItem().IsCurrent.ShouldBeFalse();
        await (await phone.SendAsync(HttpMethod.Delete, ApiRoutes.Me.KnownBrowserById(mine.Id))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "known-browser.not-found");
        using (var signedOut = await usual.PostAsync(ApiRoutes.Auth.SignOut))
        {
            signedOut.IsSuccessStatusCode.ShouldBeTrue();
        }

        await UseUpSharedLimitsAsync(email);
        var sent = await Factory.OutboxCountAsync(email);
        await RequestCodeAsync(usual, email);
        (await Factory.OutboxCountAsync(email)).ShouldBe(sent, "a forgotten browser shares the limits of strangers");

        // Another account's browser list is not reachable: a known browser's id of one account is unknown to another.
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        var phoneId = (await phone.GetJsonAsync<List<KnownBrowserResponse>>(ApiRoutes.Me.KnownBrowsers)).Single(b => b.IsCurrent).Id;
        await (await other.SendAsync(HttpMethod.Delete, ApiRoutes.Me.KnownBrowserById(phoneId))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "known-browser.not-found");
        (await phone.GetJsonAsync<List<KnownBrowserResponse>>(ApiRoutes.Me.KnownBrowsers)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_account_keeps_twenty_known_browsers_and_the_one_seen_longest_ago_goes()
    {
        using var usual = NewBrowser();
        var email = await usual.RegisterAndSignInAsync();
        var userId = await Database.UserIdAsync(email);
        await Database.ExecuteAsync(
            """
            INSERT INTO identity.known_device (id, user_id, identifier_hash, label, first_seen_at, last_seen_at)
            SELECT gen_random_uuid(), @u, sha256(('pavyzdys-' || n)::bytea), 'Firefox, Linux', now() - (n || ' days')::interval, now() - (n || ' days')::interval
            FROM generate_series(1, 20) n
            """,
            ("u", userId));
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.known_device WHERE user_id = @u", ("u", userId))).ShouldBe(21);

        using var phone = NewBrowser();
        await RequestCodeAsync(phone, email);
        await phone.SignInWithCodeAsync(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.known_device WHERE user_id = @u", ("u", userId))).ShouldBe(20);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.known_device WHERE user_id = @u AND last_seen_at < now() - interval '19 days'", ("u", userId))).ShouldBe(0, "the two seen longest ago went");
        var list = await phone.GetJsonAsync<List<KnownBrowserResponse>>(ApiRoutes.Me.KnownBrowsers);
        list.Count.ShouldBe(20);
        list.Count(b => b.IsCurrent).ShouldBe(1);
    }

    [Fact]
    public async Task Known_browsers_are_in_the_export_without_the_cookie_and_go_with_the_account()
    {
        using var usual = NewBrowser();
        var email = await usual.RegisterAndSignInAsync();
        var userId = await Database.UserIdAsync(email);
        using var export = await usual.Client.GetAsync(ApiRoutes.Me.Export);
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var document = JsonDocument.Parse(await export.Content.ReadAsStringAsync()))
        {
            var browser = document.RootElement.GetProperty("knownBrowsers").EnumerateArray().ShouldHaveSingleItem();
            browser.EnumerateObject().Select(p => p.Name).ShouldBe(["browser", "firstSeenAt", "lastSeenAt"], ignoreOrder: true);
        }

        await usual.DeleteAccountAsync();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.known_device WHERE user_id = @u", ("u", userId))).ShouldBe(0);
    }

    [Fact]
    public async Task The_cookie_is_set_after_registration_and_sign_in_never_for_a_pending_account_and_gives_no_access_by_itself()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);

        // The pending account's sign-in proves the address but makes no known browser (design 8.20.2: at the end of registration).
        using (var signedIn = await SignInAsync(browser, email, code))
        {
            signedIn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            signedIn.Headers.GetValues("Set-Cookie").ShouldNotContain(c => c.StartsWith(DeviceCookie + "=", StringComparison.Ordinal));
        }

        string cookie;
        using (var completed = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { ConfirmsAdult = true, AcceptsTerms = true }))
        {
            completed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            cookie = completed.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(DeviceCookie + "=", StringComparison.Ordinal));
        }

        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldNotContain("domain=", Case.Insensitive);
        var expires = DateTimeOffset.Parse(cookie.Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Trim()["expires=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        expires.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(364));
        var value = cookie[(DeviceCookie.Length + 1)..cookie.IndexOf(';', StringComparison.Ordinal)];
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.known_device WHERE position(convert_to(@v, 'UTF8') in identifier_hash) > 0", ("v", value)))
            .ShouldBe(0, "only a keyed hash of the identifier is kept");

        // The cookie alone: no session, no account, no page of the account.
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin), AllowAutoRedirect = false, HandleCookies = false });
        using var _c = client;
        client.DefaultRequestHeaders.Add("Cookie", DeviceCookie + "=" + value);
        (await client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync(ApiRoutes.Me.KnownBrowsers)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/paskyra")).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        // Signing out keeps it; signing in again renews it, as the same browser.
        using (var signedOut = await browser.PostAsync(ApiRoutes.Auth.SignOut))
        {
            signedOut.IsSuccessStatusCode.ShouldBeTrue();
            (signedOut.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []).ShouldNotContain(c => c.StartsWith(DeviceCookie + "=", StringComparison.Ordinal));
        }

        await RequestCodeAsync(browser, email);
        using (var again = await SignInAsync(browser, email, (await Factory.LatestCodeAsync(email))!))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            again.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(DeviceCookie + "=", StringComparison.Ordinal)).ShouldStartWith(DeviceCookie + "=" + value + ";");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.known_device WHERE user_id = @u", ("u", await Database.UserIdAsync(email)))).ShouldBe(1);
    }
}
