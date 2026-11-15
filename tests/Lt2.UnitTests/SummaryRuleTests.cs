using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.13: the summary job takes active bills without a description, planned ones first (soonest first), then the
/// most participants; at most <c>Ai:SummariesPerDay</c> a day; a bill without usable text waits seven days; a description
/// a person ever wrote, corrected or removed is never written over. A moderator confirms an AI summary or removes any
/// description.
/// </summary>
public sealed class SummaryRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private sealed record Bill(string Number, DateOnly? Planned, int Participants);

    [Fact]
    public void Bills_with_a_planned_date_come_first_soonest_first_then_the_most_participants_then_by_number()
    {
        var bills = new[]
        {
            new Bill("XVP-0001", null, 5),
            new Bill("XVP-0002", new DateOnly(2026, 11, 20), 0),
            new Bill("XVP-0003", null, 40),
            new Bill("XVP-0004", new DateOnly(2026, 11, 17), 1),
            new Bill("XVP-0005", new DateOnly(2026, 11, 17), 9),
            new Bill("XVP-0006", null, 5),
        };

        SummaryRules.Order(bills, b => b.Planned, b => b.Participants, b => b.Number).Select(b => b.Number)
            .ShouldBe(["XVP-0005", "XVP-0004", "XVP-0002", "XVP-0003", "XVP-0001", "XVP-0006"]);
    }

    [Fact]
    public void A_bill_without_a_record_or_with_an_open_record_without_a_description_is_due()
    {
        SummaryRules.IsDue(null, null, touchedByPerson: false, retryAfter: null, Now).ShouldBeTrue();
        SummaryRules.IsDue(BillRecordStatuses.Open, "", touchedByPerson: false, retryAfter: null, Now).ShouldBeTrue();
    }

    [Fact]
    public void A_description_is_never_written_over_whoever_made_it()
    {
        SummaryRules.IsDue(BillRecordStatuses.Open, "Moderatoriaus aprašymas.", touchedByPerson: true, retryAfter: null, Now).ShouldBeFalse();
        SummaryRules.IsDue(BillRecordStatuses.Open, "Ankstesnė DI santrauka.", touchedByPerson: false, retryAfter: null, Now).ShouldBeFalse();
    }

    [Fact]
    public void A_description_a_person_removed_is_never_made_again() =>
        SummaryRules.IsDue(BillRecordStatuses.Open, "", touchedByPerson: true, retryAfter: null, Now).ShouldBeFalse();

    [Theory]
    [InlineData(BillRecordStatuses.Closed)]
    [InlineData(BillRecordStatuses.Rejected)]
    public void A_closed_or_hidden_record_gets_no_summary(string status) =>
        SummaryRules.IsDue(status, "", touchedByPerson: false, retryAfter: null, Now).ShouldBeFalse();

    [Theory]
    [InlineData(SummaryOutcomes.Unavailable)]
    [InlineData(SummaryOutcomes.Insufficient)]
    public void A_bill_whose_text_was_unavailable_or_not_enough_is_tried_again_after_seven_days_not_sooner(string outcome)
    {
        var retry = SummaryRules.RetryAfter(outcome, Now);
        retry.ShouldBe(Now.AddDays(7));
        SummaryRules.IsDue(null, null, false, retry, Now.AddDays(7).AddMinutes(-1)).ShouldBeFalse();
        SummaryRules.IsDue(null, null, false, retry, Now.AddDays(7)).ShouldBeTrue();
    }

    [Fact]
    public void A_bill_on_which_the_provider_failed_is_tried_again_the_next_day() =>
        SummaryRules.RetryAfter(SummaryOutcomes.Failed, Now).ShouldBe(Now.AddDays(1));

    [Theory]
    [InlineData(30, 0, 30)]
    [InlineData(30, 29, 1)]
    [InlineData(30, 30, 0)]
    [InlineData(30, 31, 0)]
    [InlineData(0, 0, 0)]
    public void The_daily_number_counts_what_was_asked_of_the_provider_today_and_0_switches_it_off(int perDay, int asked, int left) =>
        SummaryRules.LeftToday(perDay, asked).ShouldBe(left);

    [Theory]
    [InlineData("Iš pateikto teksto neaišku, ką projektas keičia.", true)]
    [InlineData("„Iš pateikto teksto neaišku, ką projektas keičia.“", true)]
    [InlineData("Projektas nustato trečią pajamų mokesčio tarifą.", false)]
    public void The_prompts_not_enough_answer_is_never_stored(string answer, bool notEnough) =>
        SummaryRules.IsNotEnough(answer).ShouldBe(notEnough);

    [Fact]
    public void Why_the_job_makes_nothing_is_said_once_not_on_every_run_and_again_when_the_reason_changes()
    {
        var notices = new Lt2.Appeals.Application.SummaryJobNotices();
        notices.ShouldSay("no-reader").ShouldBeTrue();
        notices.ShouldSay("no-reader").ShouldBeFalse();
        notices.ShouldSay("no-reader").ShouldBeFalse();
        notices.ShouldSay("no-provider").ShouldBeTrue();
        notices.ShouldSay("no-reader").ShouldBeTrue();
    }

    [Fact]
    public void A_moderator_confirms_only_an_AI_summary_nobody_has_read_on_an_open_record()
    {
        RecordRules.CheckConfirm(BillRecordStatuses.Open, DescriptionSources.Ai).ShouldBeNull();
        RecordRules.CheckConfirm(BillRecordStatuses.Open, DescriptionSources.AiDraft).ShouldBe("moderation.wrong-state");
        RecordRules.CheckConfirm(BillRecordStatuses.Open, DescriptionSources.Moderator).ShouldBe("moderation.wrong-state");
        RecordRules.CheckConfirm(BillRecordStatuses.Open, null).ShouldBe("moderation.wrong-state");
        RecordRules.CheckConfirm(BillRecordStatuses.Closed, DescriptionSources.Ai).ShouldBe("moderation.wrong-state");
    }

    [Fact]
    public void A_moderator_removes_any_description_with_a_statement()
    {
        RecordRules.CheckRemove(BillRecordStatuses.Open, "Aprašymas.", "Netikslus.").ShouldBeNull();
        RecordRules.CheckRemove(BillRecordStatuses.Open, "Aprašymas.", " ").ShouldBe("statement.required");
        RecordRules.CheckRemove(BillRecordStatuses.Open, "", "Netikslus.").ShouldBe("moderation.wrong-state");
        RecordRules.CheckRemove(BillRecordStatuses.Rejected, "Aprašymas.", "Netikslus.").ShouldBe("moderation.wrong-state");
    }

    [Theory]
    [InlineData(DescriptionSources.Ai)]
    [InlineData(DescriptionSources.AiDraft)]
    public void A_moderators_correction_of_AI_text_is_labelled_as_corrected_by_people(string source) =>
        RecordRules.SourceOfModeratorText(fromDraft: false, source).ShouldBe(DescriptionSources.AiDraft);

    [Fact]
    public void A_moderators_correction_of_a_moderators_or_initiators_text_is_a_moderators_text()
    {
        RecordRules.SourceOfModeratorText(fromDraft: false, DescriptionSources.Moderator).ShouldBe(DescriptionSources.Moderator);
        RecordRules.SourceOfModeratorText(fromDraft: false, DescriptionSources.Initiator).ShouldBe(DescriptionSources.Moderator);
    }
}
