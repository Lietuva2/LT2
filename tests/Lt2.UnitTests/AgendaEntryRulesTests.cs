using Lt2.Api.Contracts.Parliament;
using Lt2.Parliament.Domain;
using Lt2.Web.Client.Appeals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.4, "Entering a day's agenda" (owner, 2026-11-25): how staff's lines for one sitting day are read
/// (<c>number | stage | title</c>), which day and document address are accepted, and what saving the list does to the day.
/// </summary>
public sealed class AgendaEntryRulesTests
{
    [Fact]
    public void A_line_is_a_number_a_stage_and_a_title_separated_by_bars_each_trimmed()
    {
        var parse = AgendaEntryRules.Parse("  XVP-1980 |  antrasis svarstymas | 2027–2029 metų biudžeto patvirtinimo įstatymo projektas  ");
        parse.Refused.ShouldBeEmpty();
        parse.Lines.ShouldBe([new EntryLine(1, "XVP-1980", BillStages.Consideration, "2027–2029 metų biudžeto patvirtinimo įstatymo projektas")]);
    }

    [Fact]
    public void Blank_lines_are_ignored_and_a_line_keeps_its_number_in_the_text()
    {
        var parse = AgendaEntryRules.Parse("\r\nXVP-0412 | svarstymas | Gyventojų pajamų mokesčio įstatymo pataisa\n   \n\tXVP-0533 | pateikimas | Kitas projektas\r");
        parse.Refused.ShouldBeEmpty();
        parse.Lines.Select(l => (l.Line, l.Number)).ShouldBe([(2, "XVP-0412"), (4, "XVP-0533")]);
        AgendaEntryRules.Parse("").Lines.ShouldBeEmpty();
        AgendaEntryRules.Parse(null).Refused.ShouldBeEmpty();
    }

    [Fact]
    public void The_number_is_read_as_everywhere_its_letters_kept_and_its_revision_dropped()
    {
        var parse = AgendaEntryRules.Parse("XVP-782(4) | svarstymas | Pataisa\nXIVP-4009GR | pateikimas | Iš ankstesnės kadencijos\nXVP-781(2) | | Be stadijos");
        parse.Refused.ShouldBeEmpty();
        parse.Lines.Select(l => l.Number).ShouldBe(["XVP-782", "XIVP-4009GR", "XVP-781"]);
    }

    [Fact]
    public void The_stage_is_the_first_stage_word_in_any_form_a_combined_stage_its_furthest_and_none_when_the_line_names_none()
    {
        AgendaEntryRules.StageOf("antrasis svarstymas").ShouldBe(BillStages.Consideration);
        AgendaEntryRules.StageOf("Pateikimas").ShouldBe(BillStages.Presentation);
        AgendaEntryRules.StageOf("pateikimas, svarstymas, priėmimas").ShouldBe(BillStages.Adoption);
        AgendaEntryRules.StageOf("svarstymas, priėmimas").ShouldBe(BillStages.Adoption);
        AgendaEntryRules.StageOf("priėmimo").ShouldBe(BillStages.Adoption);
        AgendaEntryRules.StageOf("").ShouldBeNull();
        AgendaEntryRules.StageOf("informacija").ShouldBeNull();
        AgendaEntryRules.Parse("XVP-2092 |  | Seimo nutarimo projektas").Lines.Single().Stage.ShouldBeNull();
    }

    [Fact]
    public void A_line_without_exactly_three_fields_is_refused_with_its_line_number()
    {
        var parse = AgendaEntryRules.Parse("XVP-0412 svarstymas Pataisa\nXVP-0533 | pateikimas | Projektas | papildomai\n\nXVP-0618 | svarstymas");
        parse.Lines.ShouldBeEmpty();
        parse.Refused.ShouldBe([new EntryRefusal(1, AgendaEntryRefusals.Fields), new EntryRefusal(2, AgendaEntryRefusals.Fields), new EntryRefusal(4, AgendaEntryRefusals.Fields)]);
    }

    [Fact]
    public void A_line_without_a_bill_number_is_refused()
    {
        var parse = AgendaEntryRules.Parse("Darbotvarkės tvirtinimas | | Posėdžio darbotvarkė\nxvp-0412 | svarstymas | Mažosios raidės\nXVP-0412 | svarstymas | Pataisa");
        parse.Refused.ShouldBe([new EntryRefusal(1, AgendaEntryRefusals.Number), new EntryRefusal(2, AgendaEntryRefusals.Number)]);
        parse.Lines.Single().Number.ShouldBe("XVP-0412");
    }

    [Fact]
    public void A_title_is_required_and_at_most_2000_characters()
    {
        var parse = AgendaEntryRules.Parse(
            $"XVP-0412 | svarstymas |   \nXVP-0533 | svarstymas | {new string('a', AgendaEntryRules.TitleMaxLength + 1)}\nXVP-0618 | svarstymas | {new string('b', AgendaEntryRules.TitleMaxLength)}");
        AgendaEntryRules.TitleMaxLength.ShouldBe(2000);
        parse.Refused.ShouldBe([new EntryRefusal(1, AgendaEntryRefusals.TitleEmpty), new EntryRefusal(2, AgendaEntryRefusals.TitleTooLong)]);
        parse.Lines.Single().Number.ShouldBe("XVP-0618");
    }

