using System.Text;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.ImportTests.StandIn;

namespace Lt2.Parliament.ImportTests.Parsing;

/// <summary>The parsers read the synthetic responses (shapes from the register); recorded ones join once they exist.</summary>
public sealed class ParserTests
{
    private static byte[] Synthetic(string name) => File.ReadAllBytes(Path.Combine(StandInFixture.SyntheticDirectory, name + ".xml"));

    private static byte[] Xml(string body) => Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + body);

    [Fact]
    public void Terms_are_read_with_an_open_end_for_the_current_one()
    {
        var terms = SeimasParsers.Terms(Synthetic("p2b.ad_seimo_kadencijos"));

        terms.Single(t => t.Id == 10).ShouldBe(new ParsedTerm(10, "2024–2028 m. kadencija", new DateOnly(2024, 11, 14), null));
        terms.Single(t => t.Id == 9).To.ShouldBe(new DateOnly(2024, 11, 13));
    }

    [Fact]
    public void An_mp_is_read_with_mandate_party_memberships_and_official_address()
    {
        var members = SeimasParsers.Members(Synthetic("p2b.ad_seimo_nariai@kadencijos_id=10"));

        members.Count.ShouldBe(14);
        var jonas = members.Single(m => m.PersonId == 99000001);
        jonas.FirstName.ShouldBe("Jonas");
        jonas.LastName.ShouldBe("Petraitis");
        jonas.NominatingParty.ShouldBe("Reformų partija");
        jonas.From.ShouldBe(new DateOnly(2024, 11, 14));
        jonas.To.ShouldBeNull();
        jonas.TermsServed.ShouldBe(2);
        jonas.Memberships.Select(m => m.BodyName).ShouldBe(["Reformų partijos frakcija", "Biudžeto ir finansų komitetas"]);
        jonas.Contacts.ShouldBe([new ParsedContact("El. p.", "pavyzdys.jonas.petraitis@lrs.lt")]);
    }

    [Fact]
    public void An_mp_who_changed_faction_has_both_memberships_with_their_dates()
    {
        var darius = SeimasParsers.Members(Synthetic("p2b.ad_seimo_nariai@kadencijos_id=10")).Single(m => m.LastName == "Liepaitis");

        darius.Memberships.Where(m => VoteRules.Body(m.BodyName) == BodyKind.Faction)
            .Select(m => (m.BodyName, m.From, m.To))
            .ShouldBe(
            [
                ("Pilietinės sąjungos frakcija", new DateOnly(2024, 11, 14), new DateOnly(2026, 4, 30)),
                ("Regionų aljanso frakcija", new DateOnly(2026, 5, 1), (DateOnly?)null),
            ]);
    }

    [Fact]
    public void An_mp_whose_mandate_ended_is_listed_with_its_end_date_and_a_constituency_is_read_when_present()
    {
        var members = SeimasParsers.Members(Synthetic("p2b.ad_seimo_nariai@kadencijos_id=10"));

        members.Single(m => m.LastName == "Alksninis").To.ShouldBe(new DateOnly(2026, 4, 15));
        members.Single(m => m.LastName == "Ąžuolaitis").Constituency.ShouldBe("Ąžuolyno (Nr. 91)");
        members.Single(m => m.LastName == "Petraitis").Constituency.ShouldBeNull();
    }

    [Fact]
    public void Sitting_times_are_vilnius_local_time_stored_in_utc_across_daylight_saving()
    {
        var spring = SeimasParsers.Sittings(Synthetic("p2b.ad_seimo_posedziai@sesijos_id=99000101"));
        var autumn = SeimasParsers.Sittings(Synthetic("p2b.ad_seimo_posedziai@sesijos_id=99000102"));

        spring.Single(s => s.Id == 99001001).StartsAt.ShouldBe(new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero));
        autumn.Single().StartsAt.ShouldBe(new DateTimeOffset(2026, 9, 15, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_future_sitting_without_a_start_time_is_skipped()
    {
        var sittings = SeimasParsers.Sittings(Xml("""
            <SeimoInformacija><SeimoPosėdis posėdžio_id="-1" pradžia="2026-11-19 10:00" pabaiga=""/>
            <SeimoPosėdis posėdžio_id="-2" pradžia="" pabaiga=""/></SeimoInformacija>
            """));

        sittings.Single().Id.ShouldBe(-1);
    }

    [Fact]
    public void A_parliamentary_group_and_a_body_with_the_same_source_id_are_distinct()
    {
        var member = SeimasParsers.Members(Xml("""
            <SeimoInformacija><SeimoNarys asmens_id="1" vardas="Jonas" pavardė="Petraitis" data_nuo="2024-11-14">
              <Pareigos padalinio_id="" padalinio_pavadinimas="" pareigos="Seimo Pirmininkas" data_nuo="2024-11-14"/>
              <Pareigos padalinio_id="5" padalinio_pavadinimas="Audito komitetas" pareigos="Narys" data_nuo="2024-11-14"/>
              <Pareigos parlamentinės_grupės_id="5" parlamentinės_grupės_pavadinimas="Seniūnų sueiga" pareigos="Narys" data_nuo="2024-11-14"/>
            </SeimoNarys></SeimoInformacija>
            """)).Single();

        member.Memberships.Select(m => m.Kind).ShouldBe([BodyKind.Committee, BodyKind.Group]);
        member.Memberships.Select(m => m.BodyId).ShouldBeUnique();
    }

    [Fact]
    public void Agenda_items_carry_the_bill_number_stage_and_e_seimas_link_and_procedural_items_have_no_bill()
    {
        var items = SeimasParsers.Agenda(Synthetic("p2b.ad_sp_darbotvarke@posedzio_id=99001001"));

        var bill = items.Single(i => i.Id == 99010002);
        bill.BillNumber.ShouldBe("XVP-0412");
        bill.Title.ShouldBe("Gyventojų pajamų mokesčio įstatymo pataisa");
        bill.Stage.ShouldBe("Pateikimas");
        bill.BillUrl.ShouldNotBeNull().ShouldStartWith("https://e-seimas.lrs.lt/");
        items.Single(i => i.Id == 99010001).BillNumber.ShouldBeNull();
    }

    [Fact]
    public void An_untitled_agenda_item_keeps_its_stage_id_and_the_next_item_is_read()
    {
        var items = SeimasParsers.Agenda(Xml("""
            <SeimoInformacija>
              <DarbotvarkėsKlausimas numeris="r -  2." laikas_nuo="" laikas_iki="" pavadinimas="">
                <KlausimoStadija darbotvarkės_klausimo_id="-97201" pavadinimas="" dokumento_nuoroda=""/>
              </DarbotvarkėsKlausimas>
              <DarbotvarkėsKlausimas numeris="3." pavadinimas="Pertrauka">
                <KlausimoStadija darbotvarkės_klausimo_id="-97202"/>
              </DarbotvarkėsKlausimas>
            </SeimoInformacija>
            """));

        items.ShouldBe([
            new ParsedAgendaItem(-97201, "r -  2.", "", null, null, null),
            new ParsedAgendaItem(-97202, "3.", "Pertrauka", null, null, null),
        ]);
    }

    [Fact]
    public void Proceedings_link_each_vote_to_its_agenda_item_and_mark_consensus()
    {
        var votes = SeimasParsers.Proceedings(Synthetic("p2b.ad_seimo_posedzio_eiga_full@posedzio_id=99001001")).Votes;

        votes.Select(v => (v.VoteId, v.AgendaItemId, v.IsConsensus)).ShouldBe(
        [
            (99100001L, 99010001L, false),
            (99100002L, 99010002L, false),
            (99100003L, 99010003L, true),
        ]);
        votes[1].HeldAt.ShouldBe(new DateTimeOffset(2026, 3, 17, 8, 41, 37, TimeSpan.Zero));
    }

    [Fact]
    public void Proceedings_keep_group_votes_and_prefer_the_direct_agenda_link_for_repeated_votes()
    {
        var proceedings = SeimasParsers.Proceedings(Xml("""
            <posedziai><posedis pos_id="-1"><posedzio-eiga>
              <darbotvarkes-klausimas svarst_kl_stad_id="-2" kl_gr_id="-3">
                <pavadinimas>Klausimų grupė</pavadinimas><stadija>Pateikimas</stadija>
                <balsavimai>
                  <balsavimas bals_id="-4"><nuo>2026-09-15 10:41:37</nuo><aprasas>Pritarta – dėl pritarimo po pateikimo</aprasas></balsavimas>
                  <balsavimas bals_id="-5"><nuo>2026-09-15 10:42:04</nuo><aprasas>Pritarta bendru sutarimu – po pateikimo</aprasas></balsavimas>
                </balsavimai>
              </darbotvarkes-klausimas>
              <darbotvarkes-klausimas svarst_kl_stad_id="-6" kl_stad_id="-7">
                <nr>1 - 1</nr><pavadinimas>Gyventojų pajamų mokesčio įstatymo pataisa (Nr. XVP-0412(2))</pavadinimas><stadija>Pateikimas</stadija>
                <balsavimai>
                  <balsavimas bals_id="-4"><nuo>2026-09-15 10:41:37</nuo><aprasas>Pritarta – dėl pritarimo po pateikimo</aprasas></balsavimas>
                </balsavimai>
              </darbotvarkes-klausimas>
            </posedzio-eiga></posedis></posedziai>
            """));

        proceedings.Votes.Count.ShouldBe(2);
        proceedings.Votes.Single(v => v.VoteId == -4).AgendaItemId.ShouldBe(-7);
        proceedings.Items.Single(i => i.Id == -7).BillNumber.ShouldBe("XVP-0412");
        var groupVote = proceedings.Votes.Single(v => v.VoteId == -5);
        groupVote.IsConsensus.ShouldBeTrue();
        proceedings.Items.Single(i => i.Id == groupVote.AgendaItemId).BillNumber.ShouldBeNull();
    }

    [Fact]
    public void A_vote_result_has_official_totals_and_one_row_per_mp_with_faction_and_choice()
    {
        var result = SeimasParsers.VoteResult(Synthetic("p2b.ad_sp_balsavimo_rezultatai@balsavimo_id=99100002")).ShouldNotBeNull();

        result.VoteId.ShouldBe(99100002);
        result.Totals.ShouldBe(new VoteTotals(71, 38, 14, 123, 141));
        result.OfficialNote.ShouldBeNull();
        result.Ballots.Count.ShouldBe(13);
        result.Ballots.Single(b => b.PersonId == 99000001).ShouldBe(new ParsedBallot(99000001, "Jonas", "Petraitis", "RPF", BallotChoice.Against));
    }

    [Fact]
    public void The_official_mismatch_note_is_kept_and_an_empty_choice_means_did_not_vote()
    {
        var result = SeimasParsers.VoteResult(Synthetic("p2b.ad_sp_balsavimo_rezultatai@balsavimo_id=99100004")).ShouldNotBeNull();

        result.OfficialNote.ShouldNotBeNull().ShouldContain("neatitinka protokole įrašytų suminių rezultatų");
        result.Ballots.Single(b => b.LastName == "Alksninis").Choice.ShouldBe(BallotChoice.DidNotVote);
    }

    [Fact]
    public void An_empty_answer_is_no_result_not_an_error()
    {
        SeimasParsers.VoteResult(Xml("<SeimoInformacija/>")).ShouldBeNull();
        SeimasParsers.Members(Xml("<SeimoInformacija/>")).ShouldBeEmpty();
    }

    [Fact]
    public void A_per_mp_row_without_asmens_id_is_read_for_matching_by_name()
    {
        var result = SeimasParsers.VoteResult(Xml(
            "<SeimoInformacija><SeimoNariųBalsavimas balsavimo_id=\"-60034\"><BendriBalsavimoRezultatai už=\"68\" prieš=\"14\" susilaikė=\"23\"/>" +
            "<IndividualusBalsavimoRezultatas vardas=\"Ona\" pavardė=\"Jonaitienė\" frakcija=\"PSF\" kaip_balsavo=\"Už\"/></SeimoNariųBalsavimas></SeimoInformacija>"))
            .ShouldNotBeNull();

        result.VoteId.ShouldBe(-60034);
        result.Totals.ShouldBe(new VoteTotals(68, 14, 23, null, null));
        result.Ballots.Single().ShouldBe(new ParsedBallot(null, "Ona", "Jonaitienė", "PSF", BallotChoice.For));
    }

    [Fact]
    public void A_missing_key_is_a_format_error_naming_the_element_and_attribute()
    {
        var error = Should.Throw<SeimasFormatException>(() => SeimasParsers.Members(Xml("<SeimoInformacija><SeimoNarys vardas=\"Jonas\"/></SeimoInformacija>")));

        error.Message.ShouldContain("SeimoNarys");
        error.Message.ShouldContain("asmens_id");
    }

    [Fact]
    public void A_vote_outside_any_agenda_item_is_a_format_error()
    {
        Should.Throw<SeimasFormatException>(() => SeimasParsers.Proceedings(Xml(
            "<posedziai><balsavimas bals_id=\"1\"><nuo>2026-03-17 10:00</nuo></balsavimas></posedziai>")));
    }

    [Theory]
    [InlineData("Už", BallotChoice.For)]
    [InlineData("Prieš", BallotChoice.Against)]
    [InlineData("Susilaikė", BallotChoice.Abstain)]
    [InlineData("", BallotChoice.DidNotVote)]
    [InlineData(null, BallotChoice.DidNotVote)]
    [InlineData("Nedalyvavo", BallotChoice.DidNotVote)]
    public void A_ballot_value_reads_as_a_choice_and_anything_unknown_as_did_not_vote(string? value, object choice) =>
        VoteRules.Choice(value).ShouldBe((BallotChoice)choice);

    [Theory]
    [InlineData("Pritarti projektui po pateikimo", null, VoteKind.StageResult, BillStage.Presentation)]
    [InlineData("Pritarti projektui po svarstymo", null, VoteKind.StageResult, BillStage.Consideration)]
    [InlineData("Priimti įstatymą", null, VoteKind.StageResult, BillStage.Adoption)]
    [InlineData("Pritarta bendru sutarimu", BillStage.Presentation, VoteKind.StageResult, BillStage.Presentation)]
    [InlineData("Dėl Seimo nario pasiūlymo dėl 2 straipsnio", BillStage.Consideration, VoteKind.Amendment, BillStage.Consideration)]
    [InlineData("Dėl posėdžio darbotvarkės patvirtinimo", null, VoteKind.Procedural, null)]
    [InlineData("Dėl pertraukos", null, VoteKind.Other, null)]
    [InlineData("Pritarta bendru sutarimu", null, VoteKind.Other, null)]
    [InlineData("Pritarta – dėl pritarimo po pateikimo", BillStage.Presentation, VoteKind.StageResult, BillStage.Presentation)]
    [InlineData("Pritarta – dėl šio įstatymo priėmimo", BillStage.Adoption, VoteKind.StageResult, BillStage.Adoption)]
    [InlineData("Sprendimas nepriimtas – dėl šio įstatymo priėmimo", BillStage.Adoption, VoteKind.Other, null)]
    [InlineData("Sprendimas nepriimtas", BillStage.Adoption, VoteKind.Other, null)]
    [InlineData("Pritarta – dėl pagrindinio komiteto siūlymo atmesti šį projektą", BillStage.Consideration, VoteKind.Other, null)]
    public void A_vote_is_classified_from_its_wording_and_what_the_rules_cannot_place_is_other_without_a_stage(
        string wording, object? itemStage, object kind, object? stage) =>
        VoteRules.Classify(wording, (BillStage?)itemStage).ShouldBe(((VoteKind)kind, (BillStage?)stage));

    [Theory]
    [InlineData("Reformų partijos frakcija", BodyKind.Faction)]
    [InlineData("Biudžeto ir finansų komitetas", BodyKind.Committee)]
    [InlineData("Etikos ir procedūrų komisija", BodyKind.Commission)]
    [InlineData("Mišri Seimo narių grupė", BodyKind.Group)]
    [InlineData("Seimo valdyba", BodyKind.Other)]
    public void A_body_kind_is_read_from_its_name_and_the_mixed_group_is_not_a_faction(string name, object kind) =>
        VoteRules.Body(name).ShouldBe((BodyKind)kind);
}
