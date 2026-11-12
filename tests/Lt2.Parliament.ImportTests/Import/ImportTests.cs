using Lt2.Parliament.ImportTests.StandIn;
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
        (await CountAsync("person")).ShouldBe(13);
        (await CountAsync("mandate")).ShouldBe(13);
        (await CountAsync("body")).ShouldBe(5);
        (await CountAsync("membership")).ShouldBe(27);
        (await CountAsync("contact")).ShouldBe(13);
        (await CountAsync("session")).ShouldBe(2);
        (await CountAsync("sitting")).ShouldBe(4);
        (await CountAsync("agenda_item")).ShouldBe(7);
        (await CountAsync("bill")).ShouldBe(3);
        (await CountAsync("vote")).ShouldBe(8);
        (await CountAsync("ballot")).ShouldBe(84);
        (await CountAsync("faction_position")).ShouldBe(21);
        (await CountAsync("mp_stat")).ShouldBe(13);
        (await database.ScalarAsync<bool>("SELECT bool_and(synthetic) FROM parliament.raw_document")).ShouldBeTrue();

        // The answer is cached for a minute on the site; a fresh process sees the stand-in's data as invented.
        await using var site = new ImportHarness(database);
        (await site.IsInventedAsync()).ShouldBeTrue();
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
    }

    [Fact]
    public async Task The_statistics_match_the_hand_calculation_for_five_mps()
    {
        await using var import = new ImportHarness(database);
        await import.RunAsync();

        var stats = await database.RowsAsync(
            "SELECT p.last_name, s.votes_held, s.votes_taken_part, s.voted_with_faction, s.votes_with_faction_position, s.free_votes " +
            "FROM parliament.mp_stat s JOIN parliament.person p ON p.id = s.person_id " +
            "WHERE p.seimas_person_id IN (99000001, 99000005, 99000007, 99000011, 99000013) ORDER BY p.seimas_person_id");

        // Hand calculation: Seimas/Synthetic/README.md, "Statistics by hand".
        stats.ShouldBe(
        [
            "Petraitis | 7 | 7 | 5 | 7 | 0",
            "Jonaitienė | 7 | 7 | 5 | 6 | 1",
            "Liepaitis | 7 | 6 | 3 | 4 | 2",
            "Alksninis | 3 | 2 | 1 | 1 | 1",
            "Kadagytė | 4 | 4 | 2 | 2 | 2",
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
        (await CountAsync("ballot")).ShouldBe(36, "the three votes before the outage are stored");
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
        (await CountAsync("ballot")).ShouldBe(84);
    }

    [Fact]
    public async Task Only_one_import_runs_at_a_time()
    {
        await using var import = new ImportHarness(database);
        await using var other = new NpgsqlConnection(database.AdminConnectionString);
        await other.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(" + 0x4C54_3250_4152_4C + ")", other))
        {
            await command.ExecuteScalarAsync();
        }

        var outcome = await import.RunAsync();

        outcome.Status.ShouldBe("skipped");
        import.TakeRequests().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_import_is_due_at_once_when_it_never_succeeded_and_hourly_after_a_success_on_a_day_without_sittings()
    {
        await using var import = new ImportHarness(database);
        (await import.IsDueAsync()).ShouldBeTrue();

        await import.RunAsync();
        (await import.IsDueAsync()).ShouldBeFalse();

        import.Clock.Advance(TimeSpan.FromMinutes(59));
        (await import.IsDueAsync()).ShouldBeTrue();
    }
}