    [Fact]
    public void A_number_listed_twice_in_a_day_keeps_its_first_line()
    {
        var parse = AgendaEntryRules.Parse("XVP-0412 | pateikimas | Pirmoji\nXVP-0412(2) | priėmimas | Antroji\nXVP-0533 | svarstymas | Kita");
        parse.Refused.ShouldBeEmpty();
        parse.Lines.ShouldBe([new EntryLine(1, "XVP-0412", BillStages.Presentation, "Pirmoji"), new EntryLine(3, "XVP-0533", BillStages.Consideration, "Kita")]);
    }

    [Fact]
    public void A_day_is_accepted_from_today_up_to_31_days_ahead()
    {
        var today = new DateOnly(2026, 11, 25);
        AgendaEntryRules.DayAccepted(today, today).ShouldBeTrue();
        AgendaEntryRules.DayAccepted(today.AddDays(31), today).ShouldBeTrue();
        AgendaEntryRules.DayAccepted(today.AddDays(32), today).ShouldBeFalse();
        AgendaEntryRules.DayAccepted(today.AddDays(-1), today).ShouldBeFalse();
    }

    [Fact]
    public void The_document_is_an_https_address_on_e_seimas_or_lrs()
    {
        const string document = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAK/901fcdf1e3e711f191c781adb2af86f6";
        AgendaEntryRules.Source(document).ShouldBe(document);
        AgendaEntryRules.Source("  " + document + " ").ShouldBe(document);
        AgendaEntryRules.Source("https://www.lrs.lt/sip/portal.show?p_r=35727").ShouldNotBeNull();
        AgendaEntryRules.Source("https://E-SEIMAS.LRS.LT/portal/x").ShouldNotBeNull();
        AgendaEntryRules.Source("http://e-seimas.lrs.lt/portal/x").ShouldBeNull("https only");
        AgendaEntryRules.Source("https://lrs.lt/x").ShouldBeNull();
        AgendaEntryRules.Source("https://e-seimas.lrs.lt.pavyzdys.test/x").ShouldBeNull();
        AgendaEntryRules.Source("https://user@e-seimas.lrs.lt/x").ShouldBeNull();
        AgendaEntryRules.Source("https://e-seimas.lrs.lt:8443/x").ShouldBeNull();
        AgendaEntryRules.Source("e-seimas.lrs.lt/portal/x").ShouldBeNull();
        AgendaEntryRules.Source("").ShouldBeNull();
        AgendaEntryRules.Source(null).ShouldBeNull();
        AgendaEntryRules.Source("https://e-seimas.lrs.lt/" + new string('a', AgendaEntryRules.SourceMaxLength)).ShouldBeNull();
    }

    [Fact]
    public void The_list_is_the_whole_day_a_bill_is_added_has_its_stage_changed_is_unchanged_or_is_taken_off()
    {
        var current = new[]
        {
            new EnteredRow("XVP-0412", BillStages.Presentation),
            new EnteredRow("XVP-0533", BillStages.Consideration),
            new EnteredRow("XVP-0707", null),
            new EnteredRow("XVP-0618", BillStages.Adoption),
        };
        var lines = AgendaEntryRules.Parse("XVP-0533 | svarstymas | A\nXVP-0412 | svarstymas | B\nXVP-9001 | pateikimas | C\nXVP-0707 | | D").Lines;
        var diff = AgendaEntryRules.Diff(current, lines);
        diff.Bills.Select(b => (b.Line.Number, b.Change)).ShouldBe(
        [
            ("XVP-0533", AgendaEntryChanges.Unchanged),
            ("XVP-0412", AgendaEntryChanges.StageChanged),
            ("XVP-9001", AgendaEntryChanges.Added),
            ("XVP-0707", AgendaEntryChanges.Unchanged),
        ]);
        diff.Removed.ShouldBe(["XVP-0618"]);
        (diff.Count(AgendaEntryChanges.Added), diff.Count(AgendaEntryChanges.StageChanged), diff.Count(AgendaEntryChanges.Unchanged)).ShouldBe((1, 1, 2));

        var first = AgendaEntryRules.Diff([], lines);
        first.Bills.ShouldAllBe(b => b.Change == AgendaEntryChanges.Added);
        first.Removed.ShouldBeEmpty();
        AgendaEntryRules.Diff(current, []).Removed.ShouldBe(["XVP-0412", "XVP-0533", "XVP-0618", "XVP-0707"]);
    }

    [Fact]
    public void Every_refusal_and_change_has_its_text_on_the_entry_page()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        var l = services.BuildServiceProvider().GetRequiredService<IStringLocalizer<AppealUi>>();
        foreach (var key in AgendaEntryRefusals.All.Select(r => "AgendaEntry.Refusal." + r).Concat(AgendaEntryChanges.All.Select(c => "AgendaEntry.Change." + c)))
        {
            l[key].ResourceNotFound.ShouldBeFalse(key);
        }
    }
}
