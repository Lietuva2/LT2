using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
using Lt2.Appeals;
using Lt2.Appeals.Domain;
using Lt2.Web.Client.Agenda;
using Lt2.Web.Client.Analytics;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Comments;
using Lt2.Web.Client.Mps;
using Lt2.Web.Client.Parliament;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.16.4 (owner, 2026-11-19): with "show more" the first page stays as small as each list says, and every further page
/// is larger: a list of rows adds 50 a press, a list of members' long texts 20. One place holds both numbers
/// (<see cref="ListPaging"/>), the same file in the contracts and the modules; each list's first press is checked here.
/// </summary>
public sealed class ListPagingTests
{
    [Fact]
    public void A_list_of_rows_grows_by_fifty_and_a_list_of_texts_by_twenty()
    {
        (ListPaging.RowStep, ListPaging.TextStep).ShouldBe((50, 20));
        (Lt2.SharedKernel.ListPaging.RowStep, Lt2.SharedKernel.ListPaging.TextStep).ShouldBe((ListPaging.RowStep, ListPaging.TextStep), "the modules' copy is the same file");
    }

    [Theory]
    [InlineData(10, 50, 300, 60)]
    [InlineData(260, 50, 300, 300)]
    [InlineData(300, 50, 300, 300)]
    [InlineData(2, 20, 1000, 22)]
    public void The_next_page_is_a_step_more_never_beyond_the_cap(int shown, int step, int cap, int next) =>
        ListPaging.Next(shown, step, cap).ShouldBe(next);

    [Theory]
    [InlineData(1, 10, 20, 10)]
    [InlineData(10, 10, 20, 10)]
    [InlineData(11, 10, 20, 30)]
    [InlineData(30, 10, 20, 30)]
    [InlineData(31, 10, 20, 50)]
    [InlineData(0, 10, 20, 10)]
    [InlineData(61, 10, 50, 110)]
    public void Showing_through_an_item_lands_on_the_lists_own_pages(int position, int first, int step, int shown) =>
        ListPaging.Through(position, first, step).ShouldBe(shown);

    [Fact]
    public void The_agenda_day_grows_from_ten_to_sixty_and_the_front_pages_from_five_to_fifty_five()
    {
        AgendaRules.NextAsked(AgendaRules.PagePerDay, 200).ShouldBe(60);
        AgendaRules.NextAsked(AgendaRules.FrontPerDay, 200).ShouldBe(55);
        AgendaRules.NextAsked(60, 200).ShouldBe(110);
        AgendaRules.NextAsked(10, 48).ShouldBe(60, "a day of 48 asks for 60 and shows all 48");
    }

    [Fact]
    public void The_lists_of_rows_grow_by_fifty_from_their_first_page()
    {
        BillListPaging.More(BillListPaging.First, 0, 500).ShouldBe(70, "/projektai: 20, then 70");
        BillListPaging.More(170, 0, 500).ShouldBe(BillListPaging.Cap, "the last press before the cap adds fewer");
        VoteHistory.More(VoteHistory.Read(null, null, null, null), 30, 1000).ShouldBe("/seimas/balsavimai?rodyti=80#vote-31");
        MpVotePaging.More(MpVotePaging.First, hasMore: true).ShouldBe(100);
        AnalyticsList.More(AnalyticsList.Read(null, null, null, null, null, null, null, null, null), 400, "XVP-0412")
            .ShouldBe("/statistika?rodyti=60#bill-XVP-0412");
        ProfileLimits.More(null).ShouldBe(60, "a member's page: 10, then 60");
        ProfileLimits.More(480).ShouldBe(ProfileLimits.MaxShown);
        BallotList.Step.ShouldBe(ListPaging.RowStep);
    }

    [Fact]
    public void The_lists_of_texts_grow_by_twenty_from_their_first_page()
    {
        ReasonPaging.NextAsked(new ReasonPageResponse("for", [], 40, ReasonLimits.First.ToString(System.Globalization.CultureInfo.InvariantCulture), []))
            .ShouldBe(22, "the arguments: 2 a side, then 22");
        CommentPaging.More(CommentLimits.First, 100).ShouldBe(30, "the comments: 10, then 30");
        ResponsePaging.More(MpResponseLimits.First, 100).ShouldBe(30, "the MPs' responses: 10, then 30");
    }

    [Fact]
    public void The_api_pages_of_texts_default_to_one_step_and_stay_bounded()
    {
        ReasonPages.Limit(null).ShouldBe(ListPaging.TextStep);
        ReasonPages.Limit(ReasonLimits.Step).ShouldBe(20, "a +20 page is allowed");
        ReasonPages.Limit(100_000).ShouldBe(ReasonPages.Max);
        ReasonLimits.Max.ShouldBe(ReasonPages.Max);
        CommentRules.Limit(null, 10).ShouldBe(ListPaging.TextStep, "a later page of comments");
        CommentRules.Limit(CommentLimits.Step, 10).ShouldBe(20);
        CommentRules.Step.ShouldBe(CommentLimits.Step);
    }
}
