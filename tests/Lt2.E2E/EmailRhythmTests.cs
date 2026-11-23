using System.Text.Json;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.24 against the running stack: a member changes how often emails come on the account page, in "Sekimas ir
/// laiškai" beside the switches; the choice is saved, survives a reload, and the API answers it. Screenshots at 1280 and 390 px
/// go to <c>LT2_E2E_RHYTHM_SHOTS</c> (or <c>output/email-rhythm/shots</c>). The member is deleted after the test (<see cref="E2ETest"/>).
/// </summary>
public sealed class EmailRhythmTests : E2ETest
{
    private static string Base => E2ESteps.Base;

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_RHYTHM_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "email-rhythm", "shots");

    [Fact]
    public async Task A_member_changes_how_often_emails_come_and_the_choice_stays()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Laiškų Skaitytoja");

        // Design 8.21, 8.24: on the follows screen, its own sheet after the switches.
        await page.GotoAsync($"{Base}/paskyra/sekimas#email-frequency");
        var immediate = page.Locator("#rhythm-immediate");
        var daily = page.Locator("#rhythm-daily");
        await Assertions.Expect(immediate).ToBeCheckedAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#rhythm-weekly")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#email-frequency")).ToContainTextAsync("Kartą per savaitę ir artėjant balsavimui");

        // Enabled once the island runs; before that its start replaces the prerendered markup.
        await Assertions.Expect(daily).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await page.Locator("#email-frequency").ScrollIntoViewIfNeededAsync();
        await E2ESteps.ShootBothAsync(page, Shots, "account-rhythm", page.Locator("#email-frequency"));

        await daily.CheckAsync();
        await Assertions.Expect(page.Locator("#email-frequency")).ToContainTextAsync("Išsaugota.", new() { Timeout = E2ESteps.Slow });
        var (status, body) = await E2ESteps.ApiAsync(page, "GET", "/api/v1/me/notices/frequency", null);
        status.ShouldBe(200, body);
        JsonDocument.Parse(body).RootElement.GetProperty("frequency").GetString().ShouldBe("daily");

        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#rhythm-daily")).ToBeCheckedAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#rhythm-immediate")).Not.ToBeCheckedAsync();

        // The switches beside it are untouched by the choice.
        await Assertions.Expect(page.Locator("#notice-planned")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#notice-voted")).ToBeCheckedAsync();
        member.ConsoleErrors.ShouldBeEmpty();
    }
}
