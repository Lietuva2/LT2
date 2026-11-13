using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.1 (decided 2026-11-13): the initiator of an open appeal proposes a new description; a moderator approves it.</summary>
public sealed class DescriptionChangeRuleTests
{
    private const string Shown = "Dabartinis aprašymas.";
    private const string Proposed = "Naujas aprašymas.";

    [Fact]
    public void The_initiator_of_an_open_appeal_may_propose_a_new_description() =>
        DescriptionChangeRules.CheckProposal(AppealStatuses.Open, isInitiator: true, hasPending: false, Shown, Proposed).ShouldBeNull();

    [Fact]
    public void Only_the_initiator_proposes_a_new_description() =>
        DescriptionChangeRules.CheckProposal(AppealStatuses.Open, isInitiator: false, hasPending: false, Shown, Proposed).ShouldBe("appeal.initiator-only");

    [Theory]
    [InlineData(AppealStatuses.Closed)]
    [InlineData(AppealStatuses.Pending)]
    [InlineData(AppealStatuses.Returned)]
    public void Not_on_a_closed_or_unpublished_appeal(string status) =>
        DescriptionChangeRules.CheckProposal(status, isInitiator: true, hasPending: false, Shown, Proposed).ShouldBe("appeal.not-open");

    [Fact]
    public void One_pending_proposal_at_a_time() =>
        DescriptionChangeRules.CheckProposal(AppealStatuses.Open, isInitiator: true, hasPending: true, Shown, Proposed).ShouldBe("description-change.pending");

    [Fact]
    public void A_proposal_must_differ_from_the_text_shown_now() =>
        DescriptionChangeRules.CheckProposal(AppealStatuses.Open, isInitiator: true, hasPending: false, Shown, Shown).ShouldBe("description.unchanged");

    [Fact]
    public void A_moderator_decides_a_pending_change_while_the_appeal_is_open()
    {
        DescriptionChangeRules.CheckDecision(DescriptionChangeStatuses.Pending, AppealStatuses.Open).ShouldBeNull();
        DescriptionChangeRules.CheckDecision(DescriptionChangeStatuses.Pending, AppealStatuses.Closed).ShouldBe("moderation.wrong-state");
        DescriptionChangeRules.CheckDecision(DescriptionChangeStatuses.Approved, AppealStatuses.Open).ShouldBe("moderation.wrong-state");
        DescriptionChangeRules.CheckDecision(DescriptionChangeStatuses.Returned, AppealStatuses.Open).ShouldBe("moderation.wrong-state");
    }
}
