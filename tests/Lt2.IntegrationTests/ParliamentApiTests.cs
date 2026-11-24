using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Meta;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 7.4: every public parliament operation over HTTP, on the invented parliament.</summary>
public sealed class ParliamentApiTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await ParliamentSeed.EnsureAsync(Database);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<T> GetAsync<T>(string url)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task The_current_term_is_term_10() =>
        (await GetAsync<TermResponse>(ParliamentRoutes.CurrentTerm)).ShouldBe(new TermResponse(10, "2024–2028 m. kadencija", new DateOnly(2024, 11, 14), null));

    [Fact]
    public async Task The_mps_are_listed_in_lithuanian_alphabetical_order_of_last_name_never_by_a_statistic()
    {
        var mps = await GetAsync<MpListResponse>(ParliamentRoutes.Mps);

        mps.Total.ShouldBe(14);
        mps.NextCursor.ShouldBeNull();
        mps.Items.Select(m => m.LastName).ShouldBe(
        [
            "Alksninis", "Ąžuolaitis", "Beržinytė", "Eglaitė", "Gluosnis", "Ievaitė", "Jonaitienė", "Kadagytė", "Klevinskas",
            "Liepaitis", "Petraitis", "Pušinskienė", "Šermukšnytė", "Uosytė",
        ]);
        mps.Items.Single(m => m.LastName == "Alksninis").Sitting.ShouldBeFalse();
        mps.Items.Single(m => m.LastName == "Liepaitis").Faction!.Name.ShouldBe("Regionų aljanso frakcija");
    }

    [Fact]
    public async Task The_mp_list_filters_by_faction_committee_and_name_without_diacritics()
    {
        (await GetAsync<MpListResponse>(ParliamentRoutes.Mps + "?faction=reformu-partijos-frakcija")).Items.Select(m => m.LastName)
            .ShouldBe(["Ąžuolaitis", "Beržinytė", "Klevinskas", "Petraitis"]);
        (await GetAsync<MpListResponse>(ParliamentRoutes.Mps + "?committee=biudzeto-ir-finansu-komitetas")).Total.ShouldBe(6);
        (await GetAsync<MpListResponse>(ParliamentRoutes.Mps + "?q=azuol")).Items.Single().LastName.ShouldBe("Ąžuolaitis");
        (await GetAsync<MpListResponse>(ParliamentRoutes.Mps + "?q=nera-tokio")).Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_mp_list_pages_with_a_cursor()
    {
        using var browser = NewBrowser();
        var first = await browser.Client.GetFromJsonAsync<MpListResponse>(ParliamentRoutes.Mps + "?cursor=10");

        first!.Items.Select(m => m.LastName).ShouldBe(["Petraitis", "Pušinskienė", "Šermukšnytė", "Uosytė"]);
    }

    [Fact]
    public async Task A_profile_has_the_mandate_memberships_contacts_the_two_statistics_with_their_counts_and_recent_votes()
    {
        var jonas = await GetAsync<MpProfileResponse>(ParliamentRoutes.MpBySlug("jonas-petraitis"));

        jonas.Mp.ShouldBe(new MpListItem("jonas-petraitis", "Jonas", "Petraitis", "V", true, new BodyLink("reformu-partijos-frakcija", "Reformų partijos frakcija", "faction"), "Reformų partija"));
        jonas.Mandate.ShouldBe(new MandateResponse(new DateOnly(2024, 11, 14), null, "Reformų partija", "Pagal sąrašą", null, 2));
        jonas.Memberships.Select(m => (m.Body.Kind, m.Role)).ShouldBe([("faction", "Frakcijos narys"), ("committee", "Komiteto narys")]);
        jonas.Contacts.ShouldBe([new ContactResponse("El. p.", "pavyzdys.jonas.petraitis@lrs.lt")]);
        jonas.Statistics.ShouldBe(new MpStatisticsResponse(7, 7, 1m, 7, 5, 5m / 7m, 0));

        jonas.RecentVotes.Count.ShouldBe(7);
        jonas.RecentVotes[0].Vote.Id.ShouldBe(99100102);
        jonas.RecentVotes.ShouldAllBe(v => v.Choice == "for" || v.Choice == "against" || v.Choice == "abstain");
        var history = await GetAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug("jonas-petraitis"));
        var consensus = history.Items.Single(v => v.Vote.IsConsensus);
        consensus.Choice.ShouldBeNull();
        consensus.Faction.ShouldBeNull();
        var presentation = jonas.RecentVotes.Single(v => v.Vote.Id == 99100002);
        presentation.Choice.ShouldBe("against");
        presentation.Faction.ShouldBe(new FactionSplitResponse("RPF", new BodyLink("reformu-partijos-frakcija", "Reformų partijos frakcija", "faction"), "for", 4, 1, 0, 0));
    }

    [Fact]
    public async Task A_former_mp_keeps_the_profile_and_their_votes_end_with_the_mandate()
    {
        var tadas = await GetAsync<MpProfileResponse>(ParliamentRoutes.MpBySlug("tadas-alksninis"));

        tadas.Mp.Sitting.ShouldBeFalse();
        tadas.Mandate.To.ShouldBe(new DateOnly(2026, 4, 15));
        tadas.RecentVotes.Select(v => v.Vote.Id).ShouldBe([99100002L, 99100001]);
        tadas.Statistics!.Participation.ShouldBe(2m / 3m);
    }

    [Fact]
    public async Task A_mixed_group_member_has_no_faction_and_votes_there_have_no_faction_split_or_figure()
    {
        var rasa = await GetAsync<MpProfileResponse>(ParliamentRoutes.MpBySlug("rasa-ievaite"));

        rasa.Mp.Faction.ShouldBeNull();
        rasa.Memberships.Where(m => m.Current).Select(m => (m.Body.Name, m.Body.Kind)).ShouldBe([("Mišri Seimo narių grupė", "group")]);
        rasa.Statistics.ShouldBe(new MpStatisticsResponse(7, 7, 1m, 3, 3, 1m, 0));
        rasa.RecentVotes.Single(v => v.Vote.Id == 99100005).Faction.ShouldBeNull();
        rasa.RecentVotes.Single(v => v.Vote.Id == 99100004).Faction!.Label.ShouldBe("RPF");

        (await GetAsync<VoteResponse>(ParliamentRoutes.VoteById(99100005))).Factions.Select(f => f.Label).ShouldBe(["RAF", "RPF", "PSF"]);
        (await GetAsync<MpListResponse>(ParliamentRoutes.Mps + "?faction=reformu-partijos-frakcija")).Items.ShouldNotContain(m => m.LastName == "Ievaitė");
    }

    [Fact]
    public async Task The_votes_of_an_mp_come_newest_first_with_a_cursor()
    {
        var votes = await GetAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug("aurelija-kadagyte"));

        votes.Items.Select(v => v.Vote.Id).ShouldBe([99100102L, 99100101, 99100006, 99100005]);
        votes.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Recent_votes_exclude_missed_votes_that_the_full_history_keeps()
    {
        const string slug = "darius-liepaitis";
        var profile = await GetAsync<MpProfileResponse>(ParliamentRoutes.MpBySlug(slug));
        var history = await GetAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug(slug));

        history.Items.ShouldContain(v => v.Choice == "did-not-vote");
        profile.RecentVotes.ShouldNotBeEmpty();
        profile.RecentVotes.Select(v => v.Vote.Id).ShouldBe(history.Items
            .Where(v => v.Choice is "for" or "against" or "abstain").Select(v => v.Vote.Id));
    }

    [Theory]
    [InlineData("/api/v1/mps/nera-tokio")]
    [InlineData("/api/v1/mps/nera-tokio/votes")]
    [InlineData("/api/v1/factions/nera-tokios")]
    [InlineData("/api/v1/committees/nera-tokio")]
    [InlineData("/api/v1/bills/XVP-9999")]
    [InlineData("/api/v1/votes/12345")]
    public async Task Anything_unknown_is_a_404_problem_with_a_code(string url)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code.ShouldBe("not-found");
    }

    [Fact]
    public async Task Factions_are_listed_with_their_current_members_and_a_faction_shows_its_recent_positions()
    {
        var factions = await GetAsync<List<BodyListItem>>(ParliamentRoutes.Factions);
        factions.Select(f => (f.Name, f.Members)).ShouldBe(
        [
            ("Pilietinės sąjungos frakcija", 3), ("Reformų partijos frakcija", 4), ("Regionų aljanso frakcija", 5),
        ]);

        var raf = await GetAsync<BodyResponse>(ParliamentRoutes.FactionBySlug("regionu-aljanso-frakcija"));
        raf.Members.Where(m => m.Current).Select(m => m.Mp.LastName).ShouldBe(["Eglaitė", "Gluosnis", "Kadagytė", "Liepaitis", "Šermukšnytė"]);
        raf.Members.Where(m => !m.Current).Select(m => m.Mp.LastName).ShouldBe(["Alksninis"]);
        raf.RecentVotes.Count.ShouldBe(7);
        raf.RecentVotes.Single(v => v.Vote.Id == 99100002).Split.Position.ShouldBe("free");
    }

    [Fact]
    public async Task Committees_are_listed_and_a_committee_shows_its_members_and_roles()
    {
        (await GetAsync<List<BodyListItem>>(ParliamentRoutes.Committees)).Select(c => c.Name)
            .ShouldBe(["Biudžeto ir finansų komitetas", "Socialinių reikalų ir darbo komitetas"]);

        var budget = await GetAsync<BodyResponse>(ParliamentRoutes.CommitteeBySlug("biudzeto-ir-finansu-komitetas"));
        budget.Body.Kind.ShouldBe("committee");
        budget.Members.Single(m => m.Mp.LastName == "Beržinytė").Role.ShouldBe("Komiteto pirmininkė");
        budget.RecentVotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bills_come_latest_vote_first_and_search_by_title()
    {
        (await GetAsync<BillListResponse>(ParliamentRoutes.Bills)).Items.Select(b => b.Number).ShouldBe(["XVP-0618", "XVP-0412", "XVP-0533"]);
        (await GetAsync<BillListResponse>(ParliamentRoutes.Bills + "?q=pajamu")).Items.Single().Number.ShouldBe("XVP-0412");
    }

    [Fact]
    public async Task A_bill_lists_its_votes_with_the_official_totals()
    {
        var bill = await GetAsync<BillResponse>(ParliamentRoutes.BillByNumber("XVP-0412"));

        bill.Title.ShouldBe("Gyventojų pajamų mokesčio įstatymo pataisa");
        bill.Votes.Select(v => (v.Vote.Kind, v.Vote.Stage, v.Totals.For, v.Totals.Against)).ShouldBe(
        [
            ("stage-result", "presentation", (int?)71, (int?)38),
            ("amendment", "consideration", 40, 60),
            ("stage-result", "consideration", 74, 44),
        ]);
    }

    [Fact]
    public async Task A_vote_has_its_totals_faction_positions_and_every_ballot()
    {
        var vote = await GetAsync<VoteResponse>(ParliamentRoutes.VoteById(99100002));

        vote.Summary.Totals.ShouldBe(new VoteTotalsResponse(71, 38, 14, 123, 141));
        vote.Factions.Select(f => (f.Label, f.Position)).ShouldBe([("RPF", "for"), ("PSF", "against"), ("RAF", "free")]);
        vote.Ballots.Count.ShouldBe(13);
        vote.Ballots.Single(b => b.Mp.Slug == "jonas-petraitis").ShouldBe(new BallotResponse(new MpLink("jonas-petraitis", "Jonas", "Petraitis"), "RPF", "against"));
        vote.AgendaItemTitle.ShouldBe("Gyventojų pajamų mokesčio įstatymo pataisa");
    }

    [Fact]
    public async Task A_consensus_vote_has_no_numbers_and_the_official_mismatch_note_comes_with_its_result()
    {
        var consensus = await GetAsync<VoteResponse>(ParliamentRoutes.VoteById(99100003));
        consensus.Summary.Vote.IsConsensus.ShouldBeTrue();
        consensus.Summary.Totals.ShouldBe(new VoteTotalsResponse(null, null, null, null, null));
        consensus.Ballots.ShouldBeEmpty();
        consensus.Factions.ShouldBeEmpty();

        (await GetAsync<VoteResponse>(ParliamentRoutes.VoteById(99100004))).Summary.OfficialNote.ShouldNotBeNull().ShouldContain("neatitinka protokole");
    }

    [Fact]
    public async Task The_imports_say_when_the_open_data_was_last_imported_and_that_it_is_invented()
    {
        // A fresh host: the answer is cached for a minute, and the shared one may have asked before the seed.
        await using var fresh = new Lt2Factory(Database);
        using var browser = fresh.NewBrowser();
        var imports = (await browser.Client.GetFromJsonAsync<ImportsResponse>(ParliamentRoutes.Imports))!;

        imports.ParliamentDataInvented.ShouldBeTrue();
        var seimas = imports.Sources.Single();
        seimas.Source.ShouldBe("seimas-open-data");
        seimas.LastSuccessAt.ShouldNotBeNull();
        seimas.LastRunStatus.ShouldBe("succeeded");
    }

    [Fact]
    public async Task Public_answers_carry_an_etag_and_a_matching_if_none_match_gets_304()
    {
        using var browser = NewBrowser();
        using var first = await browser.Client.GetAsync(ParliamentRoutes.MpBySlug("ona-jonaitiene"));
        var etag = first.Headers.ETag.ShouldNotBeNull();
        first.Headers.CacheControl!.Public.ShouldBeTrue();

        using var again = new HttpRequestMessage(HttpMethod.Get, ParliamentRoutes.MpBySlug("ona-jonaitiene"));
        again.Headers.IfNoneMatch.Add(etag);
        using var second = await browser.Client.SendAsync(again);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task The_openapi_document_lists_every_parliament_operation()
    {
        using var browser = NewBrowser();
        var document = await browser.Client.GetStringAsync("/openapi/v1.json");

        foreach (var path in new[] { "/api/v1/terms/current", "/api/v1/mps", "/api/v1/mps/{slug}", "/api/v1/mps/{slug}/votes", "/api/v1/factions", "/api/v1/factions/{slug}",
            "/api/v1/committees", "/api/v1/committees/{slug}", "/api/v1/bills", "/api/v1/bills/{number}", "/api/v1/votes", "/api/v1/votes/{id}", "/api/v1/meta/imports" })
        {
            document.ShouldContain($"\"{path}\"", customMessage: path);
        }
    }
}
