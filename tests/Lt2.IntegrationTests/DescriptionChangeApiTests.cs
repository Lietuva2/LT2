using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.1 over HTTP (decided 2026-11-13): the initiator of an open appeal proposes a new description; the page keeps
/// the approved text until a moderator approves it; approved changes enter the public history.
/// </summary>
public sealed class DescriptionChangeApiTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const string NewText = "Įvedamas trečias pajamų mokesčio tarifas (32 %) metinėms pajamoms virš 60 VDU; patikslintas aprašymas.";

    private static readonly SemaphoreSlim ModeratorGate = new(1, 1);
    private static StaffMember? _moderator;

    private async Task<TestBrowser> ModeratorAsync()
    {
        await ModeratorGate.WaitAsync();
        try
        {
            _moderator ??= await StaffMembers.ActiveAsync(Factory, Database, "moderator");
            return _moderator.Browser;
        }
        finally
        {
            ModeratorGate.Release();
        }
    }

    private static Task<HttpResponseMessage> ProposeAsync(TestBrowser browser, string text = NewText) =>
        browser.PostAsync(AppealRoutes.DescriptionChangeOf(Bill), new EditAppealRequest { Description = text });

    private static async Task<QueueDescriptionChangeResponse> QueuedAsync(TestBrowser moderator, Guid appealId) =>
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).DescriptionChanges!.Single(d => d.AppealId == appealId);

    [Fact]
    public async Task A_proposed_description_waits_for_a_moderator_and_the_page_keeps_the_approved_text()
    {
        var moderator = await ModeratorAsync();
        var (initiator, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, Bill, side: null);

        using (var sent = await ProposeAsync(initiator))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync());
        }

        using var stranger = NewBrowser();
        (await stranger.AppealAsync(Bill)).Description.ShouldBe(AppealSteps.Description, "the approved text stays until a moderator approves");
        var mine = await initiator.MineAsync(Bill);
        mine.DescriptionChange!.Status.ShouldBe("pending");
        mine.DescriptionChange.Text.ShouldBe(NewText);

        var queued = await QueuedAsync(moderator, appeal.Id);
        queued.CurrentText.ShouldBe(AppealSteps.Description);
        queued.ProposedText.ShouldBe(NewText);
        queued.InitiatorName.ShouldBe("Rūta Mockutė");

        using var second = await ProposeAsync(initiator, NewText + " Dar kartą.");
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, "description-change.pending");
    }

    [Fact]
    public async Task Approving_puts_the_new_text_on_the_page_and_in_the_history_and_tells_the_initiator()
    {
        var moderator = await ModeratorAsync();
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, Bill, side: null);
        (await ProposeAsync(initiator)).Dispose();
        var queued = await QueuedAsync(moderator, appeal.Id);

        using (var approved = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ApproveDescription, queued.Id), new ModerationRequest()))
        {
            approved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await approved.Content.ReadAsStringAsync());
        }

        using var stranger = NewBrowser();
        (await stranger.AppealAsync(Bill)).Description.ShouldBe(NewText);
        var history = await stranger.GetJsonAsync<IReadOnlyList<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(Bill));
        history.ShouldContain(h => h.OldText == AppealSteps.Description && h.NewText == NewText);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' = 'description-approved'",
            ("to", email))).ShouldBe(1);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'moderation.description-change.approve' AND target = @t", ("t", "description-change:" + queued.Id))).ShouldBe(1);

        using var again = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ApproveDescription, queued.Id), new ModerationRequest());
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
    }

    [Fact]
    public async Task Returning_keeps_the_approved_text_tells_the_initiator_why_and_allows_a_new_proposal()
    {
        var moderator = await ModeratorAsync();
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, Bill, side: null);
        (await ProposeAsync(initiator)).Dispose();
        var queued = await QueuedAsync(moderator, appeal.Id);

        using (var noNote = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReturnDescription, queued.Id), new StatementRequest { Statement = " " }))
        {
            noNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a returned change needs a note");
        }

        using (var returned = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReturnDescription, queued.Id), new StatementRequest { Statement = "Aprašymas turi būti neutralus." }))
        {
            returned.StatusCode.ShouldBe(HttpStatusCode.NoContent, await returned.Content.ReadAsStringAsync());
        }

        (await initiator.AppealAsync(Bill)).Description.ShouldBe(AppealSteps.Description);
        var mine = await initiator.MineAsync(Bill);
        mine.DescriptionChange!.Status.ShouldBe("returned");
        mine.DescriptionChange.Note.ShouldBe("Aprašymas turi būti neutralus.");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' = 'description-returned'",
            ("to", email))).ShouldBe(1);

        using var next = await ProposeAsync(initiator, NewText + " Pataisyta.");
        next.StatusCode.ShouldBe(HttpStatusCode.NoContent, "a returned change does not block a new one");
    }

    [Fact]
    public async Task Only_the_initiator_proposes_and_never_on_a_closed_appeal_or_under_a_restriction()
    {
        var moderator = await ModeratorAsync();
        var (initiator, initiatorEmail) = await Factory.MemberAsync();
        var (other, _) = await Factory.MemberAsync();
        using var _i = initiator;
        using var _o = other;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, Bill, side: null);

        using (var notMine = await ProposeAsync(other))
        {
            await notMine.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "appeal.initiator-only");
        }

        using (var same = await ProposeAsync(initiator, AppealSteps.Description))
        {
            same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var restrict = await moderator.PostAsync(
                   AppealRoutes.With(AppealRoutes.RestrictAppealInitiator, appeal.Id), new RestrictionRequest { Statement = "Bandymas." }))
        {
            restrict.StatusCode.ShouldBe(HttpStatusCode.Created, await restrict.Content.ReadAsStringAsync());
        }

        using (var restricted = await ProposeAsync(initiator))
        {
            await restricted.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        await Database.ExecuteAsync("DELETE FROM appeals.writing_restriction WHERE user_id = @u", ("u", await Database.UserIdAsync(initiatorEmail)));
        await Database.ExecuteAsync("UPDATE appeals.appeal SET status = 'closed', closed_at = now() WHERE id = @id", ("id", appeal.Id));
        using var closed = await ProposeAsync(initiator);
        await closed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "appeal.not-open");
    }
}
