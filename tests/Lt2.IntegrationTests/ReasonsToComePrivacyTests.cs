using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.11, 8.12 privacy, non-negotiable: who follows what is shown to nobody (no page, API answer or staff screen),
/// there is no follower count, nothing combines follows with sides, the person's own follows leave only in their own
/// answers, and nothing tells who took part first on a bill.
/// </summary>
public sealed class ReasonsToComePrivacyTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly string[] PublicAnswers =
    [
        AgendaRoutes.Agenda,
        ParliamentRoutes.BillByNumber(AppealSteps.SecondBill),
        ParliamentRoutes.MpBySlug(ReasonsToComeSteps.Petraitis),
        AppealRoutes.RecordOf(AppealSteps.SecondBill),
        AppealRoutes.Appeals,
    ];

    /// <summary>
    /// Design 8.11.2 (owner, 2026-11-16): following changes no public answer but the one number of how many follow, on the
    /// followed thing's own answer (the MP's <c>followers</c>, the bill's own <c>/followers</c>); nothing names the follower.
    /// </summary>
    [Fact]
    public async Task Following_changes_no_public_answer_but_the_number_of_followers()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        using var visitor = NewBrowser();
        var before = new Dictionary<string, string>();
        foreach (var route in PublicAnswers)
        {
            before[route] = Followerless(route, await visitor.Client.GetStringAsync(route));
        }

        var billFollowers = (await visitor.GetJsonAsync<Lt2.Api.Contracts.Me.FollowersResponse>(FollowRoutes.BillFollowersOf(AppealSteps.SecondBill))).Followers;
        var (member, _) = await Factory.MemberAsync("Sekanti Pavyzdytė");
        using var _m = member;
        await member.FollowBillAsync(AppealSteps.SecondBill);
        await member.FollowMpAsync(ReasonsToComeSteps.Petraitis);
        foreach (var route in PublicAnswers)
        {
            var after = await visitor.Client.GetStringAsync(route);
            Followerless(route, after).ShouldBe(before[route], route);
            after.ShouldNotContain("Sekanti Pavyzdytė");
        }

        (await visitor.GetJsonAsync<Lt2.Api.Contracts.Me.FollowersResponse>(FollowRoutes.BillFollowersOf(AppealSteps.SecondBill))).Followers.ShouldBe(billFollowers + 1);
        await member.SendAsync(HttpMethod.Delete, FollowRoutes.MpOf(ReasonsToComeSteps.Petraitis));
    }

    /// <summary>
    /// The answer without what following may change: the "followers" number, and in the lists of bills their order. Followers
    /// count into a bill's activity (design 8.12.5), which orders <c>/projektai</c> and a day's bills on the agenda, so one more
    /// follower may move the followed bill up; what each row holds stays the same. The lists' rows are compared as a set.
    /// </summary>
    private static string Followerless(string route, string json)
    {
        var plain = Regex.Replace(json, "\"followers\":\\d+", "\"followers\":_");
        return route is AppealRoutes.Appeals or AgendaRoutes.Agenda ? RowsInAnyOrder(JsonNode.Parse(plain))?.ToJsonString() ?? plain : plain;
    }

    /// <summary>The same JSON with every array of objects sorted by its elements' own text, so only the set of rows counts.</summary>
    private static JsonNode? RowsInAnyOrder(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var name in o.Select(p => p.Key).ToList())
                {
                    o[name] = RowsInAnyOrder(o[name]?.DeepClone());
                }

                return o;
            case JsonArray a when a.All(e => e is JsonObject):
                return new JsonArray(
                    a.Select(e => RowsInAnyOrder(e!.DeepClone())!).OrderBy(e => e.ToJsonString(), StringComparer.Ordinal).ToArray());
            default:
                return node;
        }
    }

    [Fact]
    public async Task Nobody_sees_another_persons_follows_or_requests()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (follower, followerEmail) = await Factory.MemberAsync("Ona Jonaitienė");
        using var _f = follower;
        await follower.FollowBillAsync(AppealSteps.SecondBill);
        await follower.FollowMpAsync(ReasonsToComeSteps.Petraitis);
        (await follower.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        var theirs = await other.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine);
        theirs.Bills.ShouldBeEmpty();
        theirs.Mps.ShouldBeEmpty();

        // The pages others see say nothing about the follower: no "Nebesekti", no "Jūs prašote", nobody first.
        var bill = WebUtility.HtmlDecode(await other.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
        bill.ShouldNotContain("Nebesekti");
        bill.ShouldNotContain("Jūs prašote");
        bill.ShouldNotContain("Ona Jonaitienė");

        // Nor any staff screen: the queue, the bill's moderation state, the hidden list and the restrictions.
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(3));
        try
        {
            var followerId = (await Database.UserIdAsync(followerEmail)).ToString();
            foreach (var route in new[] { AppealRoutes.ModerationBillOf(AppealSteps.SecondBill), AppealRoutes.ModerationQueue, AppealRoutes.Hidden, AppealRoutes.Restrictions })
            {
                var json = await moderator.Browser.Client.GetStringAsync(route);
                json.ShouldNotContain(followerEmail, Case.Insensitive, route);
                json.ShouldNotContain(followerId, Case.Insensitive, route);
                json.ShouldNotContain("Ona Jonaitienė", Case.Sensitive, route);
            }
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task Nothing_public_tells_who_took_part_first_on_a_bill()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (first, firstEmail) = await Factory.MemberAsync("Pirmoji Dalyvė");
        using var _f = first;
        (await first.TakeSideAsync(AppealSteps.SecondBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (second, _) = await Factory.MemberAsync("Antrasis Dalyvis");
        using var _s = second;
        (await second.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var visitor = NewBrowser();
        var firstId = (await Database.UserIdAsync(firstEmail)).ToString();
        foreach (var route in new[] { AppealRoutes.RecordOf(AppealSteps.SecondBill), AppealRoutes.Appeals, AgendaRoutes.Agenda, "/seimas/" + AppealSteps.SecondBill, "/projektai" })
        {
            var body = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync(route));
            body.ShouldNotContain("Pirmoji Dalyvė", Case.Sensitive, route);
            body.ShouldNotContain(firstId, Case.Insensitive, route);
            body.ShouldNotContain("initiator", Case.Insensitive, route);
            body.ShouldNotContain("atidarė", Case.Insensitive, route);
        }

        // The record itself keeps no trace of who caused it: it has no column for a person.
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'appeals' AND table_name = 'bill_record' AND column_name LIKE '%user%'"))
            .ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target LIKE 'record:%' AND actor_user_id = @u", ("u", Guid.Parse(firstId)))).ShouldBe(0);
    }
    [Fact]
    public async Task A_side_never_changes_what_the_follow_answer_holds()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, side: null);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await member.FollowBillAsync(AppealSteps.ThirdBill);
        var before = await member.Client.GetStringAsync(FollowRoutes.Mine);
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.Client.GetStringAsync(FollowRoutes.Mine)).ShouldBe(before);
        before.ShouldNotContain("\"side\"");
        before.ShouldNotContain("\"position\"");

        // And the invitation answer the share line uses carries nothing about the bill or a side.
        var invitation = await member.Client.GetStringAsync(ApiRoutes.Me.Invitation);
        invitation.ShouldNotContain(AppealSteps.ThirdBill);
        invitation.ShouldNotContain("against");
        invitation.ShouldNotContain("\"side\"");
        invitation.ShouldNotContain("tikslas");
    }
}
