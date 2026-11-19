using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.23 against the running stack, one test per kind of target: a member writes a comment in "Aptarimas" on the bill's
/// page, another agrees (the first "Sutinku" asks that the name will be visible) and reports it, a moderator hides it from the
/// bill's page with "Moderuoti", and a visitor without JavaScript reads the thread. Under a statement the organisation's
/// representative writes as the organisation; under a response the MP's account writes as the MP. At 1280 and 390 px; screenshots
/// in <c>output/comments</c>. The accounts are deleted by <see cref="E2ETest"/> (which deletes their own comments and marks); the
/// moderators' actions on the comments, the organisation and the MP's response are removed in a <c>finally</c>.
/// </summary>
public sealed partial class CommentTests(AppealFlowTests.ModeratorFixture moderator) : E2ETest, IClassFixture<AppealFlowTests.ModeratorFixture>
{
    private const string Bill = "XVP-0412";
    private const string ResponseBill = "XVP-0618";
    private const string Official = "pavyzdys.rasa.ievaite@lrs.lt";
    private const string MpName = "Rasa Ievaitė";
    private const long MpPerson = 99000014;

    private static string Base => E2ESteps.Base;

    private static string Shots => Path.Combine("output", "comments");

    [Fact]
    public async Task A_comment_under_an_argument_is_written_agreed_reported_and_hidden_on_the_bills_page()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var writer = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var reader = await PasskeyBrowser.StartAsync("native", 390, 844);
        var comment = Guid.Empty;
        try
        {
            await PasskeyTests.RegisterAsync(writer.Page, "Komentuojanti Narė");
            await PasskeyTests.RegisterAsync(reader.Page, "Sutinkantis Narys");
            var reason = await FirstReasonAsync(writer.Page);
            comment = await WriteAsync(writer.Page, reason, "Argumentui trūksta skaičių: kiek tai kainuotų? (E2E)");
            await E2ESteps.ShootBothAsync(writer.Page, Shots, "reason-written", writer.Page.Locator($"#aptarimas-panel-{reason:N}"));

            // Owner, 2026-11-17: at 1280 an open "Aptarimas" stays in its own side's column, which is wide since the arguments run
            // at the page's full width; the two sides stay side by side (the measure is the share of the arguments' area).
            var box = await writer.Page.Locator($"#aptarimas-panel-{reason:N}").BoundingBoxAsync();
            // The arguments' pair is the one holding this discussion (the organisations' statements use the same layout).
            var pair = await writer.Page.Locator(".pair--reasons").Filter(new() { Has = writer.Page.Locator($"#aptarimas-panel-{reason:N}") }).BoundingBoxAsync();
            box.ShouldNotBeNull().Width.ShouldBeLessThan(pair.ShouldNotBeNull().Width * 0.55f, "the sides stay side by side while a discussion is open");
            box.Width.ShouldBeGreaterThan(400, "a side's column has room for the discussion");

            await AgreeAsync(reader.Page, reason, comment);
            await E2ESteps.NoSidewaysScrollAsync(reader.Page, "the bill's page with a comment at 390 px");
            var (reported, reportBody) = await E2ESteps.ApiAsync(reader.Page, "POST", "/api/v1/reports", new { targetKind = "comment", targetId = comment, ground = "other", text = "Bandymas (E2E)." });
            reported.ShouldBe(204, reportBody);

            await HideOnPageAsync(comment);
            await using var visitor = await VisitorAsync();
            await visitor.Page.GotoAsync($"{Base}/seimas/{Bill}");
            (await visitor.Page.ContentAsync()).ShouldNotContain("Argumentui trūksta skaičių: kiek tai kainuotų? (E2E)");
        }
        finally
        {
            await ForgetModerationAsync(comment);
        }
    }

    /// <summary>
    /// Owner, 2026-11-19 ("Rodyti daugiau" doesn't work on phone nor desktop): with more than ten comments, once the island runs,
    /// "Rodyti daugiau komentarų" reads the next twenty in place on a phone: no navigation, no reload, the focus on the first one
    /// added. Until then the running island let enhanced navigation fetch the grown page and kept its own ten.
    /// </summary>
    [Fact]
    public async Task Rodyti_daugiau_komentaru_reads_the_next_twenty_in_place_on_a_phone()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var writer = await PasskeyBrowser.StartAsync("native", 390, 844);
        var page = writer.Page;
        await PasskeyTests.RegisterAsync(page, "Daug Komentuojanti Narė");
        var reason = await FirstReasonAsync(page);
        for (var i = 1; i <= 11; i++)
        {
            var (status, body) = await E2ESteps.ApiAsync(page, "POST", $"/api/v1/comments/reason/{reason}", new { text = $"Puslapiavimo komentaras nr. {i} (E2E)." });
            status.ShouldBe(201, body);
        }

        await page.GotoAsync($"{Base}/seimas/{Bill}");
        await page.Locator($"#aptarimas-summary-{reason:N}").ClickAsync();
        var more = page.Locator($"a#comments-more-{reason:N}");
        await Assertions.Expect(more).ToHaveAttributeAsync("data-enhance-nav", "false", new() { Timeout = E2ESteps.Slow });
        var items = page.Locator($"#comments-{reason:N} > li");
        await Assertions.Expect(items).ToHaveCountAsync(10);
        var address = page.Url;
        await page.EvaluateAsync("() => window.__notReloaded = true");

        await more.ClickAsync();

        await Assertions.Expect(items).Not.ToHaveCountAsync(10, new() { Timeout = E2ESteps.Slow });
        (await items.CountAsync()).ShouldBeGreaterThan(10);
        page.Url.ShouldBe(address, "read in place, the address unchanged");
        (await page.EvaluateAsync<bool>("() => window.__notReloaded === true")).ShouldBeTrue("no reload");
        (await page.EvaluateAsync<string>("() => document.activeElement.id")).ShouldBe(await items.Nth(10).GetAttributeAsync("id"), "the focus on the first one added");
        await E2ESteps.ShootBothAsync(page, Shots, "comments-more-phone", page.Locator($"#aptarimas-panel-{reason:N}"));
    }

    [Fact]
    public async Task A_representative_comments_under_its_statement_as_the_organisation_and_a_member_as_a_person()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var representative = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var r = representative.Page;
        await PasskeyTests.RegisterAsync(r, "Atstovaujanti Narė");
        await PasskeyTests.RegisterAsync(member.Page, "Klausiantis Narys");
        var (created, createdBody) = await E2ESteps.ApiAsync(r, "POST", "/api/v1/organisations",
            new { name = $"Aptarimo bandymo draugija {DateTime.UtcNow:HHmmssfff} {OrganisationFlowTests.Marker}", description = "Išgalvota organizacija (E2E bandymas)." });
        created.ShouldBe(201, createdBody);
        var slug = JsonDocument.Parse(createdBody).RootElement.GetProperty("slug").GetString()!;
        var ids = new List<Guid>();
        try
        {
            var (saved, savedBody) = await E2ESteps.ApiAsync(r, "PUT", $"/api/v1/bills/{Bill}/organisations/{slug}/statement",
                new { stance = "remarks", text = "Draugija pateikia pastabų dėl pataisos (E2E bandymas)." });
            saved.ShouldBe(200, savedBody);
            var statement = JsonDocument.Parse(savedBody).RootElement.GetProperty("id").GetGuid();

            ids.Add(await WriteAsync(member.Page, statement, "Kokių pastabų turi draugija? (E2E)"));
            ids.Add(await WriteAsync(r, statement, "Pastabos dėl pereinamojo laikotarpio. (E2E)", expectVoice: "organizacijos vardu"));
            var thread = r.Locator($"#aptarimas-panel-{statement:N}");
            await Assertions.Expect(thread.Locator($"#komentaras-{ids[1]:N} .tag")).ToHaveTextAsync("Organizacija");
            await Assertions.Expect(thread.Locator($"#komentaras-{ids[0]:N}")).ToContainTextAsync("Klausiantis Narys");
            (await thread.InnerTextAsync()).ShouldNotContain("Atstovaujanti Narė", Case.Sensitive, "a representative's name is never public");
            await E2ESteps.ShootBothAsync(r, Shots, "statement", thread);
            await AgreeAsync(member.Page, statement, ids[1]);
        }
        finally
        {
            foreach (var id in ids)
            {
                await ForgetModerationAsync(id);
            }

            await E2ESteps.CloseOrganisationAsync(r, slug);
        }
    }

    [Fact]
    public async Task The_mp_comments_under_its_own_response_as_the_mp_and_a_member_agrees()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await RemoveResponseAsync();
        await using var mp = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var member = await PasskeyBrowser.StartAsync("native", 390, 844);
        var page = mp.Page;
        try
        {
            await PasskeyTests.RegisterAsync(member.Page, "Atsakymą Skaitanti");

            // Step one's take-over, as MpResponseTests does it: the official address, the link from Mailpit, the passkey.
            await page.GotoAsync(Base + "/prisijungti?registruotis=true");
            await E2ESteps.RegisterHereAsync(page, Official, MpName);
            await page.WaitForURLAsync(new Regex("/paskyra#po-registracijos$"), new() { Timeout = E2ESteps.Slow });
            await page.GotoAsync(Base + "/paskyra/dalyvavimas");
            var claim = page.Locator("#mp-claim");
            await Assertions.Expect(claim).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await claim.ClickAsync();
            var mail = await Mailpit.MailAsync(Official, "Seimo nario profilio perėmimas");
            await page.GotoAsync(TakeoverLink().Match(mail).Value);
            var submit = page.Locator("#takeover-submit");
            await Assertions.Expect(submit).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await submit.ClickAsync();
            await page.WaitForURLAsync(new Regex("/paskyra/dalyvavimas$"), new() { Timeout = E2ESteps.Slow });

            var (responded, respondedBody) = await E2ESteps.ApiAsync(page, "PUT", $"/api/v1/bills/{ResponseBill}/responses/mine", new { stance = "for", text = "Balsuosiu už (E2E)." });
            responded.ShouldBe(200, respondedBody);
            var response = JsonDocument.Parse(respondedBody).RootElement.GetProperty("id").GetGuid();

            var question = await WriteAsync(member.Page, response, "Kada bus balsavimas? (E2E)", bill: ResponseBill);
            var answer = await WriteAsync(page, response, "Planuojama kitą savaitę. (E2E)", expectVoice: "Seimo nario vardu", bill: ResponseBill);
            var thread = page.Locator($"#aptarimas-panel-{response:N}");
            await Assertions.Expect(thread.Locator($"#komentaras-{answer:N}")).ToContainTextAsync(MpName);
            await Assertions.Expect(thread.Locator($"#komentaras-{answer:N}")).ToContainTextAsync("Seimo narys arba jo biuras");
            await AgreeAsync(member.Page, response, answer, ResponseBill);
            await E2ESteps.ShootBothAsync(member.Page, Shots, "response", member.Page.Locator($"#aptarimas-panel-{response:N}"));
            _ = question;
        }
        finally
        {
            await RemoveResponseAsync();
        }
    }

    /// <summary>The bill's first published argument, from the public answer.</summary>
    private static async Task<Guid> FirstReasonAsync(IPage page)
    {
        var (status, body) = await E2ESteps.ApiAsync(page, "GET", $"/api/v1/bills/{Bill}/record");
        status.ShouldBe(200, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("reasons")[0].GetProperty("id").GetGuid();
    }

    /// <summary>Opens the target's "Aptarimas" on the bill's page, writes the comment and returns its id.</summary>
    private static async Task<Guid> WriteAsync(IPage page, Guid target, string text, string expectVoice = "su jūsų vardu", string bill = Bill)
    {
        await page.GotoAsync($"{Base}/seimas/{bill}");
        var thread = page.Locator($"#aptarimas-panel-{target:N}");
        await page.Locator($"#aptarimas-summary-{target:N}").ClickAsync();
        var write = page.Locator($"#comments-write-{target:N}");
        await Assertions.Expect(write).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await write.ClickAsync();
        await Assertions.Expect(page.Locator($"#comment-voice-{target:N}")).ToContainTextAsync(expectVoice);
        await page.Locator($"#comment-text-{target:N}").FillAsync(text);
        await page.Locator($"#comment-submit-{target:N}").ClickAsync();
        await Assertions.Expect(thread).ToContainTextAsync(text, new() { Timeout = E2ESteps.Slow });
        var id = await thread.Locator("li.comment", new() { HasText = text }).GetAttributeAsync("id");
        return Guid.ParseExact(id!["komentaras-".Length..], "N");
    }

    /// <summary>The first "Sutinku" anywhere asks first; after confirming, the name stands beside the comment.</summary>
    private static async Task AgreeAsync(IPage page, Guid target, Guid comment, string bill = Bill)
    {
        await page.GotoAsync($"{Base}/seimas/{bill}");
        await page.Locator($"#aptarimas-summary-{target:N}").ClickAsync();
        var agree = page.Locator($"#komentaras-{comment:N}-agree");
        await Assertions.Expect(agree).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await agree.ClickAsync();
        await Assertions.Expect(page.Locator($"#komentaras-{comment:N}-confirm")).ToContainTextAsync("Jūsų vardas bus matomas prie šio komentaro.");
        await page.Locator($"#komentaras-{comment:N}-confirm-yes").ClickAsync();
        await Assertions.Expect(page.Locator($"#komentaras-{comment:N}-agreed")).ToContainTextAsync("Sutinka:", new() { Timeout = E2ESteps.Slow });
    }

    /// <summary>The moderator hides the comment from the bill's page, with "Moderuoti" (design 8.23.8).</summary>
    private async Task HideOnPageAsync(Guid comment)
    {
        var mod = await moderator.PageAsync();
        await mod.GotoAsync($"{Base}/seimas/{Bill}");
        // Design 8.16.1: the discussion opens from its summary in the argument's action row; "Moderuoti" is in the comment's "⋯".
        await mod.Locator($"#komentaras-{comment:N}").Locator("xpath=ancestor::div[contains(@class,'discuss__panel')]/preceding-sibling::details[1]/summary").ClickAsync();
        // "Moderuoti" comes with the moderators' answer once the island runs; open "⋯" only then.
        var moderate = mod.Locator($"#komentaras-{comment:N}-moderate");
        await Assertions.Expect(moderate).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await mod.Locator($"#komentaras-{comment:N}-more-actions").ClickAsync();
        await moderate.ClickAsync();
        // Design 8.3 (owner, 2026-11-17): "Paslėpti" is in the moderator's "Kiti veiksmai".
        await E2ESteps.ChooseAsync(mod.Locator($"#comment-action-hide-page-{comment:N}"));
        await mod.Locator($"#comment-statement-page-{comment:N}").FillAsync("Bandymas: komentaras paslepiamas (E2E).");
        await mod.Locator($"#comment-action-submit-page-{comment:N}").ClickAsync();
        await Assertions.Expect(mod.Locator($"#komentaras-{comment:N}")).ToHaveCountAsync(0, new() { Timeout = E2ESteps.Slow });
    }

    /// <summary>What the product keeps of a moderator's work on a comment: removed so the stack is as before.</summary>
    private static async Task ForgetModerationAsync(Guid comment)
    {
        if (comment == Guid.Empty)
        {
            return;
        }

        await E2ESteps.SqlAsync($"delete from appeals.moderation_action where target_kind = 'comment' and target_id = '{comment}'; delete from appeals.report where target_kind = 'comment' and target_id = '{comment}'; delete from appeals.comment where id = '{comment}'");
    }

    /// <summary>The MP's response and everything under it (its comments, versions, reports); no request removes a response.</summary>
    private static Task<string> RemoveResponseAsync() =>
        E2ESteps.SqlAsync(
            $"""
            delete from appeals.comment where target_kind = 'response' and target_id in (select id from appeals.mp_response where seimas_person_id = {MpPerson});
            delete from appeals.revision where entity_kind = 'response' and entity_id in (select id from appeals.mp_response where seimas_person_id = {MpPerson});
            delete from appeals.mp_response where seimas_person_id = {MpPerson};
            """);

    private static async Task<Visitor> VisitorAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        var context = await browser.NewContextAsync(new() { JavaScriptEnabled = false, IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        return new Visitor(playwright, browser, await context.NewPageAsync());
    }

    [GeneratedRegex(@"https?://[^\s]+/seimo-narys/perimti#[^\s]+")]
    private static partial Regex TakeoverLink();

    private sealed record Visitor(IPlaywright Playwright, IBrowser Browser, IPage Page) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            Playwright.Dispose();
        }
    }
}
