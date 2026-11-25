using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client.Appeals;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.3 (owner, 2026-11-25): the moderators' page opens on the tab asked for; without one, on the first work tab with
/// something waiting, or on Projektai when none has; never on a reference tab.
/// </summary>
public sealed class ModerationTabRuleTests
{
    private static ModerationCountsResponse Counts(
        int records = 0, int reasons = 0, int organisations = 0, int statements = 0, int responses = 0, int profiles = 0, int comments = 0,
        int undescribed = 0, int hidden = 0, int restrictions = 0) =>
        new(records, reasons, organisations, statements, responses, profiles, comments, undescribed, hidden, restrictions);

    [Fact]
    public void The_tab_asked_for_opens_whatever_the_counts() =>
        ModerationTabs.Open(ModerationTabs.Hidden, Counts(records: 3)).ShouldBe(ModerationTabs.Hidden);

    [Fact]
    public void Without_a_tab_the_first_work_tab_with_something_waiting_opens() =>
        ModerationTabs.Open(null, Counts(statements: 2, comments: 1, undescribed: 40)).ShouldBe(ModerationTabs.Statements);

    [Fact]
    public void With_nothing_waiting_Projektai_opens_never_a_reference_tab() =>
        ModerationTabs.Open(null, Counts(undescribed: 240, hidden: 5, restrictions: 1)).ShouldBe(ModerationTabs.Records);

    [Theory]
    [InlineData("nera-tokios")]
    [InlineData("")]
    public void A_tab_that_does_not_exist_counts_as_none_asked_for(string asked) =>
        ModerationTabs.Open(asked, Counts(responses: 1)).ShouldBe(ModerationTabs.Responses);

    [Fact]
    public void Without_counts_Projektai_opens() =>
        ModerationTabs.Open(null, null).ShouldBe(ModerationTabs.Records);

    [Fact]
    public void The_work_tabs_come_in_the_designs_order_then_the_reference_tabs() =>
        ModerationTabs.Work.Concat(ModerationTabs.Reference).ShouldBe(
            ["projektai", "argumentai", "organizacijos", "pozicijos", "atsakymai", "profiliai", "komentarai", "be-aprasymo", "paslepti", "apribojimai"]);
}
