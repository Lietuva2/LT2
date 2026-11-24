using Lt2.Appeals;
using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.12.1, 8.12.3: every active bill is open; one record per bill; moderation after the fact.</summary>
public sealed class RecordRuleTests
{
    [Fact]
    public void A_member_takes_part_on_an_active_bill_without_a_record_and_on_an_open_record()
    {
        RecordRules.CheckParticipation(null, billFinished: false).ShouldBeNull();
        RecordRules.CheckParticipation(BillRecordStatuses.Open, billFinished: false).ShouldBeNull();
    }

    [Theory]
    [InlineData(BillRecordStatuses.Closed)]
    [InlineData(BillRecordStatuses.Rejected)]
    public void A_closed_or_hidden_record_takes_no_sides_or_arguments(string status) =>
        RecordRules.CheckParticipation(status, billFinished: false).ShouldBe("record.not-open");

    [Fact]
    public void A_finished_bill_nobody_took_part_in_gets_no_record() =>
        RecordRules.CheckParticipation(null, billFinished: true).ShouldBe("record.not-open");

    [Fact]
    public void Taking_part_ends_when_the_final_vote_is_recorded_before_the_record_is_closed()
    {
        RecordRules.CheckParticipation(BillRecordStatuses.Open, billFinished: true).ShouldBe("record.not-open");
        RecordRules.IsFinished(BillRecordStatuses.Open, finalVoteRecorded: true).ShouldBeTrue();
        RecordRules.IsFinished(BillRecordStatuses.Closed, finalVoteRecorded: false).ShouldBeTrue();
        RecordRules.IsFinished(null, finalVoteRecorded: false).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, true, "statement.required")]
    [InlineData("  ", true, "statement.required")]
    [InlineData(null, false, null)]
    [InlineData("Paaiškinimas.", true, null)]
    public void A_moderators_statement_is_checked_where_it_is_stored(string? statement, bool required, string? error) =>
        StatementRules.Check(statement, required).ShouldBe(error);

    [Fact]
    public void A_moderators_statement_is_at_most_1000_characters()
    {
        StatementRules.Check(new string('a', 1000), required: true).ShouldBeNull();
        StatementRules.Check(new string('a', 1001), required: false).ShouldBe("staff-statement.too-long");
    }

    [Theory]
    [InlineData(BillRecordStatuses.Open, true)]
    [InlineData(BillRecordStatuses.Closed, true)]
    [InlineData(BillRecordStatuses.Rejected, true)]
    public void Open_closed_and_hidden_rows_are_the_bills_record(string status, bool record) =>
        RecordRules.IsRecord(status).ShouldBe(record);

    [Fact]
    public void A_hidden_record_comes_back_as_it_was()
    {
        RecordRules.RestoredStatus(closedAt: null).ShouldBe(BillRecordStatuses.Open);
        RecordRules.RestoredStatus(closedAt: DateTimeOffset.UnixEpoch).ShouldBe(BillRecordStatuses.Closed);
        RecordRules.CheckRestore(BillRecordStatuses.Rejected).ShouldBeNull();
        RecordRules.CheckRestore(BillRecordStatuses.Open).ShouldBe("moderation.wrong-state");
    }

    [Fact]
    public void A_moderator_writes_a_description_on_an_open_record_with_a_statement_and_a_changed_text()
    {
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, "", "Projektas keičia rinkliavas.", "Projektas darbotvarkėje.").ShouldBeNull();
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, "", "Projektas keičia rinkliavas.", " ").ShouldBe("statement.required");
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Rejected, "", "Projektas keičia rinkliavas.", "Taisau.").ShouldBe("moderation.wrong-state");
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, "Tas pats.", "Tas pats.", "Taisau.").ShouldBe("description.unchanged");
    }

    [Fact]
    public void A_description_is_unchanged_only_when_the_text_the_address_and_the_plain_title_all_are()
    {
        const string Text = "Tas pats.";
        const string Url = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas";
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, Text, Text, "Taisau.", Url, Url, "Pavadinimas", "Pavadinimas").ShouldBe("description.unchanged");
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, Text, Text, "Taisau.", Url, Url, "Pavadinimas", "  Pavadinimas ").ShouldBe("description.unchanged", "the title is compared normalised");
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, Text, Text, "Taisau.", Url, Url, null, " ").ShouldBe("description.unchanged", "a blank title is none");
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, Text, Text, "Taisau.", Url, Url, "Pavadinimas", "Kitas pavadinimas").ShouldBeNull();
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, Text, Text, "Taisau.", Url, Url, null, "Pavadinimas").ShouldBeNull("a title added to an untitled text");
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Open, Text, Text, "Taisau.", Url, Url, "Pavadinimas", null).ShouldBeNull("a title removed");
    }

    [Fact]
    public void After_the_final_vote_a_moderator_still_corrects_or_removes_the_description_but_does_not_confirm_it()
    {
        RecordRules.CheckDescriptionWrite(BillRecordStatuses.Closed, "Senas.", "Pataisytas.", "Taisau klaidą.").ShouldBeNull();
        RecordRules.CheckRemove(BillRecordStatuses.Closed, "Senas.", "Klaidingas.").ShouldBeNull();
        RecordRules.CheckRemove(BillRecordStatuses.Rejected, "Senas.", "Klaidingas.").ShouldBe("moderation.wrong-state");
        RecordRules.CheckConfirm(BillRecordStatuses.Closed, DescriptionSources.Ai).ShouldBe("moderation.wrong-state");
    }

    [Theory]
    [InlineData("keep", null)]
    [InlineData("describe", null)]
    [InlineData("undescribe", null)]
    [InlineData("threshold", "moderation.wrong-state")]
    [InlineData("hold", "moderation.wrong-state")]
    [InlineData("release", "moderation.wrong-state")]
    [InlineData("urgent", "moderation.wrong-state")]
    [InlineData("confirm", "moderation.wrong-state")]
    public void On_a_closed_record_only_palikti_and_the_description_remain_of_the_record_actions(string action, string? error)
    {
        RecordRules.CheckRecordAction(BillRecordStatuses.Closed, action).ShouldBe(error);
        RecordRules.CheckRecordAction(BillRecordStatuses.Open, action).ShouldBeNull();
        RecordRules.CheckRecordAction(BillRecordStatuses.Rejected, action).ShouldBe("moderation.wrong-state");
    }

    [Theory]
    [InlineData("hide", ReasonStatuses.Published, null)]
    [InlineData("hide", ReasonStatuses.Pending, null)]
    [InlineData("restore", ReasonStatuses.Hidden, null)]
    [InlineData("publish", ReasonStatuses.Published, null)]
    [InlineData("publish", ReasonStatuses.Pending, "moderation.wrong-state")]
    [InlineData("merge", ReasonStatuses.Published, "moderation.wrong-state")]
    public void On_a_closed_record_an_argument_is_only_hidden_restored_or_kept(string action, string reasonStatus, string? error)
    {
        RecordRules.CheckReasonAction(BillRecordStatuses.Closed, action, reasonStatus).ShouldBe(error);
        RecordRules.CheckReasonAction(BillRecordStatuses.Open, action, reasonStatus).ShouldBeNull();
        RecordRules.CheckReasonAction(BillRecordStatuses.Rejected, action, reasonStatus).ShouldBe("moderation.wrong-state");
    }

    [Fact]
    public void A_description_started_from_a_draft_keeps_its_label_through_later_corrections()
    {
        RecordRules.SourceOfModeratorText(fromDraft: true, currentSource: null).ShouldBe(DescriptionSources.AiDraft);
        RecordRules.SourceOfModeratorText(fromDraft: false, currentSource: null).ShouldBe(DescriptionSources.Moderator);
        RecordRules.SourceOfModeratorText(fromDraft: false, currentSource: DescriptionSources.AiDraft).ShouldBe(DescriptionSources.AiDraft);
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(1, true, false, true)]
    [InlineData(1, false, false, false)]
    [InlineData(1, true, true, false)]
    public void A_bill_under_its_threshold_on_the_agenda_needs_a_moderator_only_once_someone_takes_part(int participants, bool planned, bool reached, bool listed) =>
        RecordRules.UnderThresholdOnAgenda(participants, planned, reached).ShouldBe(listed);
}

