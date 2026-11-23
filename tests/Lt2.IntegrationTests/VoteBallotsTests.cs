using System.Net;
using System.Text.RegularExpressions;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.17: the list of the MPs' ballots on a vote's page as the server renders it: searchable, by faction and ballot, 20 at first
/// and 50 more each time (owner, 2026-11-19), all in the address, without JavaScript. A vote with 141 ballots is invented for each test: the
/// 13 MPs of the invented parliament, 127 more (every tenth without a faction) and "Indrė Žolynaitė" (RPF, did not vote),
/// found without diacritics; its faction positions are vote 99100002's, so the factions have their names.
/// </summary>
public sealed partial class VoteBallotsTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const long Vote = 99199701;
    private const long EmptyVote = 99199702;
    private const string Page = "/seimas/balsavimai/99199701";

    public async ValueTask InitializeAsync()
    {
        await ParliamentSeed.EnsureAsync(Database);
        await RemoveAsync();
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.person (id, seimas_person_id, first_name, last_name, slug, gender)
            SELECT gen_random_uuid(), 99600000 + n, 'Vardas' || n, 'Bandomasis' || lpad(n::text, 3, '0'), 'bandomasis-ballot-' || n, NULL
            FROM generate_series(1, 127) AS n;
            INSERT INTO parliament.person (id, seimas_person_id, first_name, last_name, slug, gender)
            VALUES (gen_random_uuid(), 99600200, 'Indrė', 'Žolynaitė', 'bandomoji-indre-zolynaite', NULL);
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT v.id, agenda_item_id, NULL, held_at, wording, type, kind, NULL, false, true, "for", against, abstain, voted, seats
            FROM parliament.vote, (VALUES (@vote), (@empty)) AS v(id) WHERE parliament.vote.id = 99100101;
            INSERT INTO parliament.ballot (vote_id, person_id, faction_label, faction_id, choice)
            SELECT @vote, person_id, faction_label, faction_id, choice FROM parliament.ballot WHERE vote_id = 99100002;
            INSERT INTO parliament.ballot (vote_id, person_id, faction_label, faction_id, choice)
            SELECT @vote, id, CASE WHEN seimas_person_id % 10 = 5 THEN NULL WHEN seimas_person_id % 2 = 0 THEN 'RPF' ELSE 'PSF' END, NULL,
                   (ARRAY['DidNotVote', 'For', 'Against', 'Abstain'])[(seimas_person_id % 4)::int + 1]
            FROM parliament.person WHERE seimas_person_id BETWEEN 99600001 AND 99600200;
            INSERT INTO parliament.faction_position (vote_id, faction_label, faction_id, position, "for", against, abstain, did_not_vote)
            SELECT @vote, faction_label, faction_id, position, "for", against, abstain, did_not_vote FROM parliament.faction_position WHERE vote_id = 99100002;
            """,
            ("vote", Vote), ("empty", EmptyVote));
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.ballot WHERE vote_id = @v", ("v", Vote))).ShouldBe(141);
    }

    public async ValueTask DisposeAsync() => await RemoveAsync();

    private async Task RemoveAsync()
    {
        await Database.ExecuteAsync("DELETE FROM parliament.faction_position WHERE vote_id IN (@a, @b)", ("a", Vote), ("b", EmptyVote));
        await Database.ExecuteAsync("DELETE FROM parliament.ballot WHERE vote_id IN (@a, @b)", ("a", Vote), ("b", EmptyVote));
        await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id IN (@a, @b)", ("a", Vote), ("b", EmptyVote));
        await Database.ExecuteAsync("DELETE FROM parliament.person WHERE seimas_person_id BETWEEN 99600001 AND 99600200");
    }

    private async Task<string> OkAsync(string path)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<long> CountAsync(string choice) =>
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.ballot WHERE vote_id = @v AND choice = @c", ("v", Vote), ("c", choice)));

    /// <summary>The part of the page that is the list (the seat map names every MP too).</summary>
    private static string List(string html)
    {
        var start = html.IndexOf("id=\"ballots\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the list is on the page");
        return html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task A_vote_with_141_ballots_shows_20_on_a_sheet_and_grows_by_50_landing_on_the_first_row_added()
    {
        var html = await OkAsync(Page);
        html.ShouldContain("<section class=\"sheet stack\" id=\"ballots\"");
        var list = List(html);

        Row().Count(list).ShouldBe(20);
        list.ShouldContain("Rodoma 20 iš 141");
        list.ShouldContain("href=\"/seimas/balsavimai/99199701?rodyti=70\"");

        var more = List(await OkAsync(Page + "?rodyti=70"));
        Row().Count(more).ShouldBe(70);
        more.ShouldContain("id=\"ballot-21\"");
        more.ShouldContain("Rodoma 70 iš 141");
        more.ShouldContain("?rodyti=120");

        var all = List(await OkAsync(Page + "?rodyti=141"));
        Row().Count(all).ShouldBe(141);
        all.ShouldContain("Rodoma 141 iš 141");
        all.ShouldNotContain("id=\"ballots-more\"");
    }

    [Fact]
    public async Task The_rows_are_in_the_order_of_the_last_names()
    {
        var list = List(await OkAsync(Page + "?rodyti=141"));
        var names = Name().Matches(list).Select(m => m.Groups[1].Value).ToList();

        names.Count.ShouldBe(141);
        names[0].ShouldBe("Tadas Alksninis", "Ą sorts with A: Alksninis before Ąžuolaitis");
        names[^1].ShouldBe("Indrė Žolynaitė");
        var lastNames = names.Select(n => n[(n.LastIndexOf(' ') + 1)..]).ToList();
        lastNames.ShouldBe(lastNames.Order(StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("lt-LT"), true)).ToList());
    }

    [Theory]
    [InlineData("uz", "For", "for", "Už")]
    [InlineData("pries", "Against", "against", "Prieš")]
    [InlineData("susilaike", "Abstain", "abstain", "Susilaikė")]
    [InlineData("nebalsavo", "DidNotVote", "did-not-vote", "Nebalsavo")]
    public async Task Each_ballot_link_shows_exactly_the_mps_with_that_ballot_and_its_count(string slug, string stored, string choice, string word)
    {
        var expected = await CountAsync(stored);
        var plain = List(await OkAsync(Page));
        plain.ShouldContain($"href=\"/seimas/balsavimai/99199701?balsas={slug}\" id=\"ballots-{choice}\">{word} <span class=\"num\">{expected}</span></a>");

        var list = List(await OkAsync($"{Page}?balsas={slug}&rodyti=141"));

        list.ShouldContain($"<b aria-current=\"true\" id=\"ballots-{choice}\">");
        Row().Count(list).ShouldBe((int)expected);
        Regex.Count(list, "<span class=\"choice[^\"]*\">([^<]+)</span>").ShouldBe((int)expected);
        Regex.Matches(list, "<span class=\"choice[^\"]*\">([^<]+)</span>").ShouldAllBe(m => m.Groups[1].Value == word);
        list.ShouldContain($"Rodoma {expected} iš {expected}");
    }

    [Fact]
    public async Task A_search_without_diacritics_finds_the_mp_and_combines_with_the_ballot()
    {
        var found = List(await OkAsync(Page + "?q=zolynaite"));
        Row().Count(found).ShouldBe(1);
        found.ShouldContain("Indrė Žolynaitė");
        found.ShouldContain("value=\"zolynaite\"");
        found.ShouldContain("href=\"/seimas/balsavimai/99199701?q=zolynaite&balsas=nebalsavo\"");

        var withBallot = List(await OkAsync(Page + "?q=zolynaite&balsas=nebalsavo"));
        Row().Count(withBallot).ShouldBe(1);
        withBallot.ShouldContain("<input type=\"hidden\" name=\"balsas\" value=\"nebalsavo\"");

        var otherBallot = List(await OkAsync(Page + "?q=zolynaite&balsas=uz"));
        Row().Count(otherBallot).ShouldBe(0);
        otherBallot.ShouldContain("Nerasta nė vieno Seimo nario.");
        otherBallot.ShouldContain("href=\"/seimas/balsavimai/99199701?balsas=uz\"", customMessage: "clearing keeps the ballot");

        foreach (var faction in new[] { "rpf", "reformu+partijos", "pilietines+sajungos" })
        {
            var byFaction = List(await OkAsync(Page + "?q=" + faction));
            Row().Count(byFaction).ShouldBe(0, "the search finds by name only: " + faction);
            byFaction.ShouldContain("Nerasta nė vieno Seimo nario.");
        }
    }

    [Fact]
    public async Task The_search_and_the_faction_are_a_plain_get_form_landing_on_the_list()
    {
        var list = List(await OkAsync(Page));

        list.ShouldContain("<form class=\"filters\" method=\"get\" action=\"/seimas/balsavimai/99199701\" data-enhance role=\"search\"");
        list.ShouldContain("name=\"q\"");
        list.ShouldContain("<select class=\"input\" id=\"ballot-faction\" name=\"frakcija\">");
        list.ShouldNotContain("name=\"balsas\"", customMessage: "no ballot chosen, nothing hidden");
        list.ShouldNotContain("id=\"ballot-clear\"", customMessage: "nothing to clear");
    }

    [Fact]
    public async Task The_faction_dropdown_lists_the_factions_with_a_ballot_by_name_and_the_mps_without_a_faction_last()
    {
        var list = List(await OkAsync(Page));
        var options = Option().Matches(list).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

        options.ShouldBe(
        [
            ("", "Visos frakcijos"),
            ("pilietines-sajungos-frakcija", "Pilietinės sąjungos frakcija"),
            ("reformu-partijos-frakcija", "Reformų partijos frakcija"),
            ("regionu-aljanso-frakcija", "Regionų aljanso frakcija"),
            ("be-frakcijos", "Be frakcijos"),
        ]);

        var plain = List(await OkAsync("/seimas/balsavimai/99100002"));
        plain.ShouldNotContain("value=\"be-frakcijos\"", customMessage: "every MP of that vote has a faction");
    }

    [Fact]
    public async Task A_faction_shows_exactly_its_mps_and_the_counts_are_within_it()
    {
        var expected = await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.ballot WHERE vote_id = @v AND faction_label = 'RPF'", ("v", Vote));
        var list = List(await OkAsync(Page + "?frakcija=reformu-partijos-frakcija&rodyti=141"));

        Row().Count(list).ShouldBe((int)expected);
        Label().Matches(list).ShouldAllBe(m => m.Groups[1].Value == "RPF");
        list.ShouldContain("<option value=\"reformu-partijos-frakcija\" selected");
        list.ShouldContain($"<b aria-current=\"true\" id=\"ballots-all\">Visi <span class=\"num\">{expected}</span></b>");
        foreach (var (slug, stored, choice) in new[]
        {
            ("uz", "For", "for"), ("pries", "Against", "against"),
            ("susilaike", "Abstain", "abstain"), ("nebalsavo", "DidNotVote", "did-not-vote"),
        })
        {
            var count = await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.ballot WHERE vote_id = @v AND faction_label = 'RPF' AND choice = @c", ("v", Vote), ("c", stored));
            list.ShouldContain($"href=\"/seimas/balsavimai/99199701?frakcija=reformu-partijos-frakcija&balsas={slug}\" id=\"ballots-{choice}\">");
            Regex.IsMatch(list, $"id=\"ballots-{choice}\">[^<]+<span class=\"num\">{count}</span>").ShouldBeTrue(slug);
        }

        list.ShouldContain("href=\"/seimas/balsavimai/99199701\" id=\"ballot-clear\"", customMessage: "clearing resets the faction");
    }

    [Fact]
    public async Task The_no_faction_option_shows_the_mps_without_a_faction()
    {
        var expected = await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.ballot WHERE vote_id = @v AND faction_label IS NULL", ("v", Vote));
        expected.ShouldBeGreaterThan(0);

        var list = List(await OkAsync(Page + "?frakcija=be-frakcijos&rodyti=141"));

        Row().Count(list).ShouldBe((int)expected);
        Label().Matches(list).ShouldAllBe(m => m.Groups[1].Value == "be frakcijos");
    }

    [Fact]
    public async Task Search_faction_and_ballot_combine_and_the_other_links_keep_them()
    {
        var list = List(await OkAsync(Page + "?q=zolynaite&frakcija=reformu-partijos-frakcija&balsas=nebalsavo"));

        Row().Count(list).ShouldBe(1);
        list.ShouldContain("Indrė Žolynaitė");
        list.ShouldContain("<input type=\"hidden\" name=\"balsas\" value=\"nebalsavo\"");
        list.ShouldContain("href=\"/seimas/balsavimai/99199701?q=zolynaite&frakcija=reformu-partijos-frakcija&balsas=uz\"");
        list.ShouldContain("href=\"/seimas/balsavimai/99199701?balsas=nebalsavo\" id=\"ballot-clear\"");

        var otherFaction = List(await OkAsync(Page + "?q=zolynaite&frakcija=pilietines-sajungos-frakcija"));
        Row().Count(otherFaction).ShouldBe(0);
        otherFaction.ShouldContain("Nerasta nė vieno Seimo nario.");

        var more = List(await OkAsync(Page + "?frakcija=reformu-partijos-frakcija"));
        var inFaction = await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.ballot WHERE vote_id = @v AND faction_label = 'RPF'", ("v", Vote));
        inFaction.ShouldBeGreaterThan(20, "the faction's list is longer than its first page");
        more.ShouldContain($"href=\"/seimas/balsavimai/99199701?frakcija=reformu-partijos-frakcija&rodyti={Math.Min(70, inFaction)}\"",
            customMessage: "50 more, or the rest of the faction");
    }

    [Theory]
    [InlineData("?rodyti=abc")]
    [InlineData("?rodyti=100000")]
    [InlineData("?rodyti=-1")]
    [InlineData("?balsas=nera")]
    [InlineData("?balsas=%3Cb%3E")]
    [InlineData("?rodyti=&balsas=&q=")]
    [InlineData("?frakcija=nera-tokios")]
    [InlineData("?frakcija=RPF")]
    [InlineData("?frakcija=&rodyti=60.5")]
    public async Task Unknown_or_oversized_values_fall_back_to_the_defaults(string query)
    {
        var list = List(await OkAsync(Page + query));

        Row().Count(list).ShouldBe(20);
        list.ShouldContain("Rodoma 20 iš 141");
        list.ShouldContain("<b aria-current=\"true\" id=\"ballots-all\">");
    }

    [Fact]
    public async Task An_oversized_search_falls_back_to_no_search() =>
        List(await OkAsync(Page + "?q=" + new string('a', 500))).ShouldContain("Rodoma 20 iš 141");

    [Fact]
    public async Task A_vote_without_ballots_and_a_consensus_vote_show_no_list_and_no_controls()
    {
        foreach (var path in new[] { "/seimas/balsavimai/99199702", "/seimas/balsavimai/99100003" })
        {
            var html = await OkAsync(path);
            html.ShouldNotContain("id=\"ballots\"", customMessage: path);
            html.ShouldNotContain("ballot-search", customMessage: path);
            html.ShouldNotContain("Rodoma", customMessage: path);
        }
    }

    [GeneratedRegex("<li id=\"ballot-\\d+\">")]
    private static partial Regex Row();

    [GeneratedRegex("<a href=\"/p/[^\"]+\">([^<]+)</a>")]
    private static partial Regex Name();

    [GeneratedRegex("<option(?: value(?:=\"([^\"]*)\")?)?(?: selected)?>([^<]+)</option>")]
    private static partial Regex Option();

    [GeneratedRegex("<span class=\"small\">([^<]+)</span>")]
    private static partial Regex Label();
}
