using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.10: the feedback balloon floats in the bottom corner of every page; the open panel lies over the page at a
/// readable width, inside the window, scrolling inside itself. Checked at 1280, 1024, 768 and 390 px, as a visitor and
/// as a member, with and without JavaScript; plus a staff reply that arrives in Mailpit.
/// </summary>
public sealed class FeedbackBalloonTests(AppealFlowTests.ModeratorFixture moderator) : IClassFixture<AppealFlowTests.ModeratorFixture>
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    private static readonly string MailpitUrl = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";

    /// <summary>At the end of the page, the closed button lies over no text or control (while scrolling it may pass over text).</summary>
    private static async Task NothingCoveredAtTheEndAsync(IPage page)
    {
        var covered = await page.EvaluateAsync<string[]>(
            """
            () => {
              window.scrollTo(0, document.documentElement.scrollHeight);
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
        covered.ShouldBeEmpty("at the end of the page");
    }

    /// <summary>What the page reports about the open panel (Playwright needs settable properties to fill it).</summary>
    private sealed class PanelBox
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double Width { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public double ScrollWidth { get; set; }
        public double ClientWidth { get; set; }
        public string[] Clipped { get; set; } = [];
    }

    /// <summary>
    /// The open panel lies fully inside the window, is at least 280 px wide, has no sideways overflow, and no line of its
    /// text sticks out (a block's scroll width beyond its client width, or any text box beyond the panel's sides).
    /// </summary>
    private static async Task PanelFitsAsync(IPage page, string where)
    {
        var box = await page.EvaluateAsync<PanelBox>(
            """
            () => {
              const panel = document.querySelector('.feedback__panel');
              const r = panel.getBoundingClientRect();
              const clipped = [];
              for (const el of panel.querySelectorAll('h2, p, label, a, button, span, code, li, textarea, input, div')) {
                if (el.getClientRects().length === 0) continue;
                const style = getComputedStyle(el);
                const name = el.tagName + ' ' + (el.id || el.textContent.trim().slice(0, 25));
                if (style.display !== 'inline' && el.clientWidth > 0 && el.scrollWidth > el.clientWidth + 1) clipped.push('overflows: ' + name);
                for (const line of el.getClientRects()) {
                  if (line.left < r.left - 0.5 || line.right > r.right + 0.5) { clipped.push('outside the panel: ' + name); break; }
                }
              }
              return {
                left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width,
                viewportWidth: document.documentElement.clientWidth, viewportHeight: window.innerHeight,
                scrollWidth: panel.scrollWidth, clientWidth: panel.clientWidth, clipped,
              };
            }
            """);
        box.Left.ShouldBeGreaterThanOrEqualTo(0, where);
        box.Top.ShouldBeGreaterThanOrEqualTo(0, where);
        box.Right.ShouldBeLessThanOrEqualTo(box.ViewportWidth, where);
        box.Bottom.ShouldBeLessThanOrEqualTo(box.ViewportHeight, where);
        box.Width.ShouldBeGreaterThanOrEqualTo(280, where);
        box.ScrollWidth.ShouldBeLessThanOrEqualTo(box.ClientWidth, $"{where}: no sideways scrolling in the panel");
        box.Clipped.ShouldBeEmpty(where);
    }

    /// <summary>The control can be brought into view inside the panel and is the topmost thing at its centre.</summary>
    private static async Task ReachableAsync(IPage page, ILocator control, string where)
    {
        await control.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(control).ToBeInViewportAsync();
        var onTop = await control.EvaluateAsync<bool>(
            """
            el => {
              const r = el.getBoundingClientRect();
              const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
              return top === el || el.contains(top);
            }
            """);
        onTop.ShouldBeTrue(where);
    }

    private static async Task ShotsAsync(IPage page, string name)
    {
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.ScreenshotAsync(new() { Path = Shot($"{name}-light.png"), Animations = ScreenshotAnimations.Disabled });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.ScreenshotAsync(new() { Path = Shot($"{name}-dark.png"), Animations = ScreenshotAnimations.Disabled });
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
    }

    /// <summary>The page has loaded everything it asks for (the WebAssembly runtime included) and has stopped moving focus.</summary>
    private static async Task SettledAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => setTimeout(r, 0)))");
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1024, 768)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
    public async Task A_visitor_opens_the_balloon_over_the_page_and_closes_it_with_escape_and_the_close_button(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new ViewportSize { Width = width, Height = height } });
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/seimas/XVP-0412", "/prisijungti" })
        {
            var where = $"{path} at {width}";
            await page.GotoAsync(Base + path);
            var summary = page.Locator("summary.feedback__button");
            await Assertions.Expect(summary).ToHaveAttributeAsync("aria-label", "Atsiliepimai");
            await Assertions.Expect(summary).ToHaveAttributeAsync("title", "Atsiliepimai");
            await NothingCoveredAtTheEndAsync(page);
            await ShotsAsync(page, $"balloon-closed{path.Replace('/', '-')}-{width}");

            // Opened from the keyboard: focus goes into the panel (the heading; a visitor has no form). Only once the page
            // has settled, as a person would: while WebAssembly starts, the toggle event and Blazor's focus on the
            // page's heading after loading both come late, and the latter takes the focus back.
            await SettledAsync(page);
            await page.EvaluateAsync("() => window.scrollTo(0, 0)");
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
            await Assertions.Expect(panel.Locator("#feedback-title")).ToBeFocusedAsync();
            await PanelFitsAsync(page, where);
            await ReachableAsync(page, panel.Locator("a[href='mailto:moderatoriai@lt2.localhost']"), where);
            await ShotsAsync(page, $"balloon-visitor{path.Replace('/', '-')}-{width}");

            // Escape closes and focus returns to the button.
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(panel).ToBeHiddenAsync();
            await Assertions.Expect(summary).ToBeFocusedAsync();

            // Opened with the mouse, closed with the panel's own close button.
            await summary.ClickAsync();
            await Assertions.Expect(panel).ToBeVisibleAsync();
            var close = panel.GetByRole(AriaRole.Button, new() { Name = "Uždaryti" });
            await ReachableAsync(page, close, where);
            await close.ClickAsync();
            await Assertions.Expect(panel).ToBeHiddenAsync();
            await Assertions.Expect(summary).ToBeFocusedAsync();
        }

        await Assertions.Expect(page.Locator("#cannot-sign-in")).ToContainTextAsync("Nepavyksta prisijungti?");
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1024, 768)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
    public async Task A_member_sends_feedback_from_the_balloon_with_and_without_javascript(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", width, height);
        var a = member.Page;
        await PasskeyTests.RegisterAsync(a, "Atsiliepianti Narė");

        // With JavaScript: focus goes to the text field; the panel fits; the send button can be reached.
        await a.GotoAsync($"{Base}/seimas/XVP-0412");
        await NothingCoveredAtTheEndAsync(a);
        await SettledAsync(a);
        await a.Locator("summary.feedback__button").ClickAsync();
        var panel = a.Locator(".feedback__panel");
        await Assertions.Expect(panel.Locator("#fb-text-panel")).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator("#fb-text-panel")).ToBeFocusedAsync();
        await Assertions.Expect(panel).ToContainTextAsync("/seimas/XVP-0412");
        await Assertions.Expect(panel).ToContainTextAsync("Nerašykite, kurią pusę pasirinkote");
        await PanelFitsAsync(a, $"member at {width}");
        await panel.Locator("#fb-text-panel").FillAsync($"Bandomasis atsiliepimas ({width} px, su JavaScript).");
        await panel.Locator("#fb-browser-panel").UncheckAsync();
        await ShotsAsync(a, $"balloon-member-{width}");
        var send = panel.GetByRole(AriaRole.Button, new() { Name = "Siųsti" });
        await ReachableAsync(a, send, $"member's send button at {width}");
        await ShotsAsync(a, $"balloon-member-send-{width}");
        await send.ClickAsync();
        await Assertions.Expect(a.Locator("#feedback-sent-panel")).ToHaveTextAsync("Ačiū! Atsiliepimą gavome.", new() { Timeout = 30_000 });
        member.ConsoleErrors.ShouldBeEmpty();

        // Without JavaScript, with the same session: open, send and close with the balloon's own button.
        var cookies = await a.Context.CookiesAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { JavaScriptEnabled = false, IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new ViewportSize { Width = width, Height = height } });
        await context.AddCookiesAsync(cookies.Select(c => new Cookie { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Secure = c.Secure, HttpOnly = c.HttpOnly, SameSite = c.SameSite, Expires = c.Expires }));
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Base}/metodika");
        await NothingCoveredAtTheEndAsync(page);
        var summary = page.Locator("summary.feedback__button");
        await summary.ClickAsync();
        var plainPanel = page.Locator(".feedback__panel");
        await Assertions.Expect(plainPanel.GetByRole(AriaRole.Button, new() { Name = "Uždaryti" })).ToBeHiddenAsync();
        await PanelFitsAsync(page, $"member without JavaScript at {width}");
        await page.Locator("#fb-text-panel").FillAsync($"Bandomasis atsiliepimas ({width} px, be JavaScript).");
        var plainSend = plainPanel.GetByRole(AriaRole.Button, new() { Name = "Siųsti" });
        await ReachableAsync(page, plainSend, $"send without JavaScript at {width}");
        await plainSend.ClickAsync();
        await Assertions.Expect(page.Locator("#feedback-sent-panel")).ToHaveTextAsync("Ačiū! Atsiliepimą gavome.");
        await PanelFitsAsync(page, $"thanks without JavaScript at {width}");
        await ShotsAsync(page, $"balloon-member-nojs-sent-{width}");
        await summary.ClickAsync();
        await Assertions.Expect(plainPanel).ToBeHiddenAsync();
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