using System.Net;
using System.Text.RegularExpressions;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 7.3: the parliament pages as the server renders them (no JavaScript involved).</summary>
public sealed partial class ParliamentPageTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Nbsp = " ";

    public async ValueTask InitializeAsync() => await ParliamentSeed.EnsureAsync(Database);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<(HttpStatusCode Status, string Html)> PageAsync(string path)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(path);
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    private async Task<string> OkAsync(string path)
    {
        var (status, html) = await PageAsync(path);
        status.ShouldBe(HttpStatusCode.OK, path);
        return html;
    }

    public static TheoryData<string> Pages() => new(
        "/seimo-nariai", "/p/jonas-petraitis", "/p/jonas-petraitis/balsavimai", "/frakcijos/reformu-partijos-frakcija",
        "/komitetai/biudzeto-ir-finansu-komitetas", "/seimas/XVP-0412", "/seimas/balsavimai/99100002", "/metodika");

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_parliament_page_is_server_rendered_in_lithuanian_with_one_h1_the_attribution_and_nothing_inline(string path)
    {
        var html = await OkAsync(path);

        html.ShouldContain("<html lang=\"lt\">");
        H1().Count(html).ShouldBe(1, path);
        html.ShouldContain("Lietuvos Respublikos Seimo atvirieji duomenys");
        html.ShouldContain("href=\"https://creativecommons.org/licenses/by/4.0/deed.lt\"");
        html.ShouldNotContain(" style=\"");
        InlineScript().Matches(html).ShouldBeEmpty();
        html.ShouldContain("Seimo narių ir balsavimų duomenys čia išgalvoti");
    }

    [Fact]
    public async Task The_mp_list_shows_sitting_and_former_mps_and_filters_with_a_plain_get_form()
    {
        var html = await OkAsync("/seimo-nariai");
        html.ShouldContain("<form class=\"filters sheet\" method=\"get\" action=\"/seimo-nariai\"");
        foreach (var name in new[] { "Jonas Petraitis", "Ona Jonaitienė", "Aurelija Kadagytė" })
        {
            html.ShouldContain(name);
        }

        html.IndexOf("Dabartiniai Seimo nariai", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Buvę šios kadencijos Seimo nariai", StringComparison.Ordinal));
        html[html.IndexOf("Buvę šios kadencijos Seimo nariai", StringComparison.Ordinal)..].ShouldContain("Tadas Alksninis");

        var filtered = await OkAsync("/seimo-nariai?frakcija=reformu-partijos-frakcija");
        filtered.ShouldContain("Rasta: 4");
        filtered.ShouldNotContain("Ona Jonaitienė</a>");
        (await OkAsync("/seimo-nariai?q=azuol")).ShouldContain("Rasta: 1");
    }

    [Fact]
    public async Task A_profile_shows_both_statistics_with_their_counts_in_whole_per_cent_with_a_no_break_space()
    {
        var html = await OkAsync("/p/jonas-petraitis");

        html.ShouldContain("<h1>Jonas Petraitis</h1>");
        html.ShouldContain("Seimo narys");
        html.ShouldContain("Dalyvavimas balsavimuose");
        html.ShouldContain($"100{Nbsp}%");
        html.ShouldContain("balsavo 7 iš 7");
        html.ShouldContain("Balsavo kaip frakcija");
        html.ShouldContain($"71{Nbsp}%");
        html.ShouldContain("5 iš 7, kai frakcija turėjo poziciją");
        html.ShouldContain("frakcija balsavo laisvai: 0");
        html.ShouldContain("pavyzdys.jonas.petraitis@lrs.lt");
        html.ShouldContain("Profilis sudarytas automatiškai iš Seimo atvirųjų duomenų.");
    }

    [Fact]
    public async Task A_consensus_vote_in_a_profile_has_no_choice_and_no_numbers()
    {
        var html = await OkAsync("/p/jonas-petraitis");
        var row = html[html.IndexOf("/seimas/balsavimai/99100003", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("</li>", StringComparison.Ordinal)];

        row.ShouldContain("Pritarta bendru sutarimu");
        row.ShouldContain("Atskirų Seimo narių balsų nėra");
        row.ShouldNotContain("choice");
    }

    [Fact]
    public async Task A_share_with_nothing_to_count_shows_no_data_never_zero_per_cent()
    {
        await Database.ExecuteAsync(
            "UPDATE parliament.mp_stat SET votes_held = 0, votes_taken_part = 0, votes_with_faction_position = 0, voted_with_faction = 0, free_votes = 0 " +
            "WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = 99000013)");
        try
        {
            var html = await OkAsync("/p/aurelija-kadagyte");
            var record = html[html.IndexOf("id=\"participation\"", StringComparison.Ordinal)..html.IndexOf("Naujausi balsavimai", StringComparison.Ordinal)];

            record.ShouldContain("Duomenų nėra");
            record.ShouldNotContain($"0{Nbsp}%");
        }
        finally
        {
            await Database.ExecuteAsync(
                "UPDATE parliament.mp_stat SET votes_held = 4, votes_taken_part = 4, votes_with_faction_position = 2, voted_with_faction = 2, free_votes = 2 " +
                "WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = 99000013)");
        }
    }

    [Fact]
    public async Task A_mixed_group_member_shows_the_group_counts_only_the_faction_time_votes_and_no_free_votes_line()
    {
        var html = await OkAsync("/p/rasa-ievaite");

        html.ShouldContain("<span class=\"tag\">Mišri Seimo narių grupė</span>");
        html.ShouldContain("3 iš 3, kai frakcija turėjo poziciją");
        html.ShouldContain("frakcija balsavo laisvai: 0", Case.Sensitive, "the line shows while the MP has faction-time votes");
    }

    [Fact]
    public async Task When_faction_time_does_not_cover_the_mandate_the_figure_names_the_faction_and_the_period()
    {
        var rasa = await OkAsync("/p/rasa-ievaite");

        rasa.ShouldContain("id=\"faction-period\"");
        rasa.ShouldContain("Skaičiuojama tik narystės frakcijoje laikotarpiu: Reformų partijos frakcija, 2024 m. lapkričio 14 d. – 2026 m. balandžio 30 d.");
    }

    [Theory]
    [InlineData("/p/jonas-petraitis")]
    [InlineData("/p/darius-liepaitis")]
    [InlineData("/p/tadas-alksninis")]
    public async Task When_faction_time_covers_the_whole_mandate_no_period_line_is_shown(string path) =>
        (await OkAsync(path)).ShouldNotContain("id=\"faction-period\"", customMessage: "a move between factions or an ended mandate leaves no gap");

    [Fact]
    public async Task A_former_mp_is_labelled_as_such_with_the_end_of_the_mandate()
    {
        var html = await OkAsync("/p/tadas-alksninis");

        html.ShouldContain("Buvęs Seimo narys");
        html.ShouldContain("Mandatas iki");
        html.ShouldContain("2026 m. balandžio 15 d.");
    }

    [Fact]
    public async Task Nothing_on_a_profile_ranks_scores_or_mentions_joining()
    {
        var html = (await OkAsync("/p/ona-jonaitiene")).ToLowerInvariant();

        foreach (var word in new[] { "reiting", "vieta tarp", "balas", "prisijungęs prie lietuva", "neprisijung", "išlaid" })
        {
            html.ShouldNotContain(word, customMessage: word);
        }
    }

    [Fact]
    public async Task A_faction_page_lists_members_and_its_positions_neutrally_with_free_votes_named()
    {
        var html = await OkAsync("/frakcijos/regionu-aljanso-frakcija");

        html.ShouldContain("<h1>Regionų aljanso frakcija</h1>");
        html.ShouldContain("Darius Liepaitis");
        html.ShouldContain("Buvę nariai");
        html.ShouldContain("balsavo laisvai");
    }

    [Fact]
    public async Task A_committee_page_lists_its_members_with_roles()
    {
        var html = await OkAsync("/komitetai/biudzeto-ir-finansu-komitetas");

        html.ShouldContain("<h1>Biudžeto ir finansų komitetas</h1>");
        html.ShouldContain("Komiteto pirmininkė");
    }

    [Fact]
    public async Task A_bill_page_shows_its_stages_with_results_and_the_stage_not_yet_voted_without_a_date()
    {
        var html = await OkAsync("/seimas/XVP-0412");

        html.ShouldContain("<h1>Gyventojų pajamų mokesčio įstatymo pataisa</h1>");
        html.ShouldContain("71 : 38");
        html.ShouldContain("74 : 44");
        html.ShouldContain("dar nebalsuota");
        html.ShouldNotContain("Planuojama", customMessage: "open data has no planned dates, so none is shown");
    }

    [Fact]
    public async Task A_vote_page_has_the_totals_a_seat_per_mp_and_every_ballot()
    {
        var html = await OkAsync("/seimas/balsavimai/99100002");

        html.ShouldContain("class=\"figure__num\">71</span>");
        html.ShouldContain("class=\"figure__num\">38</span>");
        html.ShouldContain("<svg class=\"seatmap\"");
        Regex.Count(html, "<rect [^>]*class=\"seat ").ShouldBe(13);
        html.ShouldNotContain("<circle");
        html.ShouldContain("<svg class=\"splitbar");
        html.ShouldContain("Jonas Petraitis (RPF): Prieš");
    }

    [Fact]
    public async Task A_procedural_vote_says_what_it_was_about_once()
    {
        var html = await OkAsync("/seimas/balsavimai/99100101");

        html.ShouldContain("<h1>Dėl posėdžio darbotvarkės patvirtinimo</h1>");
        html.ShouldNotContain("<p class=\"lead\">");
        Regex.Count(html[html.IndexOf("<main", StringComparison.Ordinal)..], "Dėl posėdžio darbotvarkės").ShouldBe(1);
    }

    [Fact]
    public async Task A_vote_on_a_bill_keeps_the_bill_as_heading_and_the_wording_under_it()
    {
        var html = await OkAsync("/seimas/balsavimai/99100002");

        html.ShouldContain("<h1>Gyventojų pajamų mokesčio įstatymo pataisa</h1>");
        html.ShouldContain("<p class=\"lead\">Pritarti projektui po pateikimo</p>");
    }

    [Fact]
    public async Task A_vote_approved_by_consensus_is_shown_without_numbers()
    {
        var html = await OkAsync("/seimas/balsavimai/99100003");

        html.ShouldContain("Pritarta bendru sutarimu");
        html.ShouldContain("todėl balsų skaičių nėra");
        html.ShouldNotContain("figure__num");
        html.ShouldNotContain("class=\"seat ");
    }

    [Fact]
    public async Task The_official_mismatch_note_is_shown_next_to_the_result()
    {
        var html = await OkAsync("/seimas/balsavimai/99100004");
        var result = html[html.IndexOf("id=\"h-result\"", StringComparison.Ordinal)..html.IndexOf("id=\"h-seats\"", StringComparison.Ordinal)];

        result.ShouldContain("id=\"official-note\"");
        result.ShouldContain("neatitinka protokole įrašytų suminių rezultatų");
    }

    [Fact]
    public async Task The_methodology_states_the_rules_as_built_and_when_the_data_was_last_imported()
    {
        var html = await OkAsync("/metodika");

        html.ShouldContain("bent dviejų trečdalių jos narių");
        html.ShouldContain("Balsavimai, kuriuose Seimo narys nedalyvavo, čia neskaičiuojami");
        html.ShouldContain("Mišri Seimo narių grupė nėra frakcija, todėl pozicijos neturi");
        html.ShouldContain("bendru sutarimu");
        html.ShouldContain("Paskutinį kartą duomenys sėkmingai atnaujinti");
        html.ShouldContain("CC BY 4.0");
    }

    [Theory]
    [InlineData("/nera-tokio-puslapio")]
    [InlineData("/p/nera-tokio")]
    [InlineData("/seimas/XVP-9999")]
    [InlineData("/seimas/balsavimai/12345")]
    [InlineData("/frakcijos/nera-tokios")]
    public async Task An_unknown_address_gets_the_not_found_page_with_the_layout_and_the_notice(string path)
    {
        var (status, html) = await PageAsync(path);

        status.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Puslapis nerastas");
        html.ShouldContain("class=\"page top\"");
        html.ShouldContain("Seimo narių ir balsavimų duomenys čia išgalvoti");
    }

    [Fact]
    public async Task The_theme_switch_script_loads_in_the_head_from_our_origin_and_its_button_is_hidden_until_it_runs()
    {
        var html = await OkAsync("/seimo-nariai");
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];

        Regex.IsMatch(head, "<script src=\"/?js/lt2-theme[^\"]*\\.js\"></script>").ShouldBeTrue();
        html.ShouldContain("data-theme-switch data-permanent hidden");
    }

    [GeneratedRegex("<h1[ >]")]
    private static partial Regex H1();

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>")]
    private static partial Regex InlineScript();
}
