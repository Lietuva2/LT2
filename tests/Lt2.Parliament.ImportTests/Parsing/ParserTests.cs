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

    [Theory]
    [InlineData("Gyventojų pajamų mokesčio įstatymo pataisa (Nr. XVP-0412)", "Gyventojų pajamų mokesčio įstatymo pataisa", "XVP-0412")]
    [InlineData("Gyventojų pajamų mokesčio įstatymo pataisa (Nr. XVP-0412(2))", "Gyventojų pajamų mokesčio įstatymo pataisa", "XVP-0412")]
    [InlineData("Gyventojų pajamų mokesčio įstatymo pataisa (Nr. XIVP-0412GR)", "Gyventojų pajamų mokesčio įstatymo pataisa", "XIVP-0412GR")]
    [InlineData("Gyventojų pajamų mokesčio įstatymo pataisa (Nr. XIVP-0412GR(2))", "Gyventojų pajamų mokesčio įstatymo pataisa", "XIVP-0412GR")]
    [InlineData("Gyventojų pajamų mokesčio įstatymo pataisa (Nr. XIVP-0412(2)GR)", "Gyventojų pajamų mokesčio įstatymo pataisa", "XIVP-0412GR")]
    [InlineData("Mokesčių paketas (Nr. XVP-0412(2), XVP-0413 ir XVP-0414(3))", "Mokesčių paketas", "XVP-0412")]
    [InlineData("Įstatymo Nr. IX-123 pakeitimo įstatymo projektas (Nr. XVP-0412) (tęsinys)", "Įstatymo Nr. IX-123 pakeitimo įstatymo projektas (Nr. XVP-0412) (tęsinys)", "XVP-0412")]
    [InlineData("Įstatymo projektas (Nr. XVP-0412) dėl įstatymo (Nr. IX-123)", "Įstatymo projektas (Nr. XVP-0412) dėl įstatymo (Nr. IX-123)", "XVP-0412")]
    [InlineData("Dėl posėdžio darbotvarkės", "Dėl posėdžio darbotvarkės", null)]
    public void The_bill_of_an_item_is_the_first_number_of_its_titles_last_number_part(string title, string shown, string? number) =>
        SeimasParsers.BillInTitle(title).ShouldBe((shown, number));

    [Theory]
    [InlineData("XVP-0412", "XVP-0412")]
    [InlineData("XVP-0412(2)", "XVP-0412")]
    [InlineData("XIVP-0412GR", "XIVP-0412GR")]
    [InlineData("XIVP-0412GR(3)", "XIVP-0412GR")]
    [InlineData("XVP-0412 Svarstymas", "XVP-0412")]
    [InlineData("XVP-0412Svarstymas", "XVP-0412")]
    [InlineData("Įstatymo Nr. IX-123 pakeitimas", null)]
    public void A_registration_number_keeps_its_letters_and_drops_its_revision(string text, string? number) =>
        BillNumbers.First(text).ShouldBe(number);

    [Theory]
    [InlineData("XVP-0412", "XVP-0412", 1)]
    [InlineData(" XVP-0412(2) ", "XVP-0412", 2)]
    [InlineData("XIVP-0412GR(3)", "XIVP-0412GR", 3)]
    [InlineData("XIVP-0412(2)GR", "XIVP-0412GR", 2)]
    [InlineData("26-14366", null, 0)]
    [InlineData("26-13964(3)", null, 0)]
    [InlineData("XVP-0412 ir XVP-0413", null, 0)]
    [InlineData("Nr. XVP-0412", null, 0)]
    [InlineData("", null, 0)]
    public void A_tais_project_number_is_a_bill_only_when_it_is_a_registration_number_and_nothing_else(string text, string? number, int revision) =>
        BillNumbers.Whole(text).ShouldBe(number is null ? null : (number, revision));

    [Fact]
    public void Tais_projects_give_one_row_per_seimas_bill_the_highest_revision_winning_and_skip_other_institutions_projects()
    {
        var projects = SeimasParsers.Projects(Xml("""
            <TAISProjEvents eventsDate="2026-11-12" creationDate="2026-11-12 15:00:00">
              <Events><Event action="action.dhs.infoLinkDocument" eventDate="2026-11-12 08:00:00" oid="a1" relatedDocOid="b1" linkName="Projektą lydintis dokumentas"/></Events>
              <Documents>
                <Document sort="Įstatymo projektas" title=" Vietos savivaldos įstatymo pakeitimo įstatymo projektas " projectRegistrationNo="XVP-0745" itemType="TAP" url="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-XVP-0745">
                  <Links><Link typeCode="000009"><relatedDoc title="Aiškinamasis raštas" registrationNo="XVP-0999" url="https://e-seimas.lrs.lt/portal/legalAct/lt/TAK/SINTETINIS"/></Link></Links>
                </Document>
                <Document sort="Įsakymo projektas" title="Dėl išgalvoto tvarkos aprašo patvirtinimo" projectRegistrationNo="26-00412" itemType="TAP" url="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-26-00412"/>
                <Document sort="Įstatymo projektas" title="Atliekų tvarkymo įstatymo 5 straipsnio pakeitimo įstatymo projektas" projectRegistrationNo="XVP-0746" itemType="TAP" url="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-XVP-0746-1"/>
                <Document sort="Įstatymo projektas" title="Atliekų tvarkymo įstatymo 5 ir 7 straipsnių pakeitimo įstatymo projektas" projectRegistrationNo="XVP-0746(2)" itemType="TAP" url="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-XVP-0746-2"/>
                <Document sort="Įstatymo projektas" title="Ta pati redakcija, antras dokumentas" projectRegistrationNo="XVP-0746(2)" itemType="TAP" url="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-XVP-0746-2B"/>
                <Document sort="Įstatymo projektas" title="" projectRegistrationNo="XIVP-0412GR(2)" itemType="TAP" url="javascript:alert(1)"/>
                <Document sort="Įstatymo projektas" title="Be numerio" itemType="TAP"/>
              </Documents>
            </TAISProjEvents>
            """));

        projects.ShouldBe(
        [
            new ParsedProject("XVP-0745", "Vietos savivaldos įstatymo pakeitimo įstatymo projektas", "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-XVP-0745"),
            new ParsedProject("XVP-0746", "Atliekų tvarkymo įstatymo 5 ir 7 straipsnių pakeitimo įstatymo projektas", "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-XVP-0746-2"),
            new ParsedProject("XIVP-0412GR", null, null),
        ]);
    }

    [Fact]
    public void The_stand_ins_tais_projects_hold_two_known_bills_and_one_ministrys_project()
    {
        SeimasParsers.Projects(Synthetic("p2b.tais_docs")).Select(p => p.BillNumber).ShouldBe(["XVP-0412", "XVP-0618"]);
        SeimasParsers.Projects(Xml("<TAISProjEvents eventsDate=\"2026-11-15\"><Events/><Documents/></TAISProjEvents>")).ShouldBeEmpty();
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
        result.OfficialNote.ShouldBe(VoteRules.MismatchNote, "the synthetic totals are Seimas-wide, its 13 rows cannot add up to them");
        result.Ballots.Count.ShouldBe(13);
        result.Ballots.Single(b => b.PersonId == 99000001).ShouldBe(new ParsedBallot(99000001, "Jonas", "Petraitis", "RPF", BallotChoice.Against));
    }

    [Fact]
    public void A_note_on_this_vote_is_kept_and_an_empty_choice_means_did_not_vote()
    {
        var result = SeimasParsers.VoteResult(Synthetic("p2b.ad_sp_balsavimo_rezultatai@balsavimo_id=99100004")).ShouldNotBeNull();

        result.OfficialNote.ShouldNotBeNull().ShouldContain("neatitinka protokole įrašytų suminių rezultatų");
        result.Ballots.Single(b => b.LastName == "Alksninis").Choice.ShouldBe(BallotChoice.DidNotVote);
    }

    [Fact]
    public void The_standing_disclaimer_on_a_result_is_not_its_official_note()
    {
        var result = SeimasParsers.VoteResult(Xml(
            "<SeimoInformacija><SeimoNariųBalsavimas balsavimo_id=\"-54969\"><BendriBalsavimoRezultatai už=\"28\" prieš=\"60\" susilaikė=\"11\" " +
            "komentaras=\"Elektroninėmis priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų\"/>" +
            "</SeimoNariųBalsavimas></SeimoInformacija>"))
            .ShouldNotBeNull();

        result.OfficialNote.ShouldBeNull();
    }

    [Fact]
    public void Rows_that_add_up_drop_the_disclaimer_and_rows_that_do_not_bring_back_its_sentence()
    {
        const string disclaimer = "komentaras=\"Elektroninėmis priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų\"";
        const string rows =
            "<IndividualusBalsavimoRezultatas vardas=\"Jonas\" pavardė=\"Petraitis\" kaip_balsavo=\"Prieš\"/>" +
            "<IndividualusBalsavimoRezultatas vardas=\"Ona\" pavardė=\"Jonaitienė\" kaip_balsavo=\"Už\"/>" +
            "<IndividualusBalsavimoRezultatas vardas=\"Rūta\" pavardė=\"Mockutė\" kaip_balsavo=\"\"/>";
        static ParsedVoteResult Parse(string totals, string rows) => SeimasParsers.VoteResult(Xml(
            "<SeimoInformacija><SeimoNariųBalsavimas balsavimo_id=\"-54969\"><BendriBalsavimoRezultatai " + totals + "/>" + rows +
            "</SeimoNariųBalsavimas></SeimoInformacija>")).ShouldNotBeNull();

        Parse("už=\"1\" prieš=\"1\" susilaikė=\"0\" " + disclaimer, rows).OfficialNote.ShouldBeNull();
        Parse("už=\"2\" prieš=\"1\" susilaikė=\"0\" " + disclaimer, rows).OfficialNote.ShouldBe(VoteRules.MismatchNote);
        Parse("už=\"2\" prieš=\"1\" susilaikė=\"0\" komentaras=\"\"", rows).OfficialNote.ShouldBe(VoteRules.MismatchNote, "flagged by our count, not by lrs.lt's comment");
    }

    [Theory]
    [InlineData(1, 1, 0, "Už,Prieš,", true)]
    [InlineData(1, 1, 1, "Už,Prieš,", false)]
    [InlineData(2, 0, 0, "Už,Prieš", false)]
    [InlineData(1, 1, 0, "", true)]
    [InlineData(null, 1, 0, "Prieš", true)]
    [InlineData(0, 0, 0, ",,", true)]
    public void Per_mp_rows_add_up_when_their_counts_equal_the_three_totals_and_nothing_to_compare_adds_up(int? @for, int? against, int? abstain, string choices, bool addsUp) =>
        VoteRules.RowsAddUp(
            new VoteTotals(@for, against, abstain, null, null),
            choices.Length == 0 ? [] : choices.Split(',').Select(VoteRules.Choice).ToList()).ShouldBe(addsUp);

    [Fact]
    public void A_result_that_does_not_add_up_shows_lrs_sentence_before_any_other_note_of_the_seimas()
    {
        var totals = new VoteTotals(2, 0, 0, null, null);
        BallotChoice[] rows = [BallotChoice.For];

        VoteRules.OfficialNote(VoteRules.StandingDisclaimer, totals, rows).ShouldBe(VoteRules.MismatchNote);
        VoteRules.OfficialNote(" Balsavimas pakartotas. ", totals, rows).ShouldBe(VoteRules.MismatchNote + " Balsavimas pakartotas.");
        VoteRules.OfficialNote(" Balsavimas pakartotas. ", new VoteTotals(1, 0, 0, null, null), rows).ShouldBe("Balsavimas pakartotas.");
        VoteRules.MismatchNote.ShouldBe("Elektroninėmis priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų.");
    }

    [Theory]
    [InlineData("Elektroninėmis priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų", null)]
    [InlineData("  Elektroninėmis priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų. ", null)]
    [InlineData("ELEKTRONINĖMIS PRIEMONĖMIS GAUTI INDIVIDUALŪS BALSAVIMO REZULTATAI NEATITINKA PROTOKOLE ĮRAŠYTŲ SUMINIŲ REZULTATŲ", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("Seimo narių individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų.", "Seimo narių individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų.")]
    [InlineData(" Balsavimas pakartotas. ", "Balsavimas pakartotas.")]
    public void An_official_note_is_the_seimas_comment_without_its_standing_disclaimer(string? komentaras, string? note) =>
        VoteRules.OfficialNote(komentaras).ShouldBe(note);

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
