using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.1 (decided 2026-11-13): several proposals per bill until one is published.</summary>
public sealed class ProposalRuleTests
{
    private const string Pending = AppealStatuses.Pending;
    private const string Returned = AppealStatuses.Returned;
    private const string Open = AppealStatuses.Open;
    private const string Closed = AppealStatuses.Closed;
    private const string Rejected = AppealStatuses.Rejected;
    private const string Superseded = AppealStatuses.Superseded;

    [Fact]
    public void Anyone_may_propose_on_a_bill_with_no_appeal() => ProposalRules.CheckNewProposal([]).ShouldBeNull();

    [Theory]
    [InlineData(Pending)]
    [InlineData(Returned)]
    public void Someone_elses_waiting_or_returned_proposal_blocks_nobody(string status) =>
        ProposalRules.CheckNewProposal([(status, false)]).ShouldBeNull();

    [Theory]
    [InlineData(Open)]
    [InlineData(Closed)]
    public void Nobody_may_propose_while_the_bill_has_a_published_appeal(string status)
    {
        ProposalRules.CheckNewProposal([(status, false)]).ShouldBe("appeal.exists");
        ProposalRules.CheckNewProposal([(status, true)]).ShouldBe("appeal.exists");
    }

    [Theory]
    [InlineData(Pending)]
    [InlineData(Returned)]
    public void A_person_has_at_most_one_waiting_or_returned_proposal_per_bill(string status) =>
        ProposalRules.CheckNewProposal([(status, true)]).ShouldBe("proposal.exists");

    [Theory]
    [InlineData(Rejected)]
    [InlineData(Superseded)]
    public void A_hidden_or_closed_proposal_blocks_nobody_not_even_its_author(string status)
    {
        ProposalRules.CheckNewProposal([(status, true)]).ShouldBeNull();
        ProposalRules.CheckNewProposal([(status, false)]).ShouldBeNull();
    }

    [Fact]
    public void There_is_no_cap_on_proposals_per_bill() =>
        ProposalRules.CheckNewProposal(Enumerable.Repeat((Pending, false), 500)).ShouldBeNull();

    [Fact]
    public void The_count_of_waiting_proposals_holds_waiting_and_returned_ones_only() =>
        ProposalRules.Waiting([Pending, Returned, Pending, Rejected, Superseded, Open]).ShouldBe(3);

    [Fact]
    public void A_waiting_proposal_is_published_while_nothing_else_on_the_bill_is() =>
        ProposalRules.CheckPublish(Pending, [Pending, Returned, Rejected, Superseded]).ShouldBeNull();

    [Theory]
    [InlineData(Open)]
    [InlineData(Closed)]
    public void A_proposal_is_not_published_while_another_appeal_on_the_bill_is(string other) =>
        ProposalRules.CheckPublish(Pending, [other]).ShouldBe("appeal.published-exists");

    [Theory]
    [InlineData(Returned)]
    [InlineData(Rejected)]
    [InlineData(Superseded)]
    public void Only_a_waiting_proposal_is_published(string status) =>
        ProposalRules.CheckPublish(status, []).ShouldBe("moderation.wrong-state");

    [Theory]
    [InlineData(Pending, true)]
    [InlineData(Returned, true)]
    [InlineData(Rejected, false)]
    [InlineData(Superseded, false)]
    [InlineData(Open, false)]
    public void Publishing_closes_the_other_waiting_and_returned_proposals_only(string status, bool closed) =>
        ProposalRules.ClosedWhenAnotherIsPublished(status).ShouldBe(closed);

    [Fact]
    public void A_hidden_appeal_comes_back_as_it_was_before_the_hide()
    {
        var at = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
        ProposalRules.RestoredStatus(at, at.AddDays(9)).ShouldBe(Closed);
        ProposalRules.RestoredStatus(at, null).ShouldBe(Open);
        ProposalRules.RestoredStatus(null, null).ShouldBe(Pending, "one hidden before publication waits for a moderator again");
    }

    [Theory]
    [InlineData(Open)]
    [InlineData(Closed)]
    [InlineData(Pending)]
    public void Restoring_is_refused_while_another_appeal_on_the_bill_is_published(string restoresAs)
    {
        ProposalRules.CheckRestore(Rejected, restoresAs, [(Open, false)]).ShouldBe("appeal.published-exists");
        ProposalRules.CheckRestore(Rejected, restoresAs, [(Closed, true)]).ShouldBe("appeal.published-exists");
    }

    [Fact]
    public void Restoring_a_published_appeal_is_allowed_while_only_proposals_wait() =>
        ProposalRules.CheckRestore(Rejected, Open, [(Pending, false), (Returned, true), (Rejected, false), (Superseded, false)]).ShouldBeNull();

    [Fact]
    public void A_proposal_hidden_before_publication_is_not_restored_while_its_author_has_another_waiting() =>
        ProposalRules.CheckRestore(Rejected, Pending, [(Pending, true)]).ShouldBe("proposal.exists");

    [Fact]
    public void A_proposal_hidden_before_publication_is_restored_beside_other_peoples_proposals() =>
        ProposalRules.CheckRestore(Rejected, Pending, [(Pending, false), (Returned, false)]).ShouldBeNull();

    [Theory]
    [InlineData(Open)]
    [InlineData(Pending)]
    [InlineData(Superseded)]
    public void Only_a_hidden_appeal_is_restored(string status) =>
        ProposalRules.CheckRestore(status, Open, []).ShouldBe("moderation.wrong-state");
}
