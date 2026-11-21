using Lt2.Api.Contracts.Digests;
using Lt2.Web.Client.Agenda;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.12.2 (owner, 2026-11-21): a list row of a bill whose threshold is reached names, in place of "riba pasiekta", the
/// summary planned that would hold it, else the last summary really sent that held it; a closed record only the last one
/// sent. No trial variants: a dry run is not a sent summary (the caller never gives one), and a planned day is a planned day
/// whatever the sending mode. A row under its threshold is unchanged.
/// </summary>
public sealed class RowSummaryRulesTests
{
    private static readonly DateOnly Planned = new(2026, 11, 30);
    private static readonly DateOnly Sent = new(2026, 11, 16);

    [Fact]
    public void An_open_reached_bill_with_a_planned_summary_names_the_planned_day_even_after_one_was_sent() =>
        RowSummaryRules.Of("open", reached: true, Planned, Sent).ShouldBe(new RecordSummaryResponse("planned", Planned));

    [Fact]
    public void An_open_reached_bill_with_none_planned_names_the_last_summary_sent() =>
        RowSummaryRules.Of("open", reached: true, planned: null, Sent).ShouldBe(new RecordSummaryResponse("sent", Sent));

    [Fact]
    public void An_open_reached_bill_with_none_planned_and_none_sent_names_no_summary_and_says_the_threshold_is_reached() =>
        RowSummaryRules.Of("open", reached: true, planned: null, lastSent: null).ShouldBeNull();

    [Fact]
    public void A_closed_reached_bill_names_only_the_last_summary_sent_never_a_planned_one()
    {
        RowSummaryRules.Of("closed", reached: true, Planned, Sent).ShouldBe(new RecordSummaryResponse("sent", Sent));
        RowSummaryRules.Of("closed", reached: true, Planned, lastSent: null).ShouldBeNull();
    }

    [Fact]
    public void A_bill_under_its_threshold_names_no_summary_open_or_closed()
    {
        RowSummaryRules.Of("open", reached: false, Planned, Sent).ShouldBeNull();
        RowSummaryRules.Of("closed", reached: false, planned: null, Sent).ShouldBeNull();
    }

    [Fact]
    public void A_hidden_record_names_no_summary() =>
        RowSummaryRules.Of("rejected", reached: true, Planned, Sent).ShouldBeNull();
}