/// <summary>Design 8.12.5 (decided 2026-11-14): the orders of /projektai and the front page's cards, and the search.</summary>
public sealed class BillListRuleTests
{
    private sealed record Bill(string Number, int Participants, int Arguments, DateOnly? Planned);

    private static readonly DateOnly Soon = new(2026, 11, 17);
    private static readonly DateOnly Later = new(2026, 11, 24);

    private static string[] Order(string sort, params Bill[] bills) =>
        BillListRules.Order(bills, sort, b => b.Participants, b => b.Arguments, b => b.Planned, b => b.Number).Select(b => b.Number).ToArray();

    [Fact]
    public void Activity_is_participants_plus_published_arguments() => BillListRules.Activity(participants: 10, arguments: 3).ShouldBe(13);

    [Fact]
    public void By_the_latest_activity_the_last_touched_comes_first_and_bills_without_any_last()
    {
        var at = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var last = new Dictionary<string, DateTimeOffset?> { ["A"] = at, ["B"] = at.AddMinutes(5), ["C"] = null, ["D"] = at.AddDays(-1) };
        Bill[] bills = [new("A", 50, 0, null), new("B", 1, 0, null), new("C", 99, 9, Soon), new("D", 3, 0, null)];
        BillListRules.Order(bills, BillSorts.Latest, b => b.Participants, b => b.Arguments, b => b.Planned, b => b.Number, b => last[b.Number])
            .Select(b => b.Number).ShouldBe(["B", "A", "D", "C"], "owner, 2026-11-17: the latest first, none last whatever its activity");
        BillSorts.Of("latest").ShouldBe(BillSorts.Latest);
    }

