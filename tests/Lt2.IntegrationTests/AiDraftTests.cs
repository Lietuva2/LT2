using System.Collections.Concurrent;
using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lt2.IntegrationTests;

/// <summary>A configured AI assistant that never leaves the process: it records what it was given and answers a fixed draft.</summary>
public sealed class FakeAiAssistant : IAiAssistant
{
    public const string Draft = "Projektas įveda trečią pajamų mokesčio tarifą – 32 % metinėms pajamoms, viršijančioms 60 vidutinių darbo užmokesčių.";

    public ConcurrentQueue<DraftSource> Asked { get; } = new();

    public bool IsConfigured => true;

    public Task<Result<string>> DraftDescriptionAsync(DraftSource request, CancellationToken cancellationToken)
    {
        Asked.Enqueue(request);
        return Task.FromResult<Result<string>>(Draft);
    }
}

/// <summary>The web application with <see cref="FakeAiAssistant"/> in place of the "not configured" one.</summary>
public sealed class AiFactory(DatabaseFixture database) : Lt2Factory(database)
{
    public FakeAiAssistant Assistant { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiAssistant>();
            services.AddSingleton<IAiAssistant>(Assistant);
        });
    }
}

/// <summary>
/// Design 8.11.4 over HTTP: the AI draft of a description, only in a moderator's hands. With no provider configured
/// nothing offers a draft and every other path works; with one (a fake here: no test calls a real provider) the draft fills
/// the description, is audited without its text, is limited to 20 an hour, and the bill says "Sukurta DI · taisė žmonės".
/// </summary>
public sealed class AiDraftTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Source = "Aiškinamasis raštas. Projektu siūloma nustatyti trečią 32 % gyventojų pajamų mokesčio tarifą metinėms pajamoms, viršijančioms 60 VDU.";

    private static readonly Lock FactoryGate = new();
    private static AiFactory? _ai;

    /// <summary>One for the whole run, like <see cref="ApiTest.Factory"/>: a host per test was slow. The tests count what the
    /// fakes were asked from where they started and clear what they set.</summary>
    private AiFactory Ai
    {
        get
        {
            lock (FactoryGate)
            {
                return _ai ??= new AiFactory(Database);
            }
        }
    }

    [Fact]
    public async Task Without_a_provider_nothing_offers_a_draft_and_a_moderator_still_writes_the_description_of_any_active_bill()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        (await moderator.Browser.GetJsonAsync<AiStatusResponse>(AppealRoutes.Ai)).DraftAvailable.ShouldBeFalse();
        using (var refused = await moderator.Browser.PostAsync(AppealRoutes.DescriptionDraftOf(AppealSteps.SecondBill), new DescriptionDraftRequest { SourceText = Source }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "draft.not-configured");
        }

        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var page = WebUtility.HtmlDecode(await moderator.Browser.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
        page.ShouldContain("id=\"moderation-panel\"", Case.Sensitive, "the moderator's controls are on any active bill's page");
        page.ShouldContain("Rašyti aprašymą");
        page.ShouldNotContain("juodraš", Case.Insensitive);
        page.ShouldNotContain("Šaltinio tekstas");
        page.ShouldNotContain(" DI ");

        // Claiming a draft that was never made is refused; without the claim the moderator writes the description.
        using (var claimed = await moderator.Browser.SendAsync(
            HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill),
            new ModeratorDescriptionRequest { Description = AppealSteps.Description, Statement = "Bandymas.", FromDraft = true }))
        {
            await claimed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "draft.none");
        }

        using (var written = await moderator.Browser.SendAsync(
            HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill), new ModeratorDescriptionRequest { Description = AppealSteps.Description, Statement = "Bandymas." }))
        {
            written.StatusCode.ShouldBe(HttpStatusCode.NoContent, await written.Content.ReadAsStringAsync());
        }

        // A visitor never sees the controls; the page shows the moderator's label.
        using var visitor = NewBrowser();
        var visible = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
        visible.ShouldNotContain("id=\"moderation-panel\"");
        visible.ShouldContain("Parašė moderatorius");
        using (var byVisitor = await visitor.Client.GetAsync(AppealRoutes.Ai))
        {
            byVisitor.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task With_a_provider_the_draft_fills_the_description_is_audited_without_text_and_the_bill_is_labelled()
    {
        using var moderator = await StaffMembers.ActiveAsync(Ai, Database, "moderator");
        var browser = moderator.Browser;
        (await browser.GetJsonAsync<AiStatusResponse>(AppealRoutes.Ai)).DraftAvailable.ShouldBeTrue();
        await Database.FreeBillAsync(AppealSteps.SecondBill);

        using (var empty = await browser.PostAsync(AppealRoutes.DescriptionDraftOf(AppealSteps.SecondBill), new DescriptionDraftRequest { SourceText = " " }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var tooLong = await browser.PostAsync(AppealRoutes.DescriptionDraftOf(AppealSteps.SecondBill), new DescriptionDraftRequest { SourceText = new string('a', 20_001) }))
        {
            tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var drafted = await browser.PostAsync(AppealRoutes.DescriptionDraftOf(AppealSteps.SecondBill), new DescriptionDraftRequest { SourceText = Source });
        drafted.StatusCode.ShouldBe(HttpStatusCode.OK, await drafted.Content.ReadAsStringAsync());
        (await drafted.ReadAsync<DescriptionDraftResponse>()).Draft.ShouldBe(FakeAiAssistant.Draft);
        var sent = Ai.Assistant.Asked.Last();
        sent.ShouldBe(new DraftSource(AppealSteps.SecondBill, "Vietinių rinkliavų įstatymo pataisa", Source));

        // Audited: who, which bill, when; never the source or the draft.
        var userId = await Database.UserIdAsync(moderator.Email);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'moderation.draft.requested' AND target = @t AND actor_user_id = @u",
            ("t", "bill:" + AppealSteps.SecondBill), ("u", userId))).ShouldBe(1);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'moderation.draft.requested' AND (coalesce(details::text, '') LIKE '%rinkliav%' OR coalesce(details::text, '') LIKE '%Aiškinamasis%')")).ShouldBe(0);

        using (var saved = await browser.SendAsync(
            HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill),
            new ModeratorDescriptionRequest { Description = FakeAiAssistant.Draft + " Pataisyta.", Statement = "Projektas darbotvarkėje.", FromDraft = true }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        }

        using var visitor = Ai.NewBrowser();
        (await visitor.AppealAsync(AppealSteps.SecondBill)).DescriptionSource.ShouldBe("ai-draft");
        WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill)).ShouldContain("Sukurta DI · taisė žmonės");

        // A later correction without a draft keeps the label for the description's whole life.
        using (var corrected = await browser.SendAsync(
            HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill),
            new ModeratorDescriptionRequest { Description = FakeAiAssistant.Draft, Statement = "Grąžinu juodraščio žodžius." }))
        {
            corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await visitor.AppealAsync(AppealSteps.SecondBill)).DescriptionSource.ShouldBe("ai-draft");
    }

    [Fact]
    public async Task A_moderator_may_ask_for_twenty_drafts_an_hour()
    {
        using var moderator = await StaffMembers.ActiveAsync(Ai, Database, "moderator");
        var userId = await Database.UserIdAsync(moderator.Email);
        await Database.ExecuteAsync(
            "INSERT INTO appeals.ai_draft_request (id, moderator_user_id, bill_number, requested_at) SELECT gen_random_uuid(), @u, 'XVP-0618', now() - interval '10 minutes' FROM generate_series(1, 20)",
            ("u", userId));
        using var refused = await moderator.Browser.PostAsync(AppealRoutes.DescriptionDraftOf(AppealSteps.ThirdBill), new DescriptionDraftRequest { SourceText = Source });
        await refused.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "draft.too-many");
    }
}
