using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Client.Appeals;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3 (owner, 2026-11-25): the moderators' page is tabs. <c>GET /api/v1/moderation/counts</c> says how many items
/// each list holds, numbers only, the same totals the lists state; the queue's two lists are read one at a time; the page
/// opens on the tab asked for, else the first work tab with something waiting.
/// </summary>
public sealed class ModerationTabsTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Only_an_active_moderator_in_a_passkey_session_reads_the_counts()
    {
        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(AppealRoutes.ModerationCounts)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using (var refused = await member.Client.GetAsync(AppealRoutes.ModerationCounts))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        (await (await ModeratorAsync()).Browser.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_counts_are_the_totals_the_lists_state_and_follow_an_item_in_and_out()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (writer, _) = await Factory.MemberAsync();
        using var _w = writer;
        var moderator = (await ModeratorAsync()).Browser;
        try
        {
            var before = await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts);
            await MatchesTheListsAsync(moderator, before);

            // A flagged argument waits: one more in "Argumentai"; published, it leaves and the count goes back.
            using var wrote = await writer.WriteReasonAsync(AppealSteps.SecondBill, "for", "Daugiau rasite https://pavyzdys.test/rinkliavos");
            wrote.StatusCode.ShouldBe(HttpStatusCode.Created, await wrote.Content.ReadAsStringAsync());
            var reason = await wrote.ReadAsync<ReasonCreatedResponse>();
            var waiting = await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts);
            waiting.Reasons.ShouldBe(before.Reasons + 1);
            await MatchesTheListsAsync(moderator, waiting);

            using (var published = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, reason.Id), new ModerationRequest()))
            {
                published.StatusCode.ShouldBe(HttpStatusCode.NoContent, await published.Content.ReadAsStringAsync());
            }

            (await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts)).Reasons.ShouldBe(before.Reasons);
        }
        finally
        {
            await Database.FreeBillAsync(AppealSteps.SecondBill);
        }
    }

    /// <summary>
    /// The lists composed in memory (the bills' records in the queue, "Projektai be aprašymo") are counted as they decide who
    /// is on them, without their rows: a reported record and a followed bill without a description each add one.
    /// </summary>
    [Fact]
    public async Task The_counts_of_the_lists_composed_in_memory_follow_an_item_in()
    {
        async Task FreeAsync()
        {
            foreach (var bill in new[] { AppealSteps.TaxBill, AppealSteps.ThirdBill })
            {
                await Database.FreeBillAsync(bill);
                await Database.UnfollowBillAsync(bill);
            }
        }

        await FreeAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var (member, _) = await Factory.MemberAsync(consent: false);
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        try
        {
            var record = await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
            var before = await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts);
            await MatchesTheListsAsync(moderator, before);

            // A follower alone on a bill without a description puts it on "Be aprašymo".
            await member.FollowBillAsync(AppealSteps.TaxBill);
            var followed = await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts);
            followed.Undescribed.ShouldBe(before.Undescribed + 1);
            await MatchesTheListsAsync(moderator, followed);

            // A report on the record's description puts the record in the queue.
            using (var reported = await member.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "record", TargetId = record.Id, Ground = "insult", Text = "Įžeidžia." }))
            {
                reported.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reported.Content.ReadAsStringAsync());
            }

            var queued = await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts);
            queued.Records.ShouldBe(followed.Records + 1);
            await MatchesTheListsAsync(moderator, queued);
        }
        finally
        {
            await FreeAsync();
        }
    }

    [Fact]
    public async Task Each_list_of_the_queue_is_read_alone()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var both = await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue);

        var records = await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueueOf(QueueLists.Records));
        records.Records.Select(r => r.Id).ShouldBe(both.Records.Select(r => r.Id));
        (records.RecordsTotal, records.Reasons.Count, records.ReasonsTotal).ShouldBe((both.RecordsTotal, 0, 0));

        var reasons = await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueueOf(QueueLists.Reasons));
        reasons.Reasons.Select(r => r.Id).ShouldBe(both.Reasons.Select(r => r.Id));
        (reasons.ReasonsTotal, reasons.Records.Count, reasons.RecordsTotal).ShouldBe((both.ReasonsTotal, 0, 0));

        using var unknown = await moderator.Client.GetAsync(AppealRoutes.ModerationQueueOf("visi"));
        await unknown.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "queue.list-invalid");
    }

    [Fact]
    public async Task The_page_opens_on_the_tab_asked_for_else_the_first_work_tab_with_something_waiting()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var counts = await moderator.GetJsonAsync<ModerationCountsResponse>(AppealRoutes.ModerationCounts);
        var opened = ModerationTabs.Open(null, counts);
        ModerationTabs.Work.ShouldContain(opened, "never a reference tab");

        var page = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync(ModerationTabs.Path));
        page.ShouldContain($"id=\"tab-{opened}\" aria-current=\"page\"");
        foreach (var tab in ModerationTabs.Work)
        {
            // A work tab's chip only when something waits; a reference tab's count always, in brackets.
            page.Contains($"id=\"tab-count-{tab}\"", StringComparison.Ordinal).ShouldBe(ModerationTabs.Count(counts, tab) > 0, tab);
        }

        page.ShouldContain($"id=\"tab-count-{ModerationTabs.Hidden}\">({counts.Hidden})</span>");

        var hidden = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync(ModerationTabs.Link(ModerationTabs.Hidden)));
        hidden.ShouldContain($"id=\"tab-{ModerationTabs.Hidden}\" aria-current=\"page\"");
        hidden.ShouldContain("id=\"h-hidden-appeals\"");
        hidden.ShouldNotContain("id=\"h-appeals\"", customMessage: "only the open tab's list is shown");

        var unknown = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync(ModerationTabs.Path + "?skiltis=nera-tokios"));
        unknown.ShouldContain($"id=\"tab-{opened}\" aria-current=\"page\"");
    }

    /// <summary>Each count is the total its list states (all lists are short in the tests, under their cuts).</summary>
    private static async Task MatchesTheListsAsync(TestBrowser moderator, ModerationCountsResponse counts)
    {
        var queue = await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue);
        var organisations = await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue);
        var responses = await moderator.GetJsonAsync<MpResponseModerationResponse>(MpResponseRoutes.Moderation);
        var comments = await moderator.GetJsonAsync<CommentModerationResponse>(CommentRoutes.Moderation);
        var profiles = await moderator.GetJsonAsync<List<ProfileModerationResponse>>(ProfileRoutes.Moderation);
        var undescribed = await moderator.GetJsonAsync<UndescribedBillsResponse>(AppealRoutes.Undescribed);
        var hidden = await moderator.GetJsonAsync<HiddenItemsResponse>(AppealRoutes.Hidden);
        var hiddenOrganisations = await moderator.GetJsonAsync<HiddenOrganisationsResponse>(OrganisationRoutes.ModerationHidden);
        var restrictions = await moderator.GetJsonAsync<List<RestrictionResponse>>(AppealRoutes.Restrictions);

        counts.ShouldBe(new ModerationCountsResponse(
            queue.RecordsTotal,
            queue.ReasonsTotal,
            organisations.OrganisationsTotal,
            organisations.StatementsTotal,
            responses.QueueTotal,
            profiles.Count(p => p.Reports.Count > 0),
            comments.ReportedTotal,
            undescribed.Total,
            hidden.Records.Count + hidden.Reasons.Count + hiddenOrganisations.Organisations.Count + hiddenOrganisations.Statements.Count
                + (hiddenOrganisations.KeptOut?.Count ?? 0) + responses.HiddenTotal + profiles.Count(p => p.HiddenAt is not null) + comments.HiddenTotal,
            restrictions.Count));
    }
}
