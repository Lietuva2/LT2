using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.21 and 8.25 against the stack: the account screens and a member's public profile. A member writes "Apie mane",
/// switches "Viešas profilis" on after reading what it covers, finds the page with the name and the text, a second member
/// sees the name beside an argument lead to it and follows it ("Sekėjai: 1"), and switching the profile off answers 410 at
/// the old address. Screenshots at 1280 and 390 px go to <c>output/profiles</c>. The accounts are deleted by E2ETest; the
/// argument goes with its author.
/// </summary>
public sealed partial class ProfileTests : E2ETest
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    private static string Shots => Path.Combine("output", "profiles");

    [Fact]
    public async Task A_member_switches_a_public_profile_on_is_followed_from_the_name_beside_an_argument_and_switches_it_off()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var author = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var reader = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = author.Page;
        await PasskeyTests.RegisterAsync(page, "Profilio Autorė");

        // 1. "Profilis": the text, the switch, what the consent covers before it is given.
        await page.GotoAsync(Base + "/paskyra");
        var about = page.Locator("#about-text");
        await Assertions.Expect(about).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await about.FillAsync("Mokytoja iš Kauno (E2E bandymas). https://pavyzdys.test/apie");
        await page.Locator("#profile-public-switch").CheckAsync();
        await Assertions.Expect(page.Locator("#profile-consent-text")).ToContainTextAsync("Puslapį gali rodyti paieškos sistemos");
        await E2ESteps.ShootBothAsync(page, Shots, "profile-form", page.Locator("#viesas-profilis"));
        await page.Locator("#profile-save").ClickAsync();
        var link = page.Locator("#profile-link");
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var address = (await link.GetAttributeAsync("href"))!;
        address.ShouldMatch("^/nariai/profilio-autore-[a-z2-9]{10}$");

        // 2. An argument; the page lists it under the name, with "Apie mane" and its link.
        var (written, body) = await E2ESteps.ApiAsync(page, "POST", "/api/v1/bills/XVP-0618/record/reasons",
            new { side = "for", text = "Profilio argumentas apie atliekų tvarkymą (E2E bandymas)." });
        written.ShouldBe(201, body);
        await page.GotoAsync(Base + address);
        await Assertions.Expect(page.Locator("#member-name")).ToHaveTextAsync("Profilio Autorė");
        await Assertions.Expect(page.Locator("#member-about a")).ToHaveAttributeAsync("rel", "nofollow ugc noopener noreferrer");
        await Assertions.Expect(page.Locator("#member-arguments li")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#follow-own-link")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await E2ESteps.ShootBothAsync(page, Shots, "member-page");

        // 3. Another member: the name beside the argument opens the card on hover and leads to the page; "Sekti".
        var other = reader.Page;
        await PasskeyTests.RegisterAsync(other, "Skaitanti Narė");
        await other.GotoAsync(Base + "/seimas/XVP-0618");
        var name = other.Locator($"a.said__name[href='{address}']");
        await Assertions.Expect(name).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await name.HoverAsync();
        await Assertions.Expect(other.Locator("#lt2-card")).ToContainTextAsync("Mokytoja iš Kauno", new() { Timeout = 5_000 });
        await E2ESteps.ShootBothAsync(other, Shots, "card");
        await name.ClickAsync();
        await other.WaitForURLAsync(new Regex(Regex.Escape(address) + "$"), new() { Timeout = E2ESteps.Slow });
        var follow = other.Locator("#follow-toggle-member");
        await Assertions.Expect(follow).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await follow.ClickAsync();
        await Assertions.Expect(follow).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
        await other.ReloadAsync();
        await Assertions.Expect(other.Locator("#followers-count")).ToHaveTextAsync("Sekėjai: 1");

        // 4. Switched off: the follow ends, and the old address answers 410.
        await page.GotoAsync(Base + "/paskyra");
        var publicSwitch = page.Locator("#profile-public-switch");
        await Assertions.Expect(publicSwitch).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await publicSwitch.UncheckAsync();
        await Assertions.Expect(page.Locator("#profile-off-text")).ToBeVisibleAsync();
        await page.Locator("#profile-save").ClickAsync();
        await Assertions.Expect(page.Locator("#profile-off")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var gone = await other.GotoAsync(Base + address);
        gone!.Status.ShouldBe(410);
        var (status, follows) = await E2ESteps.ApiAsync(other, "GET", "/api/v1/me/follows");
        status.ShouldBe(200);
        JsonDocument.Parse(follows).RootElement.GetProperty("members").GetArrayLength().ShouldBe(0);
        author.ConsoleErrors.ShouldBeEmpty();
    }
}
