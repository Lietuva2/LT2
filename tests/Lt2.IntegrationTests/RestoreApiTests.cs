using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, 8.12.3 over HTTP: the moderators' list of hidden bill records and reasons, and "Atkurti", with a statement,
/// audited; an argument's author is told. There is one record per bill, so nothing on the bill blocks restoring it.
/// </summary>
public sealed class RestoreApiTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const string Statement = "Paslėpta per klaidą.";

    private static readonly SemaphoreSlim ModeratorGate = new(1, 1);

    private static async Task HideAsync(TestBrowser moderator, Guid appealId)
    {
        using var hidden = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideAppeal, appealId), new StatementRequest { Statement = "Netinkamas aprašymas." });
        hidden.StatusCode.ShouldBe(HttpStatusCode.NoContent, await hidden.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> RestoreAsync(TestBrowser moderator, Guid appealId, string statement = Statement) =>
        moderator.PostAsync(AppealRoutes.With(AppealRoutes.RestoreAppeal, appealId), new StatementRequest { Statement = statement });

    [Fact]
    public async Task The_hidden_list_says_who_hid_an_appeal_when_and_why_and_what_restoring_would_do()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (initiator, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        var appeal = await Database.PrepareBillAsync(initiator, moderator, Bill);
        await HideAsync(moderator, appeal.Id);

        var list = await moderator.GetJsonAsync<HiddenItemsResponse>(AppealRoutes.Hidden);
        var item = list.Appeals.Single(a => a.Id == appeal.Id);
        item.Bill.Number.ShouldBe(Bill);

        item.Statement.ShouldBe("Netinkamas aprašymas.");
        item.HiddenAt.ShouldNotBeNull();
        item.HiddenBy.ShouldBeNull("the test moderator has no display name; a named one is shown by name");
        item.RestoresAs.ShouldBe("open");
        item.Description.ShouldBe(AppealSteps.Description);
    }

    [Fact]
    public async Task Only_a_moderator_sees_the_hidden_list_or_restores()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var list = await member.Client.GetAsync(AppealRoutes.Hidden);
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var restore = await RestoreAsync(member, Guid.NewGuid());
        restore.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restoring_brings_the_record_back_open_with_its_sides_reasons_and_marks()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (initiator, initiatorEmail) = await Factory.MemberAsync("Rūta Mockutė");
        var (supporter, _) = await Factory.MemberAsync("Giedrė L.");
        using var _i = initiator;
        using var _s = supporter;
        var appeal = await Database.PrepareBillAsync(initiator, moderator, Bill);
        (await supporter.TakeSideAsync(Bill, "against")).Dispose();
        var reasonId = (await initiator.AppealAsync(Bill)).Reasons.Single().Id;
        (await supporter.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId), null)).Dispose();
        var before = await initiator.AppealAsync(Bill);
        await HideAsync(moderator, appeal.Id);

        using (var restored = await RestoreAsync(moderator, appeal.Id))
        {
            restored.StatusCode.ShouldBe(HttpStatusCode.NoContent, await restored.Content.ReadAsStringAsync());
        }

        using var stranger = NewBrowser();
        var after = await stranger.AppealAsync(Bill);
        after.Id.ShouldBe(appeal.Id);
        after.Status.ShouldBe("open");
        after.Counts.ShouldBe(before.Counts, "the sides are as they were");
        after.Reasons.Single().Endorsements.ShouldBe(1, "the reason and its mark are as they were");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' NOT LIKE 'reason-%'",
            ("to", initiatorEmail))).ShouldBe(0, "a record has no author to email");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'moderation.appeal.restore' AND target = @t", ("t", "appeal:" + appeal.Id))).ShouldBe(1);
        (await moderator.GetJsonAsync<HiddenItemsResponse>(AppealRoutes.Hidden)).Appeals.ShouldNotContain(a => a.Id == appeal.Id);
    }

    [Fact]
    public async Task Restoring_needs_a_statement()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.PrepareBillAsync(initiator, moderator, Bill, side: null);
        await HideAsync(moderator, appeal.Id);

        using var refused = await RestoreAsync(moderator, appeal.Id, " ");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_closed_record_comes_back_closed_and_an_open_one_is_not_restored()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (first, _) = await Factory.MemberAsync();
        using var _f = first;
        var record = await Database.PrepareBillAsync(first, moderator, Bill, side: null);
        using (var open = await RestoreAsync(moderator, record.Id))
        {
            await open.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
        }

        await Database.ExecuteAsync("UPDATE appeals.appeal SET closed_at = now() WHERE id = @id", ("id", record.Id));
        try
        {
            await HideAsync(moderator, record.Id);
            (await moderator.GetJsonAsync<HiddenItemsResponse>(AppealRoutes.Hidden)).Appeals.Single(a => a.Id == record.Id).RestoresAs.ShouldBe("closed");
            (await RestoreAsync(moderator, record.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Database.ScalarAsync<string>("SELECT status FROM appeals.appeal WHERE id = @id", ("id", record.Id))).ShouldBe("closed");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
    [Fact]
    public async Task A_hidden_reason_is_listed_and_restored_without_its_marks()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (initiator, initiatorEmail) = await Factory.MemberAsync("Rūta Mockutė");
        var (supporter, _) = await Factory.MemberAsync();
        using var _i = initiator;
        using var _s = supporter;
        await Database.PrepareBillAsync(initiator, moderator, Bill);
        (await supporter.TakeSideAsync(Bill, "against")).Dispose();
        var reasonId = (await initiator.AppealAsync(Bill)).Reasons.Single().Id;
        (await supporter.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId), null)).Dispose();
        using (var hide = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, reasonId), new StatementRequest { Statement = "Įžeidžia." }))
        {
            hide.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var listed = (await moderator.GetJsonAsync<HiddenItemsResponse>(AppealRoutes.Hidden)).Reasons.Single(r => r.Id == reasonId);
        listed.Statement.ShouldBe("Įžeidžia.");
        listed.AuthorName.ShouldBe("Rūta Mockutė");
        listed.Restorable.ShouldBeTrue();

        using (var restore = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.RestoreReason, reasonId), new StatementRequest { Statement = Statement }))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.NoContent, await restore.Content.ReadAsStringAsync());
        }

        var shown = (await initiator.AppealAsync(Bill)).Reasons.Single(r => r.Id == reasonId);
        shown.Endorsements.ShouldBe(0, "the hide deleted its marks, and they do not come back");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' = 'reason-restored'",
            ("to", initiatorEmail))).ShouldBe(1);

        using var again = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.RestoreReason, reasonId), new StatementRequest { Statement = Statement });
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
    }
}
