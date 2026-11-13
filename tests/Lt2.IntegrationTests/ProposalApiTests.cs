using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.1 over HTTP (decided 2026-11-13): several members may propose an appeal on a bill until a moderator publishes
/// one; publishing closes the others and tells their authors without naming anyone; the form's count is a number only.
/// </summary>
public sealed class ProposalApiTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const string OtherText = "Projektas keičia pajamų mokesčio tarifus didelėms pajamoms; pasiūlymas su kitu aprašymu.";

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

    private static async Task<AppealResponse> ProposeAsync(TestBrowser member, string? description = null)
    {
        using var opened = await member.OpenAppealAsync(Bill, description: description);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        return await opened.ReadAsync<AppealResponse>();
    }

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => Database.ScalarAsync<long>(sql, parameters);

    [Fact]
    public async Task Two_members_may_propose_on_one_bill_and_each_sees_only_their_own()
    {
        await Database.FreeBillAsync(Bill);
        var (first, _) = await Factory.MemberAsync("Rūta Mockutė");
        var (second, _) = await Factory.MemberAsync("Giedrė L.");
        using var _f = first;
        using var _s = second;
        using var stranger = NewBrowser();

        var mine = await ProposeAsync(first);
        var theirs = await ProposeAsync(second, OtherText);

        mine.Id.ShouldNotBe(theirs.Id);
        (await first.AppealAsync(Bill)).Id.ShouldBe(mine.Id);
        (await second.AppealAsync(Bill)).Description.ShouldBe(OtherText);
        (await first.AppealAsync(Bill)).Description.ShouldNotBe(OtherText, "the other author's text is never shown");
        using var nothing = await stranger.Client.GetAsync(AppealRoutes.AppealOf(Bill));
        await nothing.ShouldBeProblemAsync(HttpStatusCode.NotFound, "appeal.not-found");
    }

    [Fact]
    public async Task A_person_has_one_waiting_proposal_per_bill_and_a_second_try_is_refused()
    {
        await Database.FreeBillAsync(Bill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await ProposeAsync(member);

        using var again = await member.OpenAppealAsync(Bill, description: OtherText);
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "proposal.exists");
    }

    [Fact]
    public async Task The_count_of_waiting_proposals_is_public_and_holds_a_number_only()
    {
        await Database.FreeBillAsync(Bill);
        var (first, _) = await Factory.MemberAsync("Rūta Mockutė");
        var (second, _) = await Factory.MemberAsync("Giedrė L.");
        using var _f = first;
        using var _s = second;
        using var stranger = NewBrowser();
        (await stranger.GetJsonAsync<ProposalsResponse>(AppealRoutes.ProposalsOf(Bill))).Waiting.ShouldBe(0);

        await ProposeAsync(first);
        await ProposeAsync(second, OtherText);

        using var answer = await stranger.Client.GetAsync(AppealRoutes.ProposalsOf(Bill));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await answer.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["waiting"], "a number only: no names, no texts");
        json.RootElement.GetProperty("waiting").GetInt32().ShouldBe(2);
        body.ShouldNotContain("Mockutė");
        body.ShouldNotContain("Giedrė");

        // A member who proposed gets the same answer as a stranger: nothing in it is about the person asking.
        (await first.GetJsonAsync<ProposalsResponse>(AppealRoutes.ProposalsOf(Bill))).Waiting.ShouldBe(2);
        using var unknown = await stranger.Client.GetAsync(AppealRoutes.ProposalsOf("XVP-9999"));
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
    }

    [Fact]
    public async Task Publishing_one_proposal_closes_the_others_and_tells_their_authors_without_names()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = await ModeratorAsync();
        var (first, _) = await Factory.MemberAsync("Rūta Mockutė");
        var (second, secondEmail) = await Factory.MemberAsync("Giedrė L.");
        var (third, thirdEmail) = await Factory.MemberAsync("Tomas K.");
        using var _f = first;
        using var _s = second;
        using var _t = third;
        var published = await ProposeAsync(first);
        var closed = await ProposeAsync(second, OtherText);
        var returned = await ProposeAsync(third, OtherText + " Trečias.");
        using (var back = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReturnAppeal, returned.Id), new StatementRequest { Statement = "Patikslinkite." }))
        {
            back.StatusCode.ShouldBe(HttpStatusCode.NoContent, await back.Content.ReadAsStringAsync());
        }

        using (var publish = await moderator.PublishAsync(published.Id))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.NoContent, await publish.Content.ReadAsStringAsync());
        }

        foreach (var id in new[] { closed.Id, returned.Id })
        {
            (await Database.ScalarAsync<string>("SELECT status FROM appeals.appeal WHERE id = @id", ("id", id))).ShouldBe("superseded");
            (await CountAsync("SELECT count(*) FROM appeals.moderation_action WHERE target_id = @id AND action = 'supersede'", ("id", id))).ShouldBe(1);
            (await CountAsync("SELECT count(*) FROM audit.event WHERE action = 'moderation.appeal.supersede' AND target = @t", ("t", "appeal:" + id))).ShouldBe(1);
        }

        foreach (var email in new[] { secondEmail, thirdEmail })
        {
            var payload = await Database.ScalarAsync<string>(
                "SELECT payload::text FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' = 'appeal-superseded'",
                ("to", email)) ?? throw new InvalidOperationException("No superseded notice for " + email);
            using var json = JsonDocument.Parse(payload);
            json.RootElement.GetProperty("path").GetString().ShouldBe($"/seimas/{Bill}/kreipimasis");
            json.RootElement.GetProperty("statement").ValueKind.ShouldBe(JsonValueKind.Null);
            payload.ShouldNotContain("Mockutė", Case.Sensitive, "the email names nobody");
        }

        // The other authors now see the published appeal, and the count is empty.
        (await second.AppealAsync(Bill)).Id.ShouldBe(published.Id);
        (await third.AppealAsync(Bill)).Status.ShouldBe("open");
        (await second.MineAsync(Bill)).IsInitiator.ShouldBeFalse("the initiator of record is the published proposal's author");
        (await first.MineAsync(Bill)).IsInitiator.ShouldBeTrue();
        using var stranger = NewBrowser();
        (await stranger.GetJsonAsync<ProposalsResponse>(AppealRoutes.ProposalsOf(Bill))).Waiting.ShouldBe(0);

        // While it is published, nobody may propose another, its author included.
        foreach (var member in new[] { first, second })
        {
            using var refused = await member.OpenAppealAsync(Bill, description: OtherText);
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "appeal.exists");
        }

        // A closed proposal cannot be published or returned any more.
        using var late = await moderator.PublishAsync(closed.Id);
        await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
    }

    [Fact]
    public async Task One_published_appeal_per_bill_holds_when_two_proposals_are_published_at_once()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = await ModeratorAsync();
        var (first, _) = await Factory.MemberAsync();
        var (second, _) = await Factory.MemberAsync();
        using var _f = first;
        using var _s = second;
        var a = await ProposeAsync(first);
        var b = await ProposeAsync(second, OtherText);

        var answers = await Task.WhenAll(moderator.PublishAsync(a.Id), moderator.PublishAsync(b.Id));
        try
        {
            answers.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1, "exactly one is published");
            answers.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        (await CountAsync("SELECT count(*) FROM appeals.appeal WHERE bill_number = @b AND status IN ('open', 'closed')", ("b", Bill))).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM appeals.appeal WHERE bill_number = @b AND status IN ('pending', 'returned')", ("b", Bill))).ShouldBe(0);
    }

    [Fact]
    public async Task The_database_refuses_a_second_published_appeal_on_a_bill()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = await ModeratorAsync();
        var (first, _) = await Factory.MemberAsync();
        var (second, _) = await Factory.MemberAsync();
        using var _f = first;
        using var _s = second;
        var a = await ProposeAsync(first);
        var b = await ProposeAsync(second, OtherText);
        using (var publish = await moderator.PublishAsync(a.Id))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await Should.ThrowAsync<Npgsql.PostgresException>(() =>
            Database.ExecuteAsync("UPDATE appeals.appeal SET status = 'open' WHERE id = @id", ("id", b.Id)));
    }
}
