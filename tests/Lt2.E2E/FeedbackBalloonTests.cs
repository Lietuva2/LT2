using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.10: the feedback balloon on every page, at 1280 and 390 px, with and without JavaScript, as a visitor and as
/// a member; it never covers text or controls; and a staff reply that arrives in Mailpit.
/// </summary>
public sealed class FeedbackBalloonTests(AppealFlowTests.ModeratorFixture moderator) : IClassFixture<AppealFlowTests.ModeratorFixture>
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    private static readonly string MailpitUrl = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";

    /// <summary>Whether the balloon's button overlaps any visible text or control outside the balloon, at the top and the bottom of the page.</summary>
    private static async Task NothingCoveredAsync(IPage page)
    {
        foreach (var where in new[] { "0", "document.documentElement.scrollHeight" })
        {
            var covered = await page.EvaluateAsync<string[]>(
                $$"""
                () => {
                  window.scrollTo(0, {{where}});
                  const button = document.querySelector('.feedback__button').getBoundingClientRect();
                  const hit = [];
                  for (const el of document.querySelectorAll('a, button, input, textarea, select, label, p, h1, h2, h3, li, td, th, code, b, span, summary')) {
                    if (el.closest('.feedback')) continue;
                    const r = el.getBoundingClientRect();
                    if (r.width === 0 || r.height === 0) continue;
                    if (r.left < button.right && r.right > button.left && r.top < button.bottom && r.bottom > button.top) hit.push(el.tagName + ' ' + (el.id || el.className || el.textContent.trim().slice(0, 30)));
                  }
                  return hit;
                }
                """);
            covered.ShouldBeEmpty($"at scroll {where}");
        }
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_visitor_finds_the_balloon_on_the_appeal_and_sign_in_pages_with_the_contact_address_and_escape_closes_it(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new ViewportSize { Width = width, Height = height } });
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/seimas/XVP-0412/kreipimasis", "/prisijungti" })
        {
            await page.GotoAsync(Base + path);
            var button = page.GetByRole(AriaRole.Group).Locator("summary.feedback__button");
            var summary = page.Locator("summary.feedback__button");
            await Assertions.Expect(summary).ToHaveAttributeAsync("aria-label", "Atsiliepimai");
            await Assertions.Expect(summary).ToHaveAttributeAsync("title", "Atsiliepimai");
            await NothingCoveredAsync(page);

            await summary.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            var panel = page.Locator(".feedback__panel");
            await Assertions.Expect(panel).ToBeVisibleAsync();
            await Assertions.Expect(panel.GetByRole(AriaRole.Heading)).ToHaveTextAsync("Atsiliepimai Lietuva 2.0 komandai");
            await Assertions.Expect(panel).ToContainTextAsync("Nepavyksta prisijungti? Parašykite mums:");
            await Assertions.Expect(panel.Locator("a[href='mailto:moderatoriai@lt2.localhost']")).ToBeVisibleAsync();
            await Assertions.Expect(panel.Locator("a[href='https://github.com/Lietuva2/LT2/issues']")).ToBeVisibleAsync();
            await Assertions.Expect(panel).ToContainTextAsync("viešas ir rodomas su jūsų GitHub vardu");
            await Assertions.Expect(panel.Locator("form")).ToHaveCountAsync(0);
            await page.ScreenshotAsync(new() { Path = Shot($"balloon-visitor{path.Replace('/', '-')}-{width}.png"), FullPage = true });

            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(panel).ToBeHiddenAsync();
            button.ShouldNotBeNull();
        }

        await Assertions.Expect(page.Locator("#cannot-sign-in")).ToContainTextAsync("Nepavyksta prisijungti?");
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_member_sends_feedback_from_the_balloon_with_and_without_javascript(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", width, height);
        var a = member.Page;
        await PasskeyTests.RegisterAsync(a, "Atsiliepianti Narė");

        // With JavaScript.
        await a.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        await NothingCoveredAsync(a);
        await a.Locator("summary.feedback__button").ClickAsync();
        var panel = a.Locator(".feedback__panel");
        await Assertions.Expect(panel.Locator("#fb-text-panel")).ToBeVisibleAsync();
        await Assertions.Expect(panel).ToContainTextAsync("/seimas/XVP-0412/kreipimasis");
        await Assertions.Expect(panel).ToContainTextAsync("Nerašykite, kurią pusę pasirinkote");
        await panel.Locator("#fb-text-panel").FillAsync($"Bandomasis atsiliepimas ({width} px, su JavaScript).");
        await panel.Locator("#fb-browser-panel").UncheckAsync();
        await a.ScreenshotAsync(new() { Path = Shot($"balloon-member-{width}.png"), FullPage = true });
        await panel.GetByRole(AriaRole.Button, new() { Name = "Siųsti" }).ClickAsync();
        await Assertions.Expect(a.Locator("#feedback-sent-panel")).ToHaveTextAsync("Ačiū! Atsiliepimą gavome.", new() { Timeout = 30_000 });
        member.ConsoleErrors.ShouldBeEmpty();

        // Without JavaScript, with the same session.
        var cookies = await a.Context.CookiesAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { JavaScriptEnabled = false, IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new ViewportSize { Width = width, Height = height } });
        await context.AddCookiesAsync(cookies.Select(c => new Cookie { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Secure = c.Secure, HttpOnly = c.HttpOnly, SameSite = c.SameSite, Expires = c.Expires }));
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Base}/metodika");
        await NothingCoveredAsync(page);
        await page.Locator("summary.feedback__button").ClickAsync();
        await page.Locator("#fb-text-panel").FillAsync($"Bandomasis atsiliepimas ({width} px, be JavaScript).");
        await page.Locator(".feedback__panel").GetByRole(AriaRole.Button, new() { Name = "Siųsti" }).ClickAsync();
        await Assertions.Expect(page.Locator("#feedback-sent-panel")).ToHaveTextAsync("Ačiū! Atsiliepimą gavome.");
    }

    [Fact]
    public async Task A_staff_reply_to_a_linked_item_arrives_in_the_members_mailbox()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var mod = await moderator.PageAsync();
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var a = member.Page;
        var email = await PasskeyTests.RegisterAsync(a, "Laukianti Atsakymo");
        var text = $"Prašau atsakyti: kur rasti taisykles? ({Guid.NewGuid():N})";
        await a.GotoAsync($"{Base}/atsiliepimai");
        await a.Locator("#fb-text-page").FillAsync(text);
        await a.Locator("#fb-reply-page").CheckAsync();
        await a.Locator("#feedback-form-page").GetByRole(AriaRole.Button, new() { Name = "Siųsti" }).ClickAsync();
        await Assertions.Expect(a.Locator("#feedback-sent-page")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await mod.GotoAsync($"{Base}/moderavimas/atsiliepimai");
        var item = mod.Locator("article").Filter(new() { HasText = text });
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(item).ToContainTextAsync("Laukianti Atsakymo");
        (await item.InnerTextAsync()).ShouldNotContain(email);
        var reply = item.Locator("textarea[id^='fb-reply-']");
        await Assertions.Expect(reply).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await reply.FillAsync("Taisyklės – puslapyje „Moderavimo taisyklės“.");
        await item.GetByRole(AriaRole.Button, new() { Name = "Siųsti atsakymą" }).ClickAsync();
        await Assertions.Expect(mod.GetByText("Atsakymas išsiųstas.")).ToBeVisibleAsync();

        using var http = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        for (var attempt = 0; ; attempt++)
        {
            var search = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{email}\" subject:\"atsakymas\"")}");
            if (search.GetProperty("messages").GetArrayLength() > 0)
            {
                var id = search.GetProperty("messages")[0].GetProperty("ID").GetString();
                var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
                message.GetProperty("Text").GetString()!.ShouldContain("Moderavimo taisyklės");
                break;
            }

            attempt.ShouldBeLessThan(60, "the reply arrives in Mailpit");
            await Task.Delay(500);
        }
    }

    private static string Shot(string name)
    {
        Directory.CreateDirectory(Path.Combine("output", "feedback"));
        return Path.Combine("output", "feedback", name);
    }
}
