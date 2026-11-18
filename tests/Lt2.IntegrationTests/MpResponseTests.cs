using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Mps;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.5.5 to 8.5.7 and their part of 8.5.8: an MP's response is public at once with its mark, keeps its versions when
/// changed, stays as withdrawn when withdrawn and continues its history when written again; only the MP's account or an office
/// member in a passkey session writes it, while the link and the mandate last; after the adoption vote it is frozen and the
/// MP's ballot stands beside it; a moderator hides it with a public statement emailed to the MP and restores it while the bill
/// is open; it is never counted; no answer, page or log says which account wrote it; the lists page on the server; and the
/// page of the bills in summaries. Each test takes its own stand-in MP and removes its accounts and responses.
/// </summary>
public sealed class MpResponseTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    /// <summary>A vote on <see cref="Bill"/> to copy an adoption vote from (its sitting and agenda item).</summary>
    private const long BillVote = 99100102;

    private static readonly string[] ResponseFields = ["id", "status", "stance", "text", "publishedAt", "editedAt", "withdrawnAt", "hiddenStatement", "earlier", "vote", "comments"];

    [Fact]
    public async Task A_response_is_public_at_once_with_its_mark_and_keeps_every_public_version_when_changed_withdrawn_and_written_again()
    {
        const string First = "Balsuosiu už: pataisa mažina naštą šeimoms.";
        const string Second = "Balsuosiu prieš: pakeitimas paliečia smulkųjį verslą.";
        const string Third = "Dar svarstau, laukiu komiteto išvados.";
        await Database.FreeBillAsync(Bill);
        using var mp = await Factory.MpAccountAsync(MpSteps.Gluosnis);
        using var visitor = NewBrowser();
        try
        {
            var saved = await mp.Browser.RespondOkAsync(Bill, "for", First);
            saved.Status.ShouldBe("published", "public at once (design 8.5.5)");
            var view = await visitor.ResponsesAsync(Bill);
            var item = view.Items.ShouldHaveSingleItem();
            (item.Mp.Slug, item.Mp.FirstName, item.Mp.LastName).ShouldBe((mp.Slug, "Kęstutis", "Gluosnis"));
            (item.Response.Id, item.Response.Status, item.Response.Stance, item.Response.Text, item.Response.EditedAt).ShouldBe((saved.Id, "published", "for", First, (DateTimeOffset?)null));
            item.Response.Earlier.ShouldBeEmpty();
            view.Counts.ShouldBe(new MpResponseCountsResponse(1, 1, 0, 0));
            var part = await PartAsync(visitor, "/seimas/" + Bill);
            part.ShouldContain("Seimo narių atsakymai");
            part.ShouldContain("Kęstutis Gluosnis");
            part.ShouldContain("Seimo narys arba jo biuras");
            part.ShouldContain("Balsuos už");
            part.ShouldContain(First);
            // Design 8.16.3 (owner, 2026-11-18): the stance above the name, the name right above the bubble (its tail points at
            // it), the faction, the mark and the dates under the bubble.
            var article = part[part.IndexOf("<article class=\"said said--mp\"", StringComparison.Ordinal)..];
            article = article[..article.IndexOf("</article>", StringComparison.Ordinal)];
            var places = new[] { "class=\"said__side\"", "class=\"row statement__who\"", "class=\"bubble bubble--mp", "Seimo narys arba jo biuras" }
                .Select(s => article.IndexOf(s, StringComparison.Ordinal)).ToList();
            places.ShouldAllBe(i => i >= 0);
            places.ShouldBeInOrder(SortDirection.Ascending);
            article[places[1]..places[2]].ShouldNotContain("class=\"tag", customMessage: "nothing but the name between the name line and the bubble");

            // Changed: the earlier text stays public, with its dates; saving the same again adds nothing.
            await mp.Browser.RespondOkAsync(Bill, "against", Second);
            await mp.Browser.RespondOkAsync(Bill, "against", Second);
            var changed = (await visitor.ResponsesAsync(Bill)).Items.Single().Response;
            (changed.Id, changed.Stance, changed.Text).ShouldBe((saved.Id, "against", Second));
            changed.EditedAt.ShouldNotBeNull();
            var earlier = changed.Earlier.ShouldHaveSingleItem();
            (earlier.Stance, earlier.Text, earlier.Withdrawn).ShouldBe(("for", First, false));
            part = await PartAsync(visitor, "/seimas/" + Bill);
            part.ShouldContain("Keista");
            part.ShouldContain("Ankstesnės versijos (1)");
            part.ShouldContain(First);
            part.ShouldContain(Second);
            // Owner, 2026-11-18: the versions show what changed, the response in force against the one before it.
            part.ShouldContain("dabartinė versija, parašyta");
            part.ShouldContain("<ins><span class=\"visually-hidden\">pridėta: </span>prieš</ins>");
            part.ShouldContain("<del><span class=\"visually-hidden\">pašalinta: </span>už</del>");

            // Withdrawn: it stays, as "Atsakymas atsiimtas" with the date and its versions; it is no longer counted as an answer.
            using (var withdrawn = await mp.Browser.WithdrawResponseAsync(Bill))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync());
            }

            view = await visitor.ResponsesAsync(Bill);
            var gone = view.Items.Single().Response;
            (gone.Status, gone.Stance, gone.Text).ShouldBe(("withdrawn", (string?)null, (string?)null));
            gone.WithdrawnAt.ShouldNotBeNull();
            gone.Earlier.Select(v => (v.Stance, v.Text, v.Withdrawn)).ShouldBe([("against", Second, true), ("for", First, false)]);
            view.Counts.Answered.ShouldBe(0);
            part = await PartAsync(visitor, "/seimas/" + Bill);
            part.ShouldContain("Atsakymas atsiimtas");
            part.ShouldContain("Ankstesnės versijos (2)");
            // Owner, 2026-11-18: the withdrawal opens the versions, the text withdrawn struck through and whole before it.
            part.ShouldContain("response-withdrawal");
            part.ShouldContain("<p class=\"voice typed\">" + Second + "</p>");
            await (await mp.Browser.WithdrawResponseAsync(Bill)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "response.not-found");

            // Written again: the same response, and its history continues without a duplicate.
            var again = await mp.Browser.RespondOkAsync(Bill, "undecided", Third);
            again.Id.ShouldBe(saved.Id);
            var back = (await visitor.ResponsesAsync(Bill)).Items.Single().Response;
            (back.Status, back.Stance, back.Text, back.WithdrawnAt).ShouldBe(("published", "undecided", Third, (DateTimeOffset?)null));
            back.Earlier.Select(v => v.Text).ShouldBe([Second, First]);

            // The public history holds no person.
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.revision WHERE entity_kind = 'response' AND entity_id = @id", ("id", saved.Id))).ShouldBe(2);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.revision WHERE entity_kind = 'response' AND entity_id = @id AND changed_by_user_id IS NOT NULL", ("id", saved.Id))).ShouldBe(0);
        }
        finally
        {
            await EndAsync(mp);
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Only_the_mps_account_or_an_office_member_in_a_passkey_session_writes_and_every_other_is_refused()
    {
        await Database.FreeBillAsync(Bill);
        using var mp = await Factory.MpAccountAsync(MpSteps.Eglaite);
        var (member, memberKey, _) = await Factory.OfficeMemberAsync(mp, "Biuro Narė");
        using var _m = member;
        using var _k = memberKey;
        var (ordinary, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _o = ordinary;
        using var visitor = NewBrowser();
        var mandate = await Database.ScalarAsync<string>(
            "SELECT \"to\"::text FROM parliament.mandate WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("p", MpSteps.Eglaite.SeimasPersonId));
        try
        {
            // Nobody else: an ordinary member.
            await (await ordinary.RespondAsync(Bill, "for", "Svetimas.")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");
            await (await ordinary.Client.GetAsync(MpResponseRoutes.MineOf(Bill))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");

            // Signed in by a code, the MP's account reads and cannot publish (design 8.5.4).
            using (var code = await Factory.CodeSessionAsync(mp.Email))
            {
                await (await code.RespondAsync(Bill, "for", "Kodu.")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
                var mine = await code.GetJsonAsync<MyMpResponseResponse>(MpResponseRoutes.MineOf(Bill));
                (mine.Open, mine.MayWrite, mine.Refusal, mine.Mp.Part).ShouldBe((true, false, "mp.passkey-required", "mp"));
            }

            // An office member in a passkey session writes the MP's response; the MP's account changes the same one.
            var saved = await member.RespondOkAsync(Bill, "for", "Biuras atsako Seimo nario vardu.");
            var own = await mp.Browser.GetJsonAsync<MyMpResponseResponse>(MpResponseRoutes.MineOf(Bill));
            (own.MayWrite, own.Refusal, own.Response!.Id, own.Response.Text).ShouldBe((true, (string?)null, saved.Id, "Biuras atsako Seimo nario vardu."));
            (await mp.Browser.RespondOkAsync(Bill, "for", "Seimo narys patikslina.")).Id.ShouldBe(saved.Id);

            // A removed office member is refused at once.
            var id = (await mp.Browser.GetJsonAsync<MpOfficeResponse>(MpRoutes.Office)).Members.Single().Id;
            (await mp.Browser.SendAsync(HttpMethod.Delete, MpRoutes.OfficeMemberOf(id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await (await member.RespondAsync(Bill, "against", "Jau ne biure.")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");

            // An MP whose mandate has ended publishes nothing new (design 8.5.5); the response stays.
            await Database.ExecuteAsync(
                "UPDATE parliament.mandate SET \"to\" = current_date - 1 WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("p", MpSteps.Eglaite.SeimasPersonId));
            await (await mp.Browser.RespondAsync(Bill, "against", "Po kadencijos.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.mandate-ended");
            await (await mp.Browser.WithdrawResponseAsync(Bill)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.mandate-ended");
            (await mp.Browser.GetJsonAsync<MyMpResponseResponse>(MpResponseRoutes.MineOf(Bill))).Refusal.ShouldBe("response.mandate-ended");
            await RestoreMandateAsync(mandate);

            // Validation: one of the three stances; the text no longer than the members' texts setting allows (Texts:MaxLength, 2 000 by default; owner, 2026-11-16).
            await (await mp.Browser.RespondAsync(Bill, "abstain", "Tekstas.")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
            await (await mp.Browser.RespondAsync(Bill, "for", new string('a', 2001))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "response.too-long");

            // The link given up: refused at once; the response stays as published (design 8.5.2).
            (await mp.Browser.SendAsync(HttpMethod.Delete, MpRoutes.Mine)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await (await mp.Browser.RespondAsync(Bill, "against", "Be profilio.")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");
            var shown = (await visitor.ResponsesAsync(Bill)).Items.Single().Response;
            (shown.Status, shown.Text).ShouldBe(("published", "Seimo narys patikslina."));
        }
        finally
        {
            await RestoreMandateAsync(mandate);
            await EndAsync(mp);
            await member.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task After_the_adoption_vote_a_response_is_frozen_and_the_mps_ballot_stands_beside_it_never_added_up()
    {
        const long vote = 99199801;
        await Database.FreeBillAsync(Bill);
        await Database.RemoveVoteAsync(vote);
        using var mp = await Factory.MpAccountAsync(MpSteps.Sermuksnyte);
        using var visitor = NewBrowser();
        try
        {
            await mp.Browser.RespondOkAsync(Bill, "for", "Balsuosiu už, nes tai teisinga.");
            (await visitor.ResponsesAsync(Bill)).Items.Single().Response.Vote.ShouldBeNull("no ballot while the bill is open");

            await Database.AddAdoptionVoteAsync(vote, BillVote);
            await Database.AddBallotAsync(vote, MpSteps.Sermuksnyte.SeimasPersonId, "Against");
            var after = await visitor.ResponsesAsync(Bill);
            after.Open.ShouldBeFalse();
            var response = after.Items.Single().Response;
            (response.Status, response.Stance, response.Vote!.VoteId, response.Vote.Ballot).ShouldBe(("published", "for", vote, "against"));

            // Two facts, never compared: the answers carry exactly these fields, with no share, sum or score.
            var json = await visitor.Client.GetStringAsync(MpResponseRoutes.OnBillOf(Bill));
            using (var document = JsonDocument.Parse(json))
            {
                document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["billNumber", "open", "counts", "items"], ignoreOrder: true);
                document.RootElement.GetProperty("counts").EnumerateObject().Select(p => p.Name).ShouldBe(["answered", "for", "against", "undecided"], ignoreOrder: true);
                var r = document.RootElement.GetProperty("items")[0].GetProperty("response");
                r.EnumerateObject().Select(p => p.Name).ShouldBe(ResponseFields, ignoreOrder: true);
                r.GetProperty("vote").EnumerateObject().Select(p => p.Name).ShouldBe(["voteId", "heldAt", "ballot"], ignoreOrder: true);
            }

            var part = await PartAsync(visitor, "/seimas/" + Bill);
            part.ShouldContain("Atsakė: balsuos už. Balsavo: prieš.");
            MpResponseSteps.WithoutIslands(part).ShouldNotContain("%");
            var profile = await PartAsync(visitor, "/p/" + mp.Slug);
            profile.ShouldContain(Bill);
            profile.ShouldContain("Atsakė: balsuos už. Balsavo: prieš.");
            MpResponseSteps.WithoutIslands(profile).ShouldNotContain("%");

            // Frozen, as a side is (design 8.5.5).
            await (await mp.Browser.RespondAsync(Bill, "against", "Per vėlu.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.bill-not-open");
            await (await mp.Browser.WithdrawResponseAsync(Bill)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.bill-not-open");
            var mine = await mp.Browser.GetJsonAsync<MyMpResponseResponse>(MpResponseRoutes.MineOf(Bill));
            (mine.Open, mine.MayWrite, mine.Refusal).ShouldBe((false, false, "response.bill-not-open"));

            // An MP without a ballot did not vote.
            await Database.ExecuteAsync("DELETE FROM parliament.ballot WHERE vote_id = @v", ("v", vote));
            (await visitor.ResponsesAsync(Bill)).Items.Single().Response.Vote!.Ballot.ShouldBe("did-not-vote");
            (await PartAsync(visitor, "/seimas/" + Bill)).ShouldContain("Atsakė: balsuos už. Balsavo: nebalsavo.");

            // A vote approved by consensus has no ballots: said so.
            await Database.RemoveVoteAsync(vote);
            await Database.AddAdoptionVoteAsync(vote, BillVote, consensus: true);
            (await visitor.ResponsesAsync(Bill)).Items.Single().Response.Vote!.Ballot.ShouldBe("consensus");
            (await PartAsync(visitor, "/seimas/" + Bill)).ShouldContain("Balsavo: projektas priimtas bendru sutarimu, Seimo narių balsų nėra.");
        }
        finally
        {
            await Database.RemoveVoteAsync(vote);
            await EndAsync(mp);
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_moderator_hides_a_response_with_a_public_statement_emailed_to_the_mp_and_restores_it_while_the_bill_is_open()
    {
        const long vote = 99199802;
        const string Statement = "Atsakyme paskelbtas asmeninis el. pašto adresas.";
        var moderator = await ModeratorAsync();
        await Database.FreeBillAsync(Bill);
        await Database.RemoveVoteAsync(vote);
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        using var visitor = NewBrowser();
        try
        {
            // Pre-screening flags it: public at once all the same, and in the queue.
            var saved = await mp.Browser.RespondOkAsync(Bill, "against", "Rašykite biurui: biuras@pavyzdys.test");
            (await visitor.ResponsesAsync(Bill)).Items.Single().Response.Status.ShouldBe("published");
            var queued = (await QueueAsync(moderator)).Queue.Single(q => q.Id == saved.Id);
            queued.Flags.ShouldContain("email");
            (queued.Mp.Slug, queued.Bill.Number, queued.BillOpen).ShouldBe((mp.Slug, Bill, true));

            // "Palikti" takes it out of the queue; a report brings it back.
            (await moderator.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Keep, saved.Id), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await QueueAsync(moderator)).Queue.ShouldNotContain(q => q.Id == saved.Id);
            (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "response", TargetId = saved.Id, Ground = "personal-data" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await QueueAsync(moderator)).Queue.Single(q => q.Id == saved.Id).Reports.ShouldHaveSingleItem().Ground.ShouldBe("personal-data");

            // Only a moderator, and only with a statement.
            (await mp.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Hide, saved.Id), new StatementRequest { Statement = "Ne." })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            await (await moderator.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Hide, saved.Id), new StatementRequest { Statement = "" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
            (await moderator.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Hide, saved.Id), new StatementRequest { Statement = Statement })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Its place says so with the statement; the text is not shown; the MP is told at the account's address.
            var hidden = (await visitor.ResponsesAsync(Bill)).Items.Single().Response;
            (hidden.Status, hidden.Stance, hidden.Text, hidden.HiddenStatement).ShouldBe(("hidden", (string?)null, (string?)null, Statement));
            hidden.Earlier.ShouldBeEmpty();
            var part = await PartAsync(visitor, "/seimas/" + Bill);
            part.ShouldContain("Atsakymas paslėptas moderatorių.");
            part.ShouldContain(Statement);
            part.ShouldNotContain("biuras@pavyzdys.test");
            (await NoticesAsync(mp.Email)).ShouldBe([(MpResponseNotices.Hidden, Statement)]);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'moderation.response.hide' AND target = @t", ("t", "response:" + saved.Id))).ShouldBe(1);
            (await QueueAsync(moderator)).Queue.ShouldNotContain(q => q.Id == saved.Id, "the report was answered");

            // The MP's side cannot write over it; it is the moderators' to restore.
            await (await mp.Browser.RespondAsync(Bill, "for", "Kitas tekstas.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.hidden");
            await (await mp.Browser.WithdrawResponseAsync(Bill)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.hidden");
            (await QueueAsync(moderator)).Hidden.Single(h => h.Id == saved.Id).Restorable.ShouldBeTrue();

            (await moderator.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Restore, saved.Id), new StatementRequest { Statement = "Adresas – biuro, viešas." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var restored = (await visitor.ResponsesAsync(Bill)).Items.Single().Response;
            (restored.Status, restored.Text, restored.HiddenStatement).ShouldBe(("published", "Rašykite biurui: biuras@pavyzdys.test", (string?)null));
            (await NoticesAsync(mp.Email)).Select(n => n.Notice).ShouldBe([MpResponseNotices.Hidden, MpResponseNotices.Restored]);

            // After the vote a hidden response is not restored.
            (await moderator.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Hide, saved.Id), new StatementRequest { Statement = Statement })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await Database.AddAdoptionVoteAsync(vote, BillVote);
            await (await moderator.Browser.PostAsync(MpResponseRoutes.With(MpResponseRoutes.Restore, saved.Id), new StatementRequest { Statement = "Bandymas." }))
                .ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
            (await QueueAsync(moderator)).Hidden.Single(h => h.Id == saved.Id).Restorable.ShouldBeFalse();
        }
        finally
        {
            await Database.RemoveVoteAsync(vote);
            await EndAsync(mp);
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_response_changes_no_count_threshold_list_agenda_front_page_statistic_or_summary()
    {
        const string Counted = AppealSteps.SecondBill;
        await Database.FreeBillAsync(Counted);
        var (participant, _) = await Factory.MemberAsync();
        using var _p = participant;
        (await participant.TakeSideAsync(Counted, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var visitor = NewBrowser();
        var routes = new[]
        {
            AppealRoutes.RecordOf(Counted), AppealRoutes.Appeals, AppealRoutes.AppealsList(null, false, false, null, "participants"),
            AppealRoutes.AppealsList(null, false, false, null, "arguments"), AgendaRoutes.Agenda, "/",
        };
        var before = new Dictionary<string, string>();
        foreach (var route in routes)
        {
            before[route] = Stable(await visitor.Client.GetStringAsync(route));
        }

        var candidatesBefore = await CandidatesAsync(Counted);
        var dailyBefore = await DailyAsync(Counted);
        using var mp = await Factory.MpAccountAsync(MpSteps.Klevinskas);
        using var other = await Factory.MpAccountAsync(MpSteps.Ievaite);
        try
        {
            await mp.Browser.RespondOkAsync(Counted, "for", "Balsuosiu už.");
            await other.Browser.RespondOkAsync(Counted, "against", "Balsuosiu prieš.");
            (await visitor.ResponsesAsync(Counted)).Counts.Answered.ShouldBe(2, "the responses are there");
            foreach (var route in routes)
            {
                Stable(await visitor.Client.GetStringAsync(route)).ShouldBe(before[route], route);
            }

            (await CandidatesAsync(Counted)).ShouldBe(candidatesBefore, "what the summaries read");
            (await DailyAsync(Counted)).ShouldBe(dailyBefore, "the daily statistics");
            (await visitor.RecordAsync(Counted)).Counts.For.ShouldBe(1, "the participant's side only");
        }
        finally
        {
            await EndAsync(mp);
            await EndAsync(other);
            await Database.FreeBillAsync(Counted);
        }
    }

    [Fact]
    public async Task No_answer_page_or_log_says_which_account_wrote_a_response_and_the_part_is_the_same_for_everyone()
    {
        const string Text = "Biuras atsako Seimo nario vardu dėl šio projekto.";
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        await Database.FreeBillAsync(Bill);
        using var mp = await logged.MpAccountAsync(MpSteps.Ievaite);
        var (member, memberKey, memberEmail) = await logged.OfficeMemberAsync(mp, "Slapta Biuro Narė");
        using var _m = member;
        using var _k = memberKey;
        var deleted = false;
        try
        {
            var saved = await member.RespondOkAsync(Bill, "undecided", Text);
            var mpAccountId = (await Database.UserIdAsync(mp.Email)).ToString();
            var memberId = (await Database.UserIdAsync(memberEmail)).ToString();
            string[] secrets = [mp.Email, memberEmail, "Slapta Biuro Narė", mpAccountId, memberId];

            // The public answers: exactly their fields, the response, and nothing of who wrote it.
            using var visitor = logged.NewBrowser();
            var json = await visitor.Client.GetStringAsync(MpResponseRoutes.OnBillOf(Bill));
            var profileJson = await visitor.Client.GetStringAsync(MpResponseRoutes.OfMpOf(mp.Slug));
            using (var document = JsonDocument.Parse(json))
            {
                var item = document.RootElement.GetProperty("items")[0];
                item.EnumerateObject().Select(p => p.Name).ShouldBe(["mp", "response"], ignoreOrder: true);
                item.GetProperty("mp").EnumerateObject().Select(p => p.Name).ShouldBe(["slug", "firstName", "lastName", "faction"], ignoreOrder: true);
                item.GetProperty("response").EnumerateObject().Select(p => p.Name).ShouldBe(ResponseFields, ignoreOrder: true);
                item.GetProperty("response").GetProperty("text").GetString().ShouldBe(Text, "the swept answer holds the response");
            }

            using (var document = JsonDocument.Parse(profileJson))
            {
                document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["slug", "items"], ignoreOrder: true);
                var item = document.RootElement.GetProperty("items")[0];
                item.EnumerateObject().Select(p => p.Name).ShouldBe(["bill", "billOpen", "response"], ignoreOrder: true);
                item.GetProperty("response").GetProperty("text").GetString().ShouldBe(Text);
            }

            // The part of the bill's page is the same for a visitor, the MP's account and the office member who wrote it,
            // apart from the islands' own content; the profile too.
            var forVisitor = MpResponseSteps.WithoutIslands(await PartAsync(visitor, "/seimas/" + Bill));
            forVisitor.ShouldContain(Text);
            forVisitor.ShouldContain("Seimo narys arba jo biuras");
            MpResponseSteps.WithoutIslands(await PartAsync(mp.Browser, "/seimas/" + Bill)).ShouldBe(forVisitor);
            MpResponseSteps.WithoutIslands(await PartAsync(member, "/seimas/" + Bill)).ShouldBe(forVisitor);
            var profileForVisitor = MpResponseSteps.WithoutIslands(await PartAsync(visitor, "/p/" + mp.Slug));
            profileForVisitor.ShouldContain(Text);
            MpResponseSteps.WithoutIslands(await PartAsync(mp.Browser, "/p/" + mp.Slug)).ShouldBe(profileForVisitor);
            foreach (var secret in secrets)
            {
                json.ShouldNotContain(secret, Case.Insensitive);
                profileJson.ShouldNotContain(secret, Case.Insensitive);
                forVisitor.ShouldNotContain(secret, Case.Insensitive);
                profileForVisitor.ShouldNotContain(secret, Case.Insensitive);
            }

            // No log line names an address or an account, nor carries the text.
            var lines = sink.Lines;
            lines.ShouldNotBeEmpty();
            foreach (var secret in secrets.Append(Text))
            {
                lines.ShouldNotContain(l => l.Contains(secret, StringComparison.OrdinalIgnoreCase), secret);
            }

            // The writer's export lists the response; deleting the account leaves it, without the account's number.
            using (var export = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export)))
            {
                var written = export.RootElement.GetProperty("appeals").GetProperty("mpResponses").EnumerateArray().ShouldHaveSingleItem();
                (written.GetProperty("bill").GetString(), written.GetProperty("text").GetString()).ShouldBe((Bill, Text));
            }

            await member.DeleteAccountAsync();
            deleted = true;
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.mp_response WHERE id = @id AND written_by_user_id IS NULL AND status = 'published'", ("id", saved.Id))).ShouldBe(1);
            (await visitor.ResponsesAsync(Bill)).Items.Single().Response.Text.ShouldBe(Text);
        }
        finally
        {
            if (!deleted)
            {
                await member.DeleteAccountAsync();
            }

            await EndAsync(mp);
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_bills_page_lists_10_and_grows_by_10_in_the_order_for_against_undecided_by_last_name_without_javascript()
    {
        const string Listed = AppealSteps.SecondBill;
        await Database.FreeBillAsync(Listed);
        await RemoveSeededAsync();
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.person (id, seimas_person_id, first_name, last_name, slug, gender)
            SELECT gen_random_uuid(), 99700000 + n, 'Vardas' || n, 'Atsakantis' || lpad((26 - n)::text, 2, '0'), 'atsakantis-' || n, NULL
            FROM generate_series(1, 25) AS n;
            INSERT INTO appeals.mp_response (id, bill_number, seimas_person_id, stance, text, status, screening_flags, published_at)
            SELECT gen_random_uuid(), @b, 99700000 + n, (ARRAY['for', 'against', 'undecided'])[(n % 3) + 1], 'Atsakymas nr. ' || n || '.', 'published', '{}', now() - make_interval(mins => n)
            FROM generate_series(1, 25) AS n;
            """,
            ("b", Listed));
        try
        {
            using var visitor = NewBrowser();
            var view = await visitor.ResponsesAsync(Listed);
            view.Items.Count.ShouldBe(25);
            view.Counts.ShouldBe(new MpResponseCountsResponse(25, 8, 9, 8));
            var order = view.Items.Select(i => (i.Response.Stance, i.Mp.LastName)).ToList();
            order.ShouldBe(order.OrderBy(o => o.Stance switch { "for" => 0, "against" => 1, _ => 2 }).ThenBy(o => o.LastName, StringComparer.Ordinal).ToList());

            var page = await PartAsync(visitor, "/seimas/" + Listed);
            page.ShouldContain("<section class=\"sheet stack\" id=\"atsakymai\"", Case.Sensitive, "its own sheet (design 8.16)");
            page.ShouldContain("Atsakė Seimo narių: 25. Balsuos už: 8 · prieš: 9 · dar neapsisprendė: 8.");
            Shown(page).ShouldBe(10);
            page.ShouldContain("Rodoma 10 iš 25");
            page.ShouldContain($"href=\"/seimas/{Listed}?atsakymai=20#atsakymas-11\"");

            var more = await PartAsync(visitor, $"/seimas/{Listed}?atsakymai=20");
            Shown(more).ShouldBe(20);
            more.ShouldContain($"href=\"/seimas/{Listed}?atsakymai=25#atsakymas-21\"");
            var all = await PartAsync(visitor, $"/seimas/{Listed}?atsakymai=25");
            Shown(all).ShouldBe(25);
            all.ShouldNotContain("id=\"responses-more\"");
            foreach (var bad in new[] { "abc", "999", "5", "-1", "20.5" })
            {
                Shown(await PartAsync(visitor, $"/seimas/{Listed}?atsakymai={bad}")).ShouldBe(10, bad);
            }

            // The profile's "Atsakymai": an MP who answered on 12 bills, the most recently written first.
            var slug = (await Database.SlugAsync(MpSteps.Gluosnis))!;
            await Database.ExecuteAsync(
                """
                INSERT INTO appeals.mp_response (id, bill_number, seimas_person_id, stance, text, status, screening_flags, published_at)
                SELECT gen_random_uuid(), 'XVP-T' || lpad(n::text, 3, '0'), @p, 'for', 'Profilio atsakymas nr. ' || n || '.', 'published', '{}', now() - make_interval(days => n)
                FROM generate_series(1, 12) AS n;
                """,
                ("p", MpSteps.Gluosnis.SeimasPersonId));
            var profile = await PartAsync(visitor, "/p/" + slug);
            Shown(profile).ShouldBe(10);
            profile.IndexOf("Profilio atsakymas nr. 1.", StringComparison.Ordinal).ShouldBeLessThan(profile.IndexOf("Profilio atsakymas nr. 2.", StringComparison.Ordinal), "newest first");
            profile.ShouldContain($"href=\"/p/{slug}?atsakymai=12#atsakymas-11\"");
            Shown(await PartAsync(visitor, $"/p/{slug}?atsakymai=12")).ShouldBe(12);
        }
        finally
        {
            await Database.ClearResponsesAsync(MpSteps.Gluosnis.SeimasPersonId);
            await RemoveSeededAsync();
            await Database.FreeBillAsync(Listed);
        }
    }

    /// <summary>
    /// Owner, 2026-11-17 ("I don't see the project in my projects list even though I've written my response"): a first
    /// response follows the bill for the account that wrote it, as a first side does; editing, withdrawing and "Nebesekti" do
    /// not undo or redo it; and the MP's bills hold an answered bill that is in no summary, after the summaries' ones, without
    /// their counts, never another MP's.
    /// </summary>
    [Fact]
    public async Task A_first_response_follows_the_bill_and_the_mps_bills_hold_an_answered_bill_in_no_summary()
    {
        const string Answered = AppealSteps.ThirdBill;
        await Database.FreeBillAsync(Answered);
        using var mp = await Factory.MpAccountAsync(MpSteps.Gluosnis);
        using var other = await Factory.MpAccountAsync(MpSteps.Eglaite);
        try
        {
            (await mp.Browser.GetJsonAsync<MpBillsResponse>(MpResponseRoutes.MyBills)).Items.ShouldNotContain(i => i.Number == Answered);

            await other.Browser.RespondOkAsync(Answered, "against", "Kito Seimo nario atsakymas be suvestinės.");
            await mp.Browser.RespondOkAsync(Answered, "for", "Mano atsakymas be suvestinės.");

            // The writer's account follows the bill; the other MP's account follows its own, and neither learns of the other.
            (await mp.Browser.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.ShouldContain(b => b.Number == Answered);
            (await other.Browser.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.ShouldContain(b => b.Number == Answered);

            // Listed after the summaries' bills, without their counts; open for a change of the answer.
            var bills = await mp.Browser.GetJsonAsync<MpBillsResponse>(MpResponseRoutes.MyBills);
            var row = bills.Items.Single(i => i.Number == Answered);
            (row.For, row.Against, row.Open).ShouldBe(((int?)null, (int?)null, true));
            row.Response!.Stance.ShouldBe("for");
            bills.Items[^1].Number.ShouldBe(Answered);
            var json = await mp.Browser.Client.GetStringAsync(MpResponseRoutes.MyBills);
            json.ShouldNotContain("Kito Seimo nario atsakymas be suvestinės.");
            json.ShouldNotContain(other.Slug);

            // "Nebesekti" keeps the response; editing it follows nothing again; a withdrawn one stays listed.
            (await mp.Browser.SendAsync(HttpMethod.Delete, FollowRoutes.BillOf(Answered))).Dispose();
            await mp.Browser.RespondOkAsync(Answered, "for", "Mano pataisytas atsakymas be suvestinės.");
            (await mp.Browser.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.ShouldNotContain(b => b.Number == Answered);
            (await mp.Browser.WithdrawResponseAsync(Answered)).Dispose();
            (await mp.Browser.GetJsonAsync<MpBillsResponse>(MpResponseRoutes.MyBills)).Items.Single(i => i.Number == Answered).Response!.Status.ShouldBe("withdrawn");
        }
        finally
        {
            await EndAsync(mp);
            await EndAsync(other);
            await Database.FreeBillAsync(Answered);
        }
    }

    [Fact]
    public async Task The_page_of_bills_in_summaries_lists_them_live_with_the_mps_own_response_and_nothing_of_other_mps()
    {
        const string Summarised = AppealSteps.SecondBill;
        var (moderator, administrator) = await StaffAsync();
        await administrator.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);
        await Database.FreeBillAsync(Summarised);
        using var mp = await Factory.MpAccountAsync(MpSteps.Gluosnis);
        using var other = await Factory.MpAccountAsync(MpSteps.Eglaite);
        var (participant, _) = await Factory.MemberAsync();
        using var _p = participant;
        try
        {
            var record = await moderator.Browser.RecordIdAsync(Summarised);
            using (var set = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record), new ModeratorThresholdRequest { Threshold = 1, Statement = "Bandymas." }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
            }

            (await participant.TakeSideAsync(Summarised, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var bills = await mp.Browser.GetJsonAsync<MpBillsResponse>(MpResponseRoutes.MyBills);
            bills.Mp.Slug.ShouldBe(mp.Slug);
            var row = bills.Items.Single(i => i.Number == Summarised);
            (row.For, row.Against, row.Response).ShouldBe((1, 0, (MpResponseResponse?)null));

            // In the summaries' order, by their own rules.
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var listed = DigestLists.InSummariesNow(await scope.ServiceProvider.GetRequiredService<IDigestSource>().CandidatesAsync(CancellationToken.None));
                bills.Items.Select(i => i.Number).ShouldBe(listed.Select(c => c.BillNumber));
            }

            // Live: the MP's own response, never another MP's.
            await other.Browser.RespondOkAsync(Summarised, "against", "Kito Seimo nario atsakymas.");
            await mp.Browser.RespondOkAsync(Summarised, "for", "Mano atsakymas.");
            var json = await mp.Browser.Client.GetStringAsync(MpResponseRoutes.MyBills);
            json.ShouldContain("Mano atsakymas.");
            json.ShouldNotContain("Kito Seimo nario atsakymas.");
            json.ShouldNotContain(other.Slug);
            (await mp.Browser.GetJsonAsync<MpBillsResponse>(MpResponseRoutes.MyBills)).Items.Single(i => i.Number == Summarised).Response!.Stance.ShouldBe("for");

            // A code session reads it; an account without a link does not.
            using (var code = await Factory.CodeSessionAsync(mp.Email))
            {
                (await code.GetJsonAsync<MpBillsResponse>(MpResponseRoutes.MyBills)).Items.ShouldContain(i => i.Number == Summarised);
            }

            await (await participant.Client.GetAsync(MpResponseRoutes.MyBills)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");

            // The page itself is filled in the browser: the server's HTML holds nothing of it.
            var html = WebUtility.HtmlDecode(await mp.Browser.Client.GetStringAsync(MpResponseRoutes.BillsPage));
            html.ShouldContain("Projektai, dėl kurių į jus kreipiamasi");
            html.ShouldNotContain("Mano atsakymas.");
        }
        finally
        {
            await administrator.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
            await EndAsync(mp);
            await EndAsync(other);
            await Database.FreeBillAsync(Summarised);
        }
    }

    /// <summary>
    /// Deletes the MP's account (which ends the link) and the mail written to its official address, so the step-one tests
    /// that read exactly the notices of an address, and the summaries' test, find none of this test's.
    /// </summary>
    private async Task EndAsync(MpAccount mp)
    {
        await mp.Browser.DeleteAccountAsync();
        await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE lower(payload->>'to') = lower(@a)", ("a", mp.Email));
    }

    private static async Task<string> PartAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return MpResponseSteps.ResponsesPart(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>How many responses the part shows (each item is <c>id="atsakymas-{n}"</c>).</summary>
    private static int Shown(string part) => System.Text.RegularExpressions.Regex.Count(part, "<li id=\"atsakymas-\\d+\"");

    private static Task<MpResponseModerationResponse> QueueAsync(StaffMember moderator) =>
        moderator.Browser.GetJsonAsync<MpResponseModerationResponse>(MpResponseRoutes.Moderation);

    private async Task<List<(string Notice, string? Statement)>> NoticesAsync(string to)
    {
        var notices = new List<(string, string?)>();
        await using var connection = new Npgsql.NpgsqlConnection(Database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT payload->>'notice', payload->>'statement' FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) ORDER BY created_at, id", connection);
        command.Parameters.AddWithValue("k", MessageKinds.MpResponseNoticeEmail);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notices.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return notices;
    }

    private async Task RestoreMandateAsync(string? to) =>
        await Database.ExecuteAsync(
            "UPDATE parliament.mandate SET \"to\" = CAST(NULLIF(@to, '') AS date) WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)",
            ("to", to ?? ""), ("p", MpSteps.Eglaite.SeimasPersonId));

    private async Task RemoveSeededAsync()
    {
        await Database.ExecuteAsync("DELETE FROM appeals.mp_response WHERE seimas_person_id BETWEEN 99700001 AND 99700025");
        await Database.ExecuteAsync("DELETE FROM parliament.person WHERE seimas_person_id BETWEEN 99700001 AND 99700025");
    }

    /// <summary>A page without the markers Blazor writes for its islands, which carry a new id on every request.</summary>
    /// <summary>
    /// A page or answer without Blazor's markers and without a bill's last activity (owner, 2026-11-17: a response made public is
    /// the bill's activity, design 8.12.5; only the "naujausia" order reads it, and these comparisons are of the others).
    /// </summary>
    private static string Stable(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(body, "<!--Blazor[^>]*-->", ""),
            "\"lastActivityAt\":(\"[^\"]*\"|null)",
            "\"lastActivityAt\":…");

    private async Task<string> CandidatesAsync(string bill)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var candidates = await scope.ServiceProvider.GetRequiredService<IDigestSource>().CandidatesAsync(CancellationToken.None);
        return JsonSerializer.Serialize(candidates.Where(c => c.BillNumber == bill).ToList());
    }

    private async Task<string> DailyAsync(string bill)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var day = ReasonsToComeSteps.VilniusToday();
        await scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>().AggregateDayAsync(day, CancellationToken.None);
        return await Database.ScalarAsync<string>(
            "SELECT (to_jsonb(d) - 'computed_at')::text FROM appeals.daily_stat d JOIN appeals.bill_record a ON a.id = d.record_id WHERE a.bill_number = @b AND d.day = @d",
            ("b", bill), ("d", day)) ?? "";
    }
}
