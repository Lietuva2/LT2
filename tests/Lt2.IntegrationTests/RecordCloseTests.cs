using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.1 step 6: an explicit rejection from the proceedings or a moderator's decision closes the record with the reason,
/// day and source. The counts freeze, followers hear of it, and a moderator can reopen an incorrect close.
/// </summary>
public sealed class RecordCloseTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const string Source = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas";
    private const string ApprovedRejection = "Pritarta – dėl pagrindinio komiteto siūlymo atmesti šį projektą";
    private const long RejectionVote = 99199700;
    private const long LaterVote = 99199701;

    private static DateOnly DecidedOn => ReasonsToComeSteps.VilniusToday().AddDays(-2);

    private static CloseRecordRequest Closing(string reason = "rejected", DateOnly? day = null, string source = Source, string statement = "Seimas projektą atmetė.") =>
        new() { Reason = reason, DecidedOn = day ?? DecidedOn, SourceUrl = source, Statement = statement };

    private static Task<HttpResponseMessage> CloseAsync(TestBrowser moderator, Guid record, CloseRecordRequest request) =>
        moderator.PostAsync(AppealRoutes.With(AppealRoutes.CloseRecord, record), request);

    private static Task<HttpResponseMessage> ReopenAsync(TestBrowser moderator, Guid record, string statement = "Uždaryta per klaidą.") =>
        moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReopenRecord, record), new StatementRequest { Statement = statement });

    private async Task RunFollowJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<DigestCandidate>> CandidatesAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDigestSource>().CandidatesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_explicit_rejection_closes_once_and_reopening_ignores_that_vote_but_allows_a_later_rejection()
    {
        await Database.ForgetEventsAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        var (author, email) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        using var visitor = NewBrowser();
        var decidedOn = DecidedOn;
        var held = VilniusTime.StartOf(decidedOn).AddMinutes(12);
        const string source = "https://www.lrs.lt/sip/portal.show?p_r=37067&p_bals_id=99199700";
        var activeList = AppealRoutes.AppealsList(Bill, false, false, null, limit: 200);

        try
        {
            await PutMotionAsync(RejectionVote, held, "Nepritarta – dėl pagrindinio komiteto siūlymo atmesti šį projektą");
            (await CloseFinishedAsync()).ShouldBe(0, "the motion to reject was defeated");
            await PutMotionAsync(RejectionVote, held, "Nepritarta – dėl pritarimo po pateikimo");
            (await CloseFinishedAsync()).ShouldBe(0, "a failed stage vote isn't a decision to reject the bill");
            await PutMotionAsync(RejectionVote, held, "Pritarta – už pasiūlymą grąžinti šį projektą iniciatoriams tobulinti");
            (await CloseFinishedAsync()).ShouldBe(0, "returning the bill does not close it");

            await PutMotionAsync(RejectionVote, held, ApprovedRejection);
            await PutMotionAsync(LaterVote, held.AddHours(1), "Pritarta – dėl pritarimo po svarstymo");
            (await CloseFinishedAsync()).ShouldBe(0, "a later vote supersedes the earlier rejection");
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", LaterVote));
            (await visitor.GetJsonAsync<BillListResponse>(activeList)).Items.ShouldNotContain(i => i.Bill.Number == Bill);
            (await CandidatesAsync()).ShouldNotContain(c => c.RecordId == record.Id);
            using (var changed = await author.TakeSideAsync(Bill, "for"))
            {
                await changed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
            }

            (await CloseFinishedAsync()).ShouldBe(1);
            (await CloseFinishedAsync()).ShouldBe(0);
            var closed = await visitor.RecordAsync(Bill);
            (closed.Status, closed.ClosedReason, closed.ClosedDecidedOn, closed.ClosedSourceUrl, closed.ClosingVoteId)
                .ShouldBe(("closed", "rejected", (DateOnly?)decidedOn, source, (long?)null));
            closed.Counts.Against.ShouldBe(1);
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + Bill));
            page.ShouldContain("Seimas projektą atmetė");
            page.ShouldContain($"href=\"{source}\" id=\"closed-source\" target=\"_blank\" rel=\"external noopener\"");
            (await Database.ScalarAsync<long>(
                """
                SELECT count(*) FROM audit.event
                WHERE action = 'record.closed' AND target = @t AND actor_role = 'system' AND actor_user_id IS NULL
                  AND details->>'reason' = 'rejected' AND details->>'vote' = '99199700'
                """, ("t", "record:" + record.Id))).ShouldBe(1);
            await RunFollowJobAsync();
            (await Database.EmailsAsync(MessageKinds.FollowClosedEmail, email)).ShouldBe(1);

            using (var reopened = await ReopenAsync(moderator, record.Id))
            {
                reopened.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reopened.Content.ReadAsStringAsync());
            }

            (await CloseFinishedAsync()).ShouldBe(0, "the same Seimas vote is ignored after reopening");
            (await visitor.GetJsonAsync<BillListResponse>(activeList)).Items.ShouldContain(i => i.Bill.Number == Bill);
            using (var changed = await author.TakeSideAsync(Bill, "for"))
            {
                changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await PutMotionAsync(LaterVote, held.AddHours(1), ApprovedRejection);
            (await CloseFinishedAsync()).ShouldBe(1);
            var closedAgain = await visitor.RecordAsync(Bill);
            closedAgain.Counts.For.ShouldBe(1);
            closedAgain.ClosedSourceUrl.ShouldBe("https://www.lrs.lt/sip/portal.show?p_r=37067&p_bals_id=99199701");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id IN (@first, @second)", ("first", RejectionVote), ("second", LaterVote));
            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }

    [Fact]
    public async Task A_reopen_after_a_moderators_close_overrules_the_rejection_vote_the_import_brought_later()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        var held = VilniusTime.StartOf(DecidedOn).AddMinutes(12);
        try
        {
            // A moderator records the rejection by hand; the import brings the vote afterwards.
            (await CloseAsync(moderator, record.Id, Closing())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await PutMotionAsync(RejectionVote, held, ApprovedRejection);
            (await CloseFinishedAsync()).ShouldBe(0, "the record is closed already");

            // The moderator finds the close wrong and reopens it: the vote known now does not close it again.
            (await ReopenAsync(moderator, record.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await CloseFinishedAsync()).ShouldBe(0, "the reopen overruled the rejection vote");
            (await NewBrowser().RecordAsync(Bill)).Status.ShouldBe("open");
            using (var changed = await author.TakeSideAsync(Bill, "for"))
            {
                changed.StatusCode.ShouldBe(HttpStatusCode.NoContent, "taking part is open again");
            }

            // A later rejection still closes it.
            await PutMotionAsync(LaterVote, held.AddHours(1), ApprovedRejection);
            (await CloseFinishedAsync()).ShouldBe(1);
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id IN (@first, @second)", ("first", RejectionVote), ("second", LaterVote));
            await Database.FreeBillAsync(Bill);
        }
    }

    private async Task<int> CloseFinishedAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>().CloseFinishedAsync(TestContext.Current.CancellationToken);
    }

    private Task PutMotionAsync(long id, DateTimeOffset held, string wording) => Database.ExecuteAsync(
        """
        INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, kind, is_consensus, has_result)
        SELECT @id, agenda_item_id, bill_id, @held, @wording, 'Other', false, false
        FROM parliament.vote WHERE id = 99100102
        ON CONFLICT (id) DO UPDATE SET wording = excluded.wording
        """, ("id", id), ("held", held), ("wording", wording));

    [Fact]
    public async Task A_moderator_closes_a_rejected_bill_its_counts_freeze_sides_are_refused_it_leaves_the_summaries_and_the_close_is_audited()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        var (member, memberEmail) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CandidatesAsync()).ShouldContain(c => c.RecordId == record.Id, "an open record is a summary's candidate");
        try
        {
            using (var closed = await CloseAsync(moderator, record.Id, Closing()))
            {
                closed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await closed.Content.ReadAsStringAsync());
            }

            using var stranger = NewBrowser();
            var after = await stranger.RecordAsync(Bill);
            (after.Status, after.ClosedReason, after.ClosedDecidedOn, after.ClosedSourceUrl, after.ClosingVoteId).ShouldBe(("closed", "rejected", (DateOnly?)DecidedOn, Source, (long?)null));
            after.ClosedAt.ShouldNotBeNull();
            (after.Counts.For, after.Counts.Against).ShouldBe((1, 1));
            (await moderator.ModerationBillAsync(Bill)).ClosedReason.ShouldBe("rejected");

            // Nobody can take or change a side, or write an argument, any more.
            foreach (var refused in new[]
            {
                await member.TakeSideAsync(Bill, "against"),
                await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(Bill)),
                await member.WriteReasonAsync(Bill, "for", "Per vėlu, projektas atmestas."),
            })
            {
                using (refused)
                {
                    await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
                }
            }

            // The counts were frozen: a position removed later (an account deleted) changes nothing.
            await Database.ExecuteAsync("DELETE FROM appeals.position WHERE user_id = @u", ("u", await Database.UserIdAsync(memberEmail)));
            (await stranger.RecordAsync(Bill)).Counts.For.ShouldBe(1);

            // Audited as the closing job's close is, with the moderator as the actor, the reason and the statement; nothing personal.
            var details = await Database.ScalarAsync<string>(
                "SELECT details::text FROM audit.event WHERE action = 'record.closed' AND target = @t AND actor_role = 'moderator' AND actor_user_id IS NOT NULL",
                ("t", "record:" + record.Id));
            using (var json = JsonDocument.Parse(details!))
            {
                json.RootElement.GetProperty("statement").GetString().ShouldBe("Seimas projektą atmetė.");
                json.RootElement.GetProperty("extra").GetProperty("reason").GetString().ShouldBe("rejected");
                json.RootElement.GetProperty("extra").GetProperty("sourceUrl").GetString().ShouldBe(Source);
            }

            details!.ShouldNotContain(memberEmail);

            // It drops out of the summaries as a record closed by a vote does, and is in the closed tab with its frozen counts.
            (await CandidatesAsync()).ShouldNotContain(c => c.RecordId == record.Id);
            var closedTab = await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, true, null, limit: 200));
            closedTab.Items.ShouldContain(i => i.Bill.Number == Bill && i.Status == "closed");
            (await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: 200))).Items.ShouldNotContain(i => i.Bill.Number == Bill);

            // No vote closed it, so the analytics page does not compare it (design 8.26.2).
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var appeals = scope.ServiceProvider.GetRequiredService<IAppealsModule>();
                (await appeals.ClosedRecordsAsync(CancellationToken.None)).ShouldNotContain(c => c.BillNumber == Bill);
            }

            // Closing again is refused: the record is not open.
            using (var again = await CloseAsync(moderator, record.Id, Closing("withdrawn")))
            {
                await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_close_is_refused_to_a_member_without_a_source_on_lrs_lt_a_reason_a_past_day_or_a_statement()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        try
        {
            using (var byMember = await CloseAsync(author, record.Id, Closing()))
            {
                await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            foreach (var (request, field) in new[]
            {
                (Closing(source: "https://naujienos.pavyzdys.test/atmesta"), "sourceUrl"),
                (Closing(source: "http://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas"), "sourceUrl"),
                (Closing(source: ""), "sourceUrl"),
                (Closing(source: "https://lrs.lt/" + new string('a', 500)), "sourceUrl"),
                (Closing(reason: "vote"), "reason"),
                (Closing(statement: " "), "statement"),
                (Closing(day: default(DateOnly)), "decidedOn"),
            })
            {
                using var refused = await CloseAsync(moderator, record.Id, request);
                refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
                (await refused.Content.ReadAsStringAsync()).ShouldContain(field, Case.Sensitive);
            }

            // A day after today in Vilnius is refused where the close is stored (the validator does not know the clock).
            using (var future = await CloseAsync(moderator, record.Id, Closing(day: ReasonsToComeSteps.VilniusToday().AddDays(1))))
            {
                await future.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "closing.date-future");
            }

            using (var unknown = await CloseAsync(moderator, Guid.CreateVersion7(), Closing()))
            {
                unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }

            (await NewBrowser().RecordAsync(Bill)).Status.ShouldBe("open", "nothing refused closed it");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'record.closed' AND target = @t", ("t", "record:" + record.Id))).ShouldBe(0);

            // The module refuses as the endpoint does, for every caller (design 6.8).
            await using var scope = Factory.Services.CreateAsyncScope();
            var module = scope.ServiceProvider.GetRequiredService<IAppealsModeration>();
            var actor = new StaffActor(await Database.UserIdAsync((await ModeratorAsync()).Email), "moderator", null);
            (await module.CloseRecordAsync(record.Id, new RecordClosure("rejected", DecidedOn, "https://pavyzdys.test/a", "Bandymas."), actor, CancellationToken.None))
                .Error.ShouldNotBeNull().Code.ShouldBe("closing-url.invalid");
            (await module.CloseRecordAsync(record.Id, new RecordClosure("withdrawn", ReasonsToComeSteps.VilniusToday().AddDays(1), Source, "Bandymas."), actor, CancellationToken.None))
                .Error.ShouldNotBeNull().Code.ShouldBe("closing.date-future");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_wrong_close_is_reopened_with_the_live_counts_and_sides_are_taken_again_but_a_close_by_the_vote_is_never_reopened()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        try
        {
            using (var notClosed = await ReopenAsync(moderator, record.Id))
            {
                await notClosed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
            }

            (await CloseAsync(moderator, record.Id, Closing("withdrawn", statement: "Iniciatoriai atsiėmė."))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using (var noStatement = await ReopenAsync(moderator, record.Id, " "))
            {
                noStatement.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            using (var byMember = await ReopenAsync(author, record.Id))
            {
                await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            // While closed a position goes (an account deleted): the frozen counts stay; reopened, the live ones come back.
            var (member, memberEmail) = await Factory.MemberAsync();
            using var _m = member;
            await Database.ExecuteAsync(
                "INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since) VALUES (@r, @u, 'for', now(), now(), now())",
                ("r", record.Id), ("u", await Database.UserIdAsync(memberEmail)));
            using var stranger = NewBrowser();
            ((await stranger.RecordAsync(Bill)).Counts.For).ShouldBe(0, "frozen at the close");

            (await ReopenAsync(moderator, record.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var reopened = await stranger.RecordAsync(Bill);
            (reopened.Status, reopened.ClosedAt, reopened.ClosedReason, reopened.ClosedDecidedOn, reopened.ClosedSourceUrl).ShouldBe(("open", (DateTimeOffset?)null, (string?)null, (DateOnly?)null, (string?)null));
            (reopened.Counts.For, reopened.Counts.Against).ShouldBe((1, 1), "the positions were kept");
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM appeals.bill_record WHERE id = @r AND closed_for IS NULL AND closed_for_older_than30days IS NULL AND closed_against IS NULL AND closed_against_older_than30days IS NULL",
                ("r", record.Id))).ShouldBe(1);
            (await member.TakeSideAsync(Bill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent, "sides are taken again");
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'record.reopened' AND target = @t AND actor_role = 'moderator' AND details->'extra'->>'reason' = 'withdrawn'",
                ("t", "record:" + record.Id))).ShouldBe(1);

            // A record the Seimas's vote closed is never reopened.
            await Database.ExecuteAsync(
                "UPDATE appeals.bill_record SET status = 'closed', closed_at = now(), closing_vote_id = 99100102, closed_reason = 'vote', closed_for = 1, closed_for_older_than30days = 0, closed_against = 1, closed_against_older_than30days = 0 WHERE id = @r",
                ("r", record.Id));
            using (var byVote = await ReopenAsync(moderator, record.Id))
            {
                await byVote.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
            }

            (await stranger.RecordAsync(Bill)).Status.ShouldBe("closed");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_page_of_a_record_a_moderator_closed_shows_what_happened_the_day_and_the_source_and_no_vote()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        try
        {
            (await CloseAsync(moderator, record.Id, Closing("withdrawn"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using var stranger = NewBrowser();
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + Bill));
            var day = DecidedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            html.ShouldContain($"Iniciatoriai projektą atsiėmė {day}.");
            html.ShouldContain($"<a href=\"{Source}\" id=\"closed-source\" target=\"_blank\" rel=\"external noopener\">Šaltinis</a>");
            html.ShouldContain("Projektas atsiimtas");
            html.ShouldContain("Skaičiai užfiksuoti");
            html.ShouldContain("id=\"appeal-status\"");
            html.ShouldNotContain("Seimas balsavo galutinai", customMessage: "no vote closed it");
            // The decision sheet holds no ballots; the bill's own votes (this one has some) are the Seimas part of the sides' switch.
            var decision = html[html.IndexOf("id=\"seimas-result\"", StringComparison.Ordinal)..];
            decision = decision[..decision.IndexOf("</section>", StringComparison.Ordinal)];
            decision.ShouldNotContain("id=\"each-mp\"");
            decision.ShouldNotContain("class=\"seatmap\"");
            html.ShouldNotContain("class=\"seatmap\"");
            html.ShouldNotContain("id=\"side-for\"", customMessage: "no side can be taken any more");
            html.ShouldNotContain("id=\"moderation-panel\"", customMessage: "a visitor sees no moderators' panel");

            // The closed tab of /projektai lists it.
            var tab = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/projektai/uzdaryti"));
            tab.ShouldContain($"id=\"bill-{Bill}\"");

            // A moderator sees the panel on the closed record, with "Atšaukti uždarymą" and no close.
            var own = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync("/seimas/" + Bill));
            own.ShouldContain("id=\"moderation-panel\"", Case.Sensitive);
            own.ShouldContain($"id=\"action-reopen-panel-{record.Id}\"");
            own.ShouldNotContain($"id=\"action-close-panel-{record.Id}\"");

            // Rejected reads so too.
            (await ReopenAsync(moderator, record.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var open = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync("/seimas/" + Bill));
            open.ShouldContain($"id=\"action-close-panel-{record.Id}\"", customMessage: "an open record offers the close");
            (await CloseAsync(moderator, record.Id, Closing("rejected"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + Bill)).ShouldContain($"Seimas projektą atmetė {day}.");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Followers_hear_of_a_close_by_the_follow_email_and_a_close_undone_before_the_email_is_never_sent()
    {
        await Database.ForgetEventsAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        var (author, authorEmail) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Bill, "against");
        var (quiet, quietEmail) = await Factory.MemberAsync();
        using var _q = quiet;
        await quiet.FollowBillAsync(Bill);
        (await quiet.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.Notices, new Lt2.Api.Contracts.Follows.NoticeSettingsRequest { Planned = true, Voted = false })).Dispose();
        try
        {
            // Undone before the follow job ran: nobody is told.
            (await CloseAsync(moderator, record.Id, Closing())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await ReopenAsync(moderator, record.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await RunFollowJobAsync();
            (await Database.EmailsAsync(MessageKinds.FollowClosedEmail, authorEmail)).ShouldBe(0);

            // A first side followed the bill: its follower is told once, in the rhythm, with the day and the source.
            (await CloseAsync(moderator, record.Id, Closing("withdrawn"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await RunFollowJobAsync();
            (await Database.EmailsAsync(MessageKinds.FollowClosedEmail, authorEmail)).ShouldBe(1);
            using (var json = JsonDocument.Parse((await Database.LatestEmailAsync(MessageKinds.FollowClosedEmail, authorEmail))!))
            {
                var root = json.RootElement;
                root.GetProperty("billNumber").GetString().ShouldBe(Bill);
                root.GetProperty("reason").GetString().ShouldBe("withdrawn");
                root.GetProperty("day").GetString().ShouldBe(DecidedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                root.GetProperty("sourceUrl").GetString().ShouldBe(Source);
                root.GetProperty("billPath").GetString().ShouldBe("/seimas/" + Bill);
                root.GetProperty("stopToken").GetString().ShouldNotBeNullOrEmpty();
                root.TryGetProperty("side", out _).ShouldBeFalse();
                root.TryGetProperty("for", out _).ShouldBeFalse();
            }

            (await Database.EmailsAsync(MessageKinds.FollowClosedEmail, quietEmail)).ShouldBe(0, "the voted switch is off");
            await RunFollowJobAsync();
            (await Database.EmailsAsync(MessageKinds.FollowClosedEmail, authorEmail)).ShouldBe(1, "one email per event");

            // Reopening tells nobody.
            (await ReopenAsync(moderator, record.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await RunFollowJobAsync();
            (await Database.EmailsAsync(MessageKinds.FollowClosedEmail, authorEmail)).ShouldBe(1, "reopening tells nobody");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }
}
