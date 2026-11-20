using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 7.2 (owner, 2026-11-20): a moderator corrects a vote's classification (kind, and a stage result's stage) with a
/// statement, audited, and returns it to the rules' classification. Every reader shows the classification in force (the vote,
/// the bill's votes and stages, an MP's latest votes, the history of votes), the vote's page says a moderator corrected it
/// (never who), a vote corrected into the adoption stage closes the bill's record at the closing job's next run, and that
/// closing vote is never corrected out of it again.
/// </summary>
public sealed class VoteClassificationTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    /// <summary>XVP-0412's vote on an amendment at the consideration stage (the rules' classification).</summary>
    private const long Amendment = 99100005;

    /// <summary>A vote inserted on XVP-0618 for the closing test, removed after it.</summary>
    private const long Inserted = 99199720;

    public async ValueTask InitializeAsync()
    {
        await ParliamentSeed.EnsureAsync(Database);
        await ForgetAsync(Amendment);
    }

    public async ValueTask DisposeAsync() => await ForgetAsync(Amendment);

    [Fact]
    public async Task A_moderators_correction_shows_wherever_the_vote_does_until_it_is_returned_to_the_rules()
    {
        using var moderator = await ModeratorAsync();
        using var stranger = NewBrowser();
        var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");
        (await GetAsync<VoteResponse>(stranger, ParliamentRoutes.VoteById(Amendment))).Summary.Vote.Kind.ShouldBe(VoteKinds.Amendment);
        (await AdoptionsAsync(stranger)).ShouldNotContain(Amendment);
        var before = await GetAsync<VoteClassificationResponse>(moderator.Browser, ParliamentRoutes.VoteClassificationOf(Amendment));
        before.ShouldBe(new VoteClassificationResponse(Amendment, VoteKinds.Amendment, BillStages.Consideration, VoteKinds.Amendment, BillStages.Consideration, false, null, null, false));

        try
        {
            using (var corrected = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.VoteClassificationOf(Amendment),
                new VoteClassificationRequest { Kind = VoteKinds.StageResult, Stage = BillStages.Adoption, Statement = "Tai priėmimo balsavimas, ne pasiūlymas." }))
            {
                corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await corrected.Content.ReadAsStringAsync());
            }

            var state = await GetAsync<VoteClassificationResponse>(moderator.Browser, ParliamentRoutes.VoteClassificationOf(Amendment));
            state.Kind.ShouldBe(VoteKinds.StageResult);
            state.Stage.ShouldBe(BillStages.Adoption);
            state.RulesKind.ShouldBe(VoteKinds.Amendment);
            state.Corrected.ShouldBeTrue();
            state.CorrectedAt.ShouldNotBeNull();
            state.Statement.ShouldBe("Tai priėmimo balsavimas, ne pasiūlymas.");

            // Every reader: the vote, the bill's votes and its stages, an MP's latest votes, the history of votes.
            var vote = await GetAsync<VoteResponse>(stranger, ParliamentRoutes.VoteById(Amendment));
            (vote.Summary.Vote.Kind, vote.Summary.Vote.Stage, vote.ClassificationCorrected).ShouldBe((VoteKinds.StageResult, BillStages.Adoption, true));
            (await GetAsync<BillResponse>(stranger, ParliamentRoutes.BillByNumber("XVP-0412"))).Votes.Single(v => v.Vote.Id == Amendment).Vote.Kind.ShouldBe(VoteKinds.StageResult);
            (await GetAsync<MpProfileResponse>(stranger, ParliamentRoutes.MpBySlug("jonas-petraitis"))).RecentVotes.Single(v => v.Vote.Id == Amendment).Vote.Stage.ShouldBe(BillStages.Adoption);
            (await AdoptionsAsync(stranger)).ShouldContain(Amendment);

            var bill = await PageAsync(stranger, "/seimas/XVP-0412");
            var adoption = bill[bill.IndexOf("id=\"stage-adoption\"", StringComparison.Ordinal)..];
            adoption[..adoption.IndexOf("</li>", StringComparison.Ordinal)].ShouldContain($"href=\"/seimas/balsavimai/{Amendment}\"");

            // The vote's page says a moderator corrected it, beside the stage, and never who; only a moderator sees the part.
            var page = await PageAsync(stranger, $"/seimas/balsavimai/{Amendment}");
            var hint = AppealPageTests.HintOf(page, "classification-corrected");
            hint.Trigger.ShouldBe("Klasifikaciją pataisė moderatoriai");
            hint.More.ShouldBe("/metodika#stadijos");
            page.ShouldContain(" · Priėmimas · ");
            page.ShouldNotContain("id=\"vote-moderation\"");
            page.ShouldNotContain("Tai priėmimo balsavimas", Case.Sensitive, "the statement is the moderators'");
            var staffPage = await PageAsync(moderator.Browser, $"/seimas/balsavimai/{Amendment}");
            staffPage.ShouldContain("id=\"vote-moderation\"");
            staffPage.ShouldContain("Pataisė moderatorius ");
            staffPage.ShouldContain("Taisyklių klasifikacija: Dėl pasiūlymo · Svarstymas.");

            using (var reset = await moderator.Browser.SendAsync(HttpMethod.Delete, ParliamentRoutes.VoteClassificationOf(Amendment),
                new VoteClassificationResetRequest { Statement = "Taisyklės buvo teisios." }))
            {
                reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());
            }

            (await GetAsync<VoteClassificationResponse>(moderator.Browser, ParliamentRoutes.VoteClassificationOf(Amendment))).ShouldBe(before);
            var back = await GetAsync<VoteResponse>(stranger, ParliamentRoutes.VoteById(Amendment));
            (back.Summary.Vote.Kind, back.Summary.Vote.Stage, back.ClassificationCorrected).ShouldBe((VoteKinds.Amendment, BillStages.Consideration, false));
            (await AdoptionsAsync(stranger)).ShouldNotContain(Amendment);
            (await PageAsync(stranger, $"/seimas/balsavimai/{Amendment}")).ShouldNotContain("id=\"classification-corrected\"");

            // Audited with the classification before and after and the statement; nothing about any person.
            var audited = await Database.ScalarAsync<string>(
                "SELECT string_agg(action || ' ' || (details->'before'->>'kind') || '>' || (details->'after'->>'kind') || '/' || coalesce(details->'after'->>'stage', '-') || ' ' || (details->>'statement'), ' | ' ORDER BY occurred_at) FROM audit.event WHERE target = @t AND occurred_at >= @since",
                ("t", $"vote:{Amendment}"), ("since", since));
            audited.ShouldBe("vote.reclassified amendment>stage-result/adoption Tai priėmimo balsavimas, ne pasiūlymas. | vote.classification-reset stage-result>amendment/consideration Taisyklės buvo teisios.");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target = @t AND details::text LIKE '%@%'", ("t", $"vote:{Amendment}"))).ShouldBe(0);
        }
        finally
        {
            await ForgetAsync(Amendment);
        }
    }

    [Fact]
    public async Task Only_a_moderator_corrects_with_a_statement_and_a_correction_must_change_something()
    {
        using var moderator = await ModeratorAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");
        var route = ParliamentRoutes.VoteClassificationOf(Amendment);
        var procedural = new VoteClassificationRequest { Kind = VoteKinds.Procedural, Statement = "Procedūrinis." };

        foreach (var (who, browser) in new[] { ("member", member), ("visitor", NewBrowser()) })
        {
            using (browser == member ? null : browser)
            {
                using var read = await browser.Client.GetAsync(route);
                read.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], who);
                using var put = await browser.SendAsync(HttpMethod.Put, route, procedural);
                put.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], who);
                using var delete = await browser.SendAsync(HttpMethod.Delete, route, new VoteClassificationResetRequest { Statement = "Ne." });
                delete.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], who);
            }
        }

        (await PageAsync(member, $"/seimas/balsavimai/{Amendment}")).ShouldNotContain("id=\"vote-moderation\"");
        (await GetAsync<VoteResponse>(member, ParliamentRoutes.VoteById(Amendment))).Summary.Vote.Kind.ShouldBe(VoteKinds.Amendment, "nothing changed");

        using (var noStatement = await moderator.Browser.SendAsync(HttpMethod.Put, route, procedural with { Statement = " " }))
        {
            noStatement.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await noStatement.ProblemAsync()).GetProperty("errors").TryGetProperty("statement", out _).ShouldBeTrue();
        }

        using (var noStage = await moderator.Browser.SendAsync(HttpMethod.Put, route, new VoteClassificationRequest { Kind = VoteKinds.StageResult, Statement = "Be stadijos." }))
        {
            noStage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await noStage.ProblemAsync()).GetProperty("errors").TryGetProperty("stage", out _).ShouldBeTrue();
        }

        using (var resetWithout = await moderator.Browser.SendAsync(HttpMethod.Delete, route, new VoteClassificationResetRequest()))
        {
            resetWithout.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var missing = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.VoteClassificationOf(99999999), procedural))
        {
            await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, "vote.not-found");
        }

        using (var same = await moderator.Browser.SendAsync(HttpMethod.Put, route, new VoteClassificationRequest { Kind = VoteKinds.Amendment, Statement = "Tas pats." }))
        {
            await same.ShouldBeProblemAsync(HttpStatusCode.Conflict, "vote-classification.unchanged");
        }

        using (var nothingToReset = await moderator.Browser.SendAsync(HttpMethod.Delete, route, new VoteClassificationResetRequest { Statement = "Grąžinti." }))
        {
            await nothingToReset.ShouldBeProblemAsync(HttpStatusCode.Conflict, "vote-classification.not-corrected");
        }

        try
        {
            using (var corrected = await moderator.Browser.SendAsync(HttpMethod.Put, route, procedural))
            {
                corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await corrected.Content.ReadAsStringAsync());
            }

            (await GetAsync<VoteResponse>(member, ParliamentRoutes.VoteById(Amendment))).Summary.Vote.Stage.ShouldBeNull("a procedural vote has no stage");
            using var toRules = await moderator.Browser.SendAsync(HttpMethod.Put, route, new VoteClassificationRequest { Kind = VoteKinds.Amendment, Statement = "Atgal." });
            await toRules.ShouldBeProblemAsync(HttpStatusCode.Conflict, "vote-classification.rules");
        }
        finally
        {
            await ForgetAsync(Amendment);
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target = @t AND action LIKE 'vote.%' AND occurred_at >= @since", ("t", $"vote:{Amendment}"), ("since", since))).ShouldBe(1, "refusals write nothing");
    }

    [Fact]
    public async Task A_vote_corrected_into_the_adoption_stage_closes_the_record_and_then_stays_the_adoption_stage_result()
    {
        using var moderator = await ModeratorAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");

        // A vote the rules cannot classify ("other"), as the import would store it.
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Dėl projekto', type, 'Other', NULL, false, true, "for", against, abstain, voted, seats
            FROM parliament.vote WHERE id = 99100102
            """,
            ("id", Inserted));
        try
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>();
            (await maintenance.CloseFinishedAsync(CancellationToken.None)).ShouldBe(0, "no adoption-stage result yet");

            var route = ParliamentRoutes.VoteClassificationOf(Inserted);
            using (var corrected = await moderator.Browser.SendAsync(HttpMethod.Put, route,
                new VoteClassificationRequest { Kind = VoteKinds.StageResult, Stage = BillStages.Adoption, Statement = "Galutinis balsavimas." }))
            {
                corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await corrected.Content.ReadAsStringAsync());
            }

            (await GetAsync<VoteClassificationResponse>(moderator.Browser, route)).ClosesRecord.ShouldBeFalse("the job has not run");
            (await maintenance.CloseFinishedAsync(CancellationToken.None)).ShouldBe(1, "the closing job's next run closes the record");
            using var stranger = NewBrowser();
            var closed = await stranger.RecordAsync(AppealSteps.ThirdBill);
            (closed.Status, closed.ClosingVoteId).ShouldBe(("closed", (long?)Inserted));

            // The closing vote stays the adoption-stage result: a record closed by the vote is never reopened (design 8.1).
            (await GetAsync<VoteClassificationResponse>(moderator.Browser, route)).ClosesRecord.ShouldBeTrue();
            using (var away = await moderator.Browser.SendAsync(HttpMethod.Put, route, new VoteClassificationRequest { Kind = VoteKinds.Other, Statement = "Klaida." }))
            {
                await away.ShouldBeProblemAsync(HttpStatusCode.Conflict, "vote-classification.closing-vote");
                (await away.ProblemAsync()).GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("atverti iš naujo negalima");
            }

            using (var reset = await moderator.Browser.SendAsync(HttpMethod.Delete, route, new VoteClassificationResetRequest { Statement = "Klaida." }))
            {
                await reset.ShouldBeProblemAsync(HttpStatusCode.Conflict, "vote-classification.closing-vote");
            }

            (await PageAsync(moderator.Browser, $"/seimas/balsavimai/{Inserted}")).ShouldContain("id=\"vote-classification-closing\"");
            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Status.ShouldBe("closed");
        }
        finally
        {
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", Inserted));
        }
    }

    /// <summary>The adoption-stage results in the history of votes on XVP-0412 (design 8.18, "Tik priėmimai").</summary>
    private static async Task<IReadOnlyList<long>> AdoptionsAsync(TestBrowser browser) =>
        (await GetAsync<VoteListResponse>(browser, ParliamentRoutes.Votes + "?kind=adoptions&q=XVP-0412")).Items.Select(i => i.Summary.Vote.Id).ToList();

    private static async Task<T> GetAsync<T>(TestBrowser browser, string url)
    {
        using var response = await browser.Client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<string> PageAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The vote back as the import left it: no correction, the rules' classification.</summary>
    private Task ForgetAsync(long voteId) => Database.ExecuteAsync(
        """
        UPDATE parliament.vote SET kind = 'Amendment', stage = 'Consideration', kind_override = NULL, stage_override = NULL,
          overridden_at = NULL, overridden_by = NULL, override_statement = NULL WHERE id = @id
        """,
        ("id", voteId));
}
