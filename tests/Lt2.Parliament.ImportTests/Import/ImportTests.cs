using Lt2.Parliament.ImportTests.StandIn;
using Lt2.SeimasStandIn;
using Npgsql;

namespace Lt2.Parliament.ImportTests.Import;

[CollectionDefinition(Name)]
public sealed class ImportDatabaseGroup : ICollectionFixture<ImportDatabase>
{
    public const string Name = "import database";
}

/// <summary>
/// Design 7.2 against the stand-in and a real PostgreSQL: the import fills the model, running it again changes nothing,
/// an outage mid-import is resumed, and a changed response updates rows by Seimas identifier.
/// </summary>
[Collection(ImportDatabaseGroup.Name)]
public sealed class ImportTests(ImportDatabase database) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<long> CountAsync(string table) => database.ScalarAsync<long>($"SELECT count(*) FROM parliament.{table}");

    [Fact]
    public async Task A_full_import_fills_the_parliament_from_the_stand_in_and_marks_it_invented()
    {
        await using var import = new ImportHarness(database);
        (await import.IsInventedAsync()).ShouldBeFalse();

        var outcome = await import.RunAsync();

        outcome.Status.ShouldBe("succeeded");
        outcome.Synthetic.ShouldBeTrue();
        outcome.Requests.ShouldBe(20);
        (await CountAsync("raw_document")).ShouldBe(20);
        (await CountAsync("term")).ShouldBe(2);
        (await CountAsync("person")).ShouldBe(14);
        (await CountAsync("mandate")).ShouldBe(14);
        (await CountAsync("body")).ShouldBe(6);
        (await CountAsync("membership")).ShouldBe(29);
        (await CountAsync("contact")).ShouldBe(14);
        (await CountAsync("session")).ShouldBe(2);
        (await CountAsync("sitting")).ShouldBe(4);
        (await CountAsync("agenda_item")).ShouldBe(7);
        (await CountAsync("bill")).ShouldBe(3);
        (await CountAsync("vote")).ShouldBe(8);
        (await CountAsync("ballot")).ShouldBe(91);
        (await CountAsync("faction_position")).ShouldBe(21);
        (await CountAsync("mp_stat")).ShouldBe(14);
        (await database.ScalarAsync<bool>("SELECT bool_and(synthetic) FROM parliament.raw_document")).ShouldBeTrue();

        // The answer is cached for a minute on the site; a fresh process sees the stand-in's data as invented.
        await using var site = new ImportHarness(database);
        (await site.IsInventedAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task The_proceedings_supply_a_bill_and_vote_absent_from_the_published_agenda()
    {
        var responses = StandInFixture.Responses();
        responses.Replace("p2b.ad_sp_darbotvarke?posedzio_id=99001101", StandInResponses.EmptyDocument);
        responses.Replace("p2b.ad_seimo_posedzio_eiga_full?posedzio_id=99001101", """
            <posedziai><posedis pos_id="99001101"><posedzio-eiga>
              <darbotvarkes-klausimas kl_stad_id="99010102">
                <nr>2</nr><pavadinimas>Atliekų tvarkymo įstatymo pataisa (Nr. XVP-0618)</pavadinimas><stadija>Svarstymas</stadija>
                <balsavimai>
                  <balsavimas bals_id="99100102"><nuo>2026-09-15 11:05:44</nuo><aprasas>Pritarta – dėl pritarimo po svarstymo</aprasas></balsavimas>
                </balsavimai>
              </darbotvarkes-klausimas>
            </posedzio-eiga></posedis></posedziai>
            """);
        await using var import = new ImportHarness(database, responses);
        var outcome = await import.RunAsync();

        outcome.Status.ShouldBe("succeeded");
        (await database.RowsAsync("""
            SELECT v.agenda_item_id, b.number, v.kind, v.stage, v.has_result
            FROM parliament.vote v JOIN parliament.bill b ON b.id = v.bill_id
            WHERE v.id = 99100102
            """)).ShouldBe(["99010102 | XVP-0618 | StageResult | Consideration | True"]);
    }

    [Fact]
    public async Task The_proceedings_add_only_the_bill_to_an_agenda_item_listed_without_one()
    {
        var responses = StandInFixture.Responses();
        responses.Replace("p2b.ad_sp_darbotvarke?posedzio_id=99001101", """
            <?xml version="1.0" encoding="UTF-8"?>
            <SeimoInformacija>
              <SeimoPosėdis posėdžio_id="99001101" numeris="241" tipas="Rytinis" pradžia="2026-09-15 10:00" pabaiga="2026-09-15 13:15">
                <DarbotvarkėsKlausimas numeris="2" pavadinimas="Atliekų tvarkymo įstatymo pataisa, visas pavadinimas" laikas_nuo="10:10">
                  <KlausimoStadija darbotvarkės_klausimo_id="99010102" pavadinimas="Svarstymas" dokumento_nuoroda=""/>
                </DarbotvarkėsKlausimas>
              </SeimoPosėdis>
            </SeimoInformacija>
            """);
        responses.Replace("p2b.ad_seimo_posedzio_eiga_full?posedzio_id=99001101", """
            <posedziai><posedis pos_id="99001101"><posedzio-eiga>
              <darbotvarkes-klausimas kl_stad_id="99010102">
                <nr>2.1</nr><pavadinimas>Atliekų tvarkymo įst. pataisa (Nr. XVP-0618)</pavadinimas><stadija>Priėmimas</stadija>
                <balsavimai>
                  <balsavimas bals_id="99100102"><nuo>2026-09-15 11:05:44</nuo><aprasas>Pritarta – dėl pritarimo po svarstymo</aprasas></balsavimas>
                </balsavimai>
              </darbotvarkes-klausimas>
            </posedzio-eiga></posedis></posedziai>
            """);
        await using var import = new ImportHarness(database, responses);
        (await import.RunAsync()).Status.ShouldBe("succeeded");

        (await database.RowsAsync("""
            SELECT i.sitting_id, i.number, i.title, i.stage, b.number
            FROM parliament.agenda_item i JOIN parliament.bill b ON b.id = i.bill_id
            WHERE i.id = 99010102
            """)).ShouldBe(["99001101 | 2 | Atliekų tvarkymo įstatymo pataisa, visas pavadinimas | Svarstymas | XVP-0618"]);
        (await database.ScalarAsync<string>("SELECT b.number FROM parliament.vote v JOIN parliament.bill b ON b.id = v.bill_id WHERE v.id = 99100102"))
            .ShouldBe("XVP-0618");
    }

    [Fact]
    public async Task Bill_XVP_0412_has_its_stage_votes_and_the_awkward_cases_are_stored_as_the_design_says()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();

        var votes = await database.RowsAsync(
            "SELECT v.id, v.kind, coalesce(v.stage, '-'), v.for, v.against, v.is_consensus, v.has_result FROM parliament.vote v " +
            "JOIN parliament.bill b ON b.id = v.bill_id WHERE b.number = 'XVP-0412' ORDER BY v.held_at");
        votes.ShouldBe(
        [
            "99100002 | StageResult | Presentation | 71 | 38 | False | True",
            "99100005 | Amendment | Consideration | 40 | 60 | False | True",
            "99100006 | StageResult | Consideration | 74 | 44 | False | True",
        ]);

        // Consensus: stored as a vote, no result, no ballots.
        (await database.RowsAsync("SELECT kind, stage, is_consensus, has_result, (SELECT count(*) FROM parliament.ballot WHERE vote_id = 99100003) FROM parliament.vote WHERE id = 99100003"))
            .ShouldBe(["StageResult | Presentation | True | False | 0"]);

        // The official mismatch note is kept with the result.
        (await database.ScalarAsync<string>("SELECT official_note FROM parliament.vote WHERE id = 99100004")).ShouldContain("neatitinka protokole");

        // Faction change: Liepaitis votes in PSF in March and in RAF in May, and the ballot knows the faction body of the day.
        (await database.RowsAsync(
            "SELECT x.vote_id, x.faction_label, f.name FROM parliament.ballot x JOIN parliament.person p ON p.id = x.person_id " +
            "JOIN parliament.body f ON f.id = x.faction_id WHERE p.seimas_person_id = 99000007 AND x.vote_id IN (99100002, 99100006) ORDER BY 1"))
            .ShouldBe(["99100002 | PSF | Pilietinės sąjungos frakcija", "99100006 | RAF | Regionų aljanso frakcija"]);

        // Ended mandate: a former MP keeps the profile with the end date.
        (await database.ScalarAsync<DateOnly>("SELECT m.\"to\" FROM parliament.mandate m JOIN parliament.person p ON p.id = m.person_id WHERE p.seimas_person_id = 99000011"))
            .ShouldBe(new DateOnly(2026, 4, 15));

        // Faction positions: a free vote, and exactly two thirds.
        (await database.RowsAsync("SELECT vote_id, faction_label, position FROM parliament.faction_position WHERE (vote_id, faction_label) IN ((99100002, 'RAF'), (99100102, 'PSF'), (99100102, 'RAF')) ORDER BY 1, 2"))
            .ShouldBe(["99100002 | RAF | Free", "99100102 | PSF | Against", "99100102 | RAF | Free"]);
        (await database.ScalarAsync<string>("SELECT slug FROM parliament.person WHERE seimas_person_id = 99000001")).ShouldBe("jonas-petraitis");

        // The mixed group is not a faction: its label gets no position row, and its member's ballot no faction body.
        (await database.ScalarAsync<long>("SELECT count(*) FROM parliament.faction_position WHERE faction_label = 'MG'")).ShouldBe(0);
        (await database.ScalarAsync<string>("SELECT kind FROM parliament.body WHERE seimas_id = 99100009")).ShouldBe("Group");
        (await database.RowsAsync(
            "SELECT x.vote_id, x.faction_label, coalesce(f.name, '-') FROM parliament.ballot x JOIN parliament.person p ON p.id = x.person_id " +
            "LEFT JOIN parliament.body f ON f.id = x.faction_id WHERE p.seimas_person_id = 99000014 AND x.vote_id IN (99100004, 99100005) ORDER BY 1"))
            .ShouldBe(["99100004 | RPF | Reformų partijos frakcija", "99100005 | MG | -"]);
    }

    [Fact]
    public async Task The_statistics_match_the_hand_calculation_for_five_mps()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();

        var stats = await database.RowsAsync(
            "SELECT p.last_name, s.votes_held, s.votes_taken_part, s.voted_with_faction, s.votes_with_faction_position, s.free_votes " +
            "FROM parliament.mp_stat s JOIN parliament.person p ON p.id = s.person_id " +
            "WHERE p.seimas_person_id IN (99000001, 99000005, 99000007, 99000011, 99000013, 99000014) ORDER BY p.seimas_person_id");

        // Hand calculation: Seimas/Synthetic/README.md, "Statistics by hand".
        stats.ShouldBe(
        [
            "Petraitis | 7 | 7 | 5 | 7 | 0",
            "Jonaitienė | 7 | 7 | 5 | 6 | 1",
            "Liepaitis | 7 | 6 | 3 | 4 | 2",
            "Alksninis | 3 | 2 | 1 | 1 | 1",
            "Kadagytė | 4 | 4 | 2 | 2 | 2",
            "Ievaitė | 7 | 7 | 3 | 3 | 0",
        ]);
    }

    [Fact]
    public async Task Importing_twice_changes_nothing_even_when_every_response_is_fetched_again()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        var first = await database.SnapshotAsync();
        import.TakeRequests();

        var second = await import.RunAsync();
        second.Status.ShouldBe("succeeded");
        second.Changes.ShouldBe(0);
        import.TakeRequests().ShouldBe(["/sip/p2b.ad_seimo_posedziai?sesijos_id=99000102"], "only the open session's sittings are asked for again");

        // A day later the daily lists are fetched again too; still nothing changes.
        import.Clock.Advance(TimeSpan.FromHours(25));
        var third = await import.RunAsync();
        third.Changes.ShouldBe(0);
        import.TakeRequests().Count.ShouldBe(4);

        (await database.SnapshotAsync()).ShouldBe(first);
        (await CountAsync("raw_document")).ShouldBe(20, "an unchanged response is not stored twice");
    }

    [Fact]
    public async Task A_raised_parser_version_applies_the_stored_responses_again_without_asking_the_service()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        var expected = await database.SnapshotAsync();

        // As if the parser changed: every stored response was applied by an older version, and the data drifted.
        await database.ScalarAsync<int>("WITH u AS (UPDATE parliament.fetch_state SET parser_version = 0 RETURNING 1) SELECT count(*)::int FROM u");
        await database.ScalarAsync<int>("WITH u AS (UPDATE parliament.bill SET title = 'Pasenęs pavadinimas' RETURNING 1) SELECT count(*)::int FROM u");
        import.TakeRequests();

        var outcome = await import.RunAsync();

        outcome.Status.ShouldBe("succeeded");
        import.TakeRequests().ShouldBe(["/sip/p2b.ad_seimo_posedziai?sesijos_id=99000102"], "only what is due anyway is asked for; the rest is re-applied from storage");
        (await database.SnapshotAsync()).ShouldBe(expected);
        (await database.ScalarAsync<long>("SELECT count(*) FROM parliament.fetch_state WHERE parser_version <> " + global::Lt2.Parliament.Import.ParliamentImporter.ParserVersion)).ShouldBe(0);
    }

    /// <summary>
    /// Design 7.2: a moderator's correction of a vote's classification is kept apart from the rules' result and wins over it
    /// on every re-parse and every re-import: the import writes the effective classification and never touches the correction.
    /// </summary>
    [Fact]
    public async Task A_moderators_correction_of_a_votes_classification_survives_a_re_parse_and_a_full_re_import()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        const string Vote = "SELECT kind, stage, coalesce(kind_override, '-'), coalesce(stage_override, '-'), coalesce(override_statement, '-') FROM parliament.vote WHERE id = 99100005";
        (await database.RowsAsync(Vote)).ShouldBe(["Amendment | Consideration | - | - | -"], "the rules' classification");

        // As the moderators' action stores it: the correction in its own columns, the effective classification in kind and stage.
        await database.ScalarAsync<int>(
            "WITH u AS (UPDATE parliament.vote SET kind_override = 'StageResult', stage_override = 'Adoption', overridden_at = now(), override_statement = 'Bandymas.', " +
            "kind = 'StageResult', stage = 'Adoption' WHERE id = 99100005 RETURNING 1) SELECT count(*)::int FROM u");
        var corrected = await database.SnapshotAsync();

        // Re-parsed from storage by a newer parser: the rules run again over the wording, and the correction still wins.
        await database.ScalarAsync<int>("WITH u AS (UPDATE parliament.fetch_state SET parser_version = 0 RETURNING 1) SELECT count(*)::int FROM u");
        var reparsed = await import.RunAsync();
        reparsed.Status.ShouldBe("succeeded");
        reparsed.Changes.ShouldBe(0, "the import does not write the rules' result over the correction, not even for a moment");
        (await database.RowsAsync(Vote)).ShouldBe(["StageResult | Adoption | StageResult | Adoption | Bandymas."]);

        // Every response fetched again (an administrator's full re-read): the same.
        import.Clock.Advance(TimeSpan.FromDays(2));
        (await import.RunAsync(ImportParts.All)).Status.ShouldBe("succeeded");
        (await database.RowsAsync(Vote)).ShouldBe(["StageResult | Adoption | StageResult | Adoption | Bandymas."]);
        (await database.SnapshotAsync()).ShouldBe(corrected);

        // Without the correction the next re-parse writes the rules' classification again.
        await database.ScalarAsync<int>(
            "WITH u AS (UPDATE parliament.vote SET kind_override = NULL, stage_override = NULL, overridden_at = NULL, override_statement = NULL WHERE id = 99100005 RETURNING 1) SELECT count(*)::int FROM u");
        await database.ScalarAsync<int>("WITH u AS (UPDATE parliament.fetch_state SET parser_version = 0 RETURNING 1) SELECT count(*)::int FROM u");
        await import.RunAsync();
        (await database.RowsAsync(Vote)).ShouldBe(["Amendment | Consideration | - | - | -"]);
    }

    [Fact]
    public async Task A_run_cut_off_with_its_process_is_marked_failed_by_the_next_run()
    {
        await database.ScalarAsync<int>(
            "WITH i AS (INSERT INTO parliament.import_run (id, started_at, status, source, synthetic, requests, changes) VALUES (gen_random_uuid(), now() - interval '2 hours', 'running', 'x', true, 3, 0) RETURNING 1) SELECT count(*)::int FROM i");
        await using var import = new ImportHarness(database);

        await import.RunAsync();

        (await database.RowsAsync("SELECT status, error FROM parliament.import_run WHERE source = 'x'")).ShouldBe(["failed | abandoned"]);
    }

    [Fact]
    public async Task A_vote_result_missing_when_first_asked_is_asked_for_again_later_however_old_the_vote()
    {
        var responses = StandInFixture.Responses();
        const string result = "p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=99100102";
        var published = responses.Text(result);
        responses.Replace(result, StandInResponses.EmptyDocument);
        await using var import = new ImportHarness(database, responses);
        await import.RunAsync();
        (await database.ScalarAsync<bool>("SELECT has_result FROM parliament.vote WHERE id = 99100102")).ShouldBeFalse();

        // The 15 September vote is long past the refetch window; the result appears in open data later.
        responses.Replace(result, published);
        import.Clock.Advance(TimeSpan.FromHours(2));
        import.TakeRequests();
        await import.RunAsync();
        import.TakeRequests().ShouldNotContain("/sip/" + result, "not again within the day");

        import.Clock.Advance(TimeSpan.FromDays(1));
        await import.RunAsync();

        import.TakeRequests().ShouldContain("/sip/" + result);
        (await database.ScalarAsync<bool>("SELECT has_result FROM parliament.vote WHERE id = 99100102")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_outage_mid_import_fails_the_run_keeps_what_was_stored_and_the_next_run_resumes()
    {
        string expected;
        await using (var clean = new ImportHarness(database))
        {
            await clean.RunAsync();
            expected = await database.SnapshotAsync();
        }

        await database.ResetAsync();
        await using var import = new ImportHarness(database);
        import.StandIn.FailWhen = r => r.RequestUri!.Query.Contains("balsavimo_id=99100005", StringComparison.Ordinal);

        var broken = await import.RunAsync();

        broken.Status.ShouldBe("failed");
        broken.Error.ShouldNotBeNull().ShouldContain("balsavimo_id=99100005");
        (await CountAsync("ballot")).ShouldBe(39, "the three votes before the outage are stored");
        (await database.ScalarAsync<string>("SELECT status FROM parliament.import_run ORDER BY started_at LIMIT 1")).ShouldBe("failed");

        import.StandIn.FailWhen = null;
        import.TakeRequests();
        var resumed = await import.RunAsync();

        resumed.Status.ShouldBe("succeeded");
        var asked = import.TakeRequests();
        asked.ShouldNotContain(r => r.Contains("posedzio_id=99001001", StringComparison.Ordinal) || r.Contains("balsavimo_id=99100002", StringComparison.Ordinal),
            "what was parsed before the outage is not asked for again");
        asked.ShouldContain("/sip/p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=99100005");
        (await database.SnapshotAsync()).ShouldBe(expected);
    }

    [Fact]
    public async Task A_service_that_is_down_fails_the_run_quietly_and_leaves_the_stored_data_as_it_was()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        var before = await database.SnapshotAsync();

        import.StandIn.FailWhen = _ => true;
        import.Clock.Advance(TimeSpan.FromDays(2));
        var outcome = await import.RunAsync();

        outcome.Status.ShouldBe("failed");
        (await database.SnapshotAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_changed_mp_record_updates_the_same_rows_by_asmens_id()
    {
        var responses = StandInFixture.Responses();
        await using var import = new ImportHarness(database, responses);
        await import.RunAsync();
        var ids = await database.RowsAsync("SELECT seimas_person_id, id FROM parliament.person ORDER BY 1");

        const string members = "p2b.ad_seimo_nariai?kadencijos_id=10";
        var changed = responses.Text(members)
            .Replace(
                "asmens_id=\"99000005\" vardas=\"Ona\" pavardė=\"Jonaitienė\" lytis=\"M\" data_nuo=\"2024-11-14\" data_iki=\"\"",
                "asmens_id=\"99000005\" vardas=\"Ona\" pavardė=\"Jonaitienė\" lytis=\"M\" data_nuo=\"2024-11-14\" data_iki=\"2026-11-01\"",
                StringComparison.Ordinal)
            .Replace("pareigos=\"Frakcijos narys\" data_nuo=\"2024-11-14\" data_iki=\"\"/>\n      <Pareigos padalinio_id=\"99200001\" padalinio_pavadinimas=\"Biudžeto ir finansų komitetas\" pareigos=\"Komiteto narys\"",
                "pareigos=\"Frakcijos narys\" data_nuo=\"2024-11-14\" data_iki=\"\"/>\n      <Pareigos padalinio_id=\"99200001\" padalinio_pavadinimas=\"Biudžeto ir finansų komitetas\" pareigos=\"Komiteto pirmininko pavaduotojas\"",
                StringComparison.Ordinal)
            .Replace("reikšmė=\"pavyzdys.jonas.petraitis@lrs.lt\"", "reikšmė=\"pavyzdys.j.petraitis@lrs.lt\"", StringComparison.Ordinal);
        changed.ShouldNotBe(responses.Text(members));
        responses.Replace(members, changed);

        import.Clock.Advance(TimeSpan.FromHours(25));
        var outcome = await import.RunAsync();

        outcome.Changes.ShouldBeGreaterThan(0);
        (await database.RowsAsync("SELECT seimas_person_id, id FROM parliament.person ORDER BY 1")).ShouldBe(ids, "no person is created twice");
        (await database.ScalarAsync<DateOnly>("SELECT m.\"to\" FROM parliament.mandate m JOIN parliament.person p ON p.id = m.person_id WHERE p.seimas_person_id = 99000005"))
            .ShouldBe(new DateOnly(2026, 11, 1));
        (await database.RowsAsync("SELECT m.role FROM parliament.membership m JOIN parliament.person p ON p.id = m.person_id JOIN parliament.body b ON b.id = m.body_id WHERE p.seimas_person_id = 99000001 AND b.seimas_id = 99200001"))
            .ShouldBe(["Komiteto pirmininko pavaduotojas"]);
        (await database.RowsAsync("SELECT c.value FROM parliament.contact c JOIN parliament.person p ON p.id = c.person_id WHERE p.seimas_person_id = 99000001"))
            .ShouldBe(["pavyzdys.j.petraitis@lrs.lt"]);
        (await CountAsync("raw_document")).ShouldBe(21, "both versions of the response are kept");

        // Ona's mandate now ends on 1 November; no recorded vote falls after it, so her counts stay.
        (await database.RowsAsync("SELECT s.votes_held FROM parliament.mp_stat s JOIN parliament.person p ON p.id = s.person_id WHERE p.seimas_person_id = 99000005"))
            .ShouldBe(["7"]);
    }

    [Fact]
    public async Task A_changed_recent_vote_result_updates_the_ballot_the_faction_position_and_the_statistics()
    {
        var responses = StandInFixture.Responses();
        await using var import = new ImportHarness(database, responses);
        import.Clock.Now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero); // the 15 September sitting is still recent
        await import.RunAsync();

        const string result = "p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=99100102";
        responses.Replace(result, responses.Text(result).Replace(
            "pavardė=\"Šermukšnytė\" frakcija=\"RAF\" kaip_balsavo=\"Susilaikė\"",
            "pavardė=\"Šermukšnytė\" frakcija=\"RAF\" kaip_balsavo=\"Už\"",
            StringComparison.Ordinal));
        import.Clock.Advance(TimeSpan.FromHours(1));

        var outcome = await import.RunAsync();

        outcome.Changes.ShouldBeGreaterThan(0);
        (await database.ScalarAsync<string>("SELECT x.choice FROM parliament.ballot x JOIN parliament.person p ON p.id = x.person_id WHERE x.vote_id = 99100102 AND p.seimas_person_id = 99000012"))
            .ShouldBe("For");
        (await database.ScalarAsync<string>("SELECT position FROM parliament.faction_position WHERE vote_id = 99100102 AND faction_label = 'RAF'"))
            .ShouldBe("For", "4 of 5 voting RAF members now chose for");
        (await database.RowsAsync("SELECT s.voted_with_faction, s.votes_with_faction_position, s.free_votes FROM parliament.mp_stat s JOIN parliament.person p ON p.id = s.person_id WHERE p.seimas_person_id = 99000007"))
            .ShouldBe(["4 | 5 | 1"], "Liepaitis voted for, now with his faction");
        (await CountAsync("ballot")).ShouldBe(91);
    }

    [Theory]
    [InlineData(ImportParts.None)]
    [InlineData(ImportParts.Members)]
    [InlineData(ImportParts.All)]
    public async Task Only_one_import_runs_at_a_time_a_forced_one_included(ImportParts force)
    {
        await using var import = new ImportHarness(database);
        await using var other = new NpgsqlConnection(database.UnpooledAdminConnectionString);
        await other.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(" + 0x4C54_3250_4152_4C + ")", other))
        {
            await command.ExecuteScalarAsync();
        }

        var outcome = await import.RunAsync(force);

        outcome.Status.ShouldBe("skipped");
        outcome.RunId.ShouldBeNull();
        import.TakeRequests().ShouldBeEmpty();
        (await CountAsync("import_run")).ShouldBe(0, "a run that did not start leaves no record");
    }

    [Fact]
    public async Task Forcing_the_members_asks_for_the_terms_the_mp_list_and_the_sessions_again_however_fresh()
    {
        var responses = StandInFixture.Responses();
        await using var import = new ImportHarness(database, responses);
        await import.RunAsync();

        const string members = "p2b.ad_seimo_nariai?kadencijos_id=10";
        responses.Replace(members, responses.Text(members)
            .Replace("reikšmė=\"pavyzdys.jonas.petraitis@lrs.lt\"", "reikšmė=\"pavyzdys.j.petraitis@lrs.lt\"", StringComparison.Ordinal));
        import.Clock.Advance(TimeSpan.FromHours(1));
        import.TakeRequests();

        // The scheduled run leaves the hour-old MP list alone.
        (await import.RunAsync()).Changes.ShouldBe(0);
        import.TakeRequests().ShouldBe(["/sip/p2b.ad_seimo_posedziai?sesijos_id=99000102"]);

        import.Clock.Advance(TimeSpan.FromMinutes(1));
        var forced = await import.RunAsync(ImportParts.Members);

        forced.Status.ShouldBe("succeeded");
        forced.Changes.ShouldBeGreaterThan(0);
        import.TakeRequests().ShouldBe(
        [
            "/sip/p2b.ad_seimo_kadencijos",
            "/sip/" + members,
            "/sip/p2b.ad_seimo_sesijos?kadencijos_id=10",
            "/sip/p2b.ad_seimo_posedziai?sesijos_id=99000102",
        ], "the forced part, then the rest of the walk by its own rules");
        (await database.RowsAsync("SELECT c.value FROM parliament.contact c JOIN parliament.person p ON p.id = c.person_id WHERE p.seimas_person_id = 99000001"))
            .ShouldBe(["pavyzdys.j.petraitis@lrs.lt"]);
        (await database.RowsAsync("SELECT status, source FROM parliament.import_run ORDER BY started_at DESC LIMIT 1"))
            .ShouldBe(["succeeded | http://seimas-standin.test/sip/ forced: members"]);
    }

    [Fact]
    public async Task Forcing_the_sittings_asks_for_every_sitting_list_agenda_and_proceedings_however_old()
    {
        var responses = StandInFixture.Responses();
        await using var import = new ImportHarness(database, responses);
        await import.RunAsync();

        // The 17 March sitting is long past the refetch window.
        const string agenda = "p2b.ad_sp_darbotvarke?posedzio_id=99001001";
        responses.Replace(agenda, responses.Text(agenda)
            .Replace("pavadinimas=\"Dėl posėdžio darbotvarkės\"", "pavadinimas=\"Dėl posėdžio darbotvarkės tvirtinimo\"", StringComparison.Ordinal));
        import.Clock.Advance(TimeSpan.FromHours(1));
        await import.RunAsync();
        (await database.ScalarAsync<string>("SELECT title FROM parliament.agenda_item WHERE id = 99010001")).ShouldBe("Dėl posėdžio darbotvarkės");
        import.TakeRequests();

        import.Clock.Advance(TimeSpan.FromMinutes(1));
        var forced = await import.RunAsync(ImportParts.Sittings);

        forced.Status.ShouldBe("succeeded");
        var asked = import.TakeRequests();
        asked.Count(r => r.Contains("p2b.ad_seimo_posedziai?", StringComparison.Ordinal)).ShouldBe(2, "both sessions, the closed one too");
        asked.Count(r => r.Contains("p2b.ad_sp_darbotvarke?", StringComparison.Ordinal)).ShouldBe(4);
        asked.Count(r => r.Contains("p2b.ad_seimo_posedzio_eiga_full?", StringComparison.Ordinal)).ShouldBe(4);
        asked.Count.ShouldBe(10, "no MP list and no vote result: those parts keep their own rules");
        (await database.ScalarAsync<string>("SELECT title FROM parliament.agenda_item WHERE id = 99010001")).ShouldBe("Dėl posėdžio darbotvarkės tvirtinimo");
        (await CountAsync("raw_document")).ShouldBe(21, "only the changed response is stored again");
    }

    [Fact]
    public async Task Forcing_the_votes_asks_for_every_recorded_result_however_old_and_applies_a_change()
    {
        var responses = StandInFixture.Responses();
        await using var import = new ImportHarness(database, responses);
        await import.RunAsync();

        // The 15 September vote is long past the refetch window, and its result is there: the schedule never asks again.
        const string result = "p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=99100102";
        responses.Replace(result, responses.Text(result).Replace(
            "pavardė=\"Šermukšnytė\" frakcija=\"RAF\" kaip_balsavo=\"Susilaikė\"",
            "pavardė=\"Šermukšnytė\" frakcija=\"RAF\" kaip_balsavo=\"Už\"",
            StringComparison.Ordinal));
        const string choice = "SELECT x.choice FROM parliament.ballot x JOIN parliament.person p ON p.id = x.person_id WHERE x.vote_id = 99100102 AND p.seimas_person_id = 99000012";
        import.Clock.Advance(TimeSpan.FromHours(1));
        await import.RunAsync();
        (await database.ScalarAsync<string>(choice)).ShouldBe("Abstain");
        import.TakeRequests();

        import.Clock.Advance(TimeSpan.FromMinutes(1));
        var forced = await import.RunAsync(ImportParts.Votes);

        forced.Status.ShouldBe("succeeded");
        var asked = import.TakeRequests();
        asked.Count(r => r.Contains("p2b.ad_sp_balsavimo_rezultatai?", StringComparison.Ordinal)).ShouldBe(7, "every vote with per-MP results; not the consensus one");
        asked.ShouldNotContain(r => r.Contains("p2b.ad_seimo_nariai", StringComparison.Ordinal) || r.Contains("p2b.ad_sp_darbotvarke", StringComparison.Ordinal));
        (await database.ScalarAsync<string>(choice)).ShouldBe("For");
        (await database.ScalarAsync<string>("SELECT position FROM parliament.faction_position WHERE vote_id = 99100102 AND faction_label = 'RAF'")).ShouldBe("For");
        (await database.RowsAsync("SELECT s.voted_with_faction, s.votes_with_faction_position, s.free_votes FROM parliament.mp_stat s JOIN parliament.person p ON p.id = s.person_id WHERE p.seimas_person_id = 99000007"))
            .ShouldBe(["4 | 5 | 1"], "the statistics are recomputed after a forced run that changed something");
    }

    [Fact]
    public async Task Forcing_everything_asks_for_every_request_in_the_first_imports_order_and_changes_nothing_that_did_not_change()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();
        var first = import.TakeRequests();
        var expected = await database.SnapshotAsync();
        import.Clock.Advance(TimeSpan.FromHours(1));

        var forced = await import.RunAsync(ImportParts.All);

        forced.Status.ShouldBe("succeeded");
        forced.Requests.ShouldBe(20);
        forced.Changes.ShouldBe(0, "a body already applied by this parser is not applied again");
        var asked = import.TakeRequests();
        asked.ShouldBe(first, "a full re-read as on an empty database: the MPs before the sittings, the sittings before the votes");
        asked.IndexOf("/sip/p2b.ad_seimo_nariai?kadencijos_id=10").ShouldBeLessThan(asked.FindIndex(r => r.Contains("balsavimo_id=", StringComparison.Ordinal)));
        (await database.SnapshotAsync()).ShouldBe(expected);
        (await CountAsync("raw_document")).ShouldBe(20, "an unchanged response is not stored twice");
        (await database.ScalarAsync<string>("SELECT source FROM parliament.import_run ORDER BY started_at DESC LIMIT 1")).ShouldEndWith(" forced: members, sittings, votes");
    }

    [Fact]
    public async Task The_import_is_due_at_once_when_it_never_succeeded_and_hourly_after_a_success_on_a_day_without_sittings()
    {
        await using var import = new ImportHarness(database);
        (await import.IsDueAsync()).ShouldBeTrue();
        (await import.DueAsync()).LastStart.ShouldBeNull();

        await import.RunAsync();
        (await import.DueAsync()).ShouldBe(new ImportDue(false, import.Clock.Now), "not due, and the last start is said");

        import.Clock.Advance(TimeSpan.FromMinutes(59));
        (await import.IsDueAsync()).ShouldBeTrue();
    }
}