    [Fact]
    public void By_activity_the_most_active_comes_first_and_activity_is_the_default()
    {
        Bill[] bills = [new("A", 5, 0, null), new("B", 3, 4, null), new("C", 6, 0, null)];
        Order(BillSorts.Activity, bills).ShouldBe(["B", "C", "A"]);
        Order(BillSorts.Of(null), bills).ShouldBe(["B", "C", "A"]);
        Order(BillSorts.Of("nonsense"), bills).ShouldBe(["B", "C", "A"]);
    }

    [Fact]
    public void Bills_with_nothing_come_last_by_planned_date_then_number()
    {
        Order(BillSorts.Activity,
                new("X-3", 0, 0, null), new("X-2", 0, 0, Later), new("X-1", 0, 0, Soon), new("X-9", 1, 0, null), new("X-0", 0, 0, null))
            .ShouldBe(["X-9", "X-1", "X-2", "X-0", "X-3"]);
    }

    [Fact]
    public void Equal_activity_goes_by_planned_date_then_by_number()
    {
        Order(BillSorts.Activity, new("B", 2, 1, Later), new("C", 3, 0, Soon), new("A", 1, 2, Later))
            .ShouldBe(["C", "A", "B"]);
    }

    [Fact]
    public void By_participants_and_by_arguments_ties_go_by_activity()
    {
        Bill[] bills = [new("A", 5, 1, null), new("B", 5, 4, null), new("C", 1, 9, null)];
        Order(BillSorts.Participants, bills).ShouldBe(["B", "A", "C"]);
        Order(BillSorts.Arguments, bills).ShouldBe(["C", "B", "A"]);
    }

    [Fact]
    public void By_planned_date_the_soonest_first_and_bills_without_one_last()
    {
        Order(BillSorts.Planned, new("A", 9, 9, null), new("B", 0, 0, Later), new("C", 0, 0, Soon), new("D", 1, 0, Later))
            .ShouldBe(["C", "D", "B", "A"]);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("xvp-0412", true)]
    [InlineData("pajamų mokesčio", true)]
    [InlineData("MOKESČIO pajamų", true)]
    [InlineData("pajamų rinkliavų", false)]
    public void The_search_keeps_bills_with_every_word_in_the_number_or_title(string? query, bool found) =>
        BillListRules.Matches(query, "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa").ShouldBe(found);
}
