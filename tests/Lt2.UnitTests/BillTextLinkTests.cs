using Lt2.Api.Contracts.Comments;
using Lt2.Web.Client.Comments;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.23.6 (the designer's proposal of 2026-11-17): a link to a comment shows the whole discussion through it, on the
/// thread's own pages (the first 10, then 20 a step, owner 2026-11-19); every link to a comment or an argument is made in one place, the same in the contracts and the modules.
/// </summary>
public sealed class BillTextLinkTests
{
    private static readonly Guid Comment = Guid.Parse("0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid Target = Guid.Parse("0193a1b2-c3d4-7e5f-8a9b-000000000001");

    [Theory]
    [InlineData(1, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 30)]
    [InlineData(25, 30)]
    [InlineData(30, 30)]
    [InlineData(31, 50)]
    [InlineData(50, 50)]
    [InlineData(51, 70)]
    [InlineData(0, 10)]
    public void A_linked_comment_shows_everything_through_it_on_the_threads_own_pages(int position, int shown) =>
        BillTextLinks.ShownThrough(position).ShouldBe(shown);

    [Fact]
    public void The_links_carry_the_page_the_parameter_and_the_elements_own_anchor()
    {
        BillTextLinks.ToComment("XVP-0412", Comment).ShouldBe($"/seimas/XVP-0412?komentaras={Comment:N}#comment-{Comment:N}");
        BillTextLinks.ToReason("XVP-0412", Comment).ShouldBe($"/seimas/XVP-0412?argumentas={Comment:N}#reason-{Comment:D}");
        CommentLimits.ElementId(Comment).ShouldBe(BillTextLinks.CommentElement(Comment));
        Lt2.SharedKernel.BillTextLinks.ToComment("XVP-0412", Comment).ShouldBe(BillTextLinks.ToComment("XVP-0412", Comment), "the modules' copy is the same file");
        Lt2.SharedKernel.BillTextLinks.ToReason("XVP-0412", Comment).ShouldBe(BillTextLinks.ToReason("XVP-0412", Comment));
    }

    [Theory]
    [InlineData("0193a1b2c3d47e5f8a9b0c1d2e3f4a5b", true)]
    [InlineData("0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b", true)]
    [InlineData("ne-id", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_an_id_is_read_from_the_address(string? value, bool read) =>
        (BillTextLinks.Parse(value) == Comment).ShouldBe(read);

    [Fact]
    public void A_link_and_the_paging_together_show_the_more_of_the_two_and_only_under_its_own_target()
    {
        var linked = new CommentPlaceResponse(Comment, "reason", Target, "XVP-0412", 25);
        CommentPaging.Asked(null, Target, linked).ShouldBe(30);
        CommentPaging.Asked($"{Target:N}-40", Target, linked).ShouldBe(40);
        CommentPaging.Asked($"{Target:N}-20", Target, linked).ShouldBe(30);
        CommentPaging.Asked(null, Guid.NewGuid(), linked).ShouldBeNull("another target stays closed");
        CommentPaging.Asked(null, Target, null).ShouldBeNull();
        CommentPaging.Marked(Target, linked).ShouldBe(Comment);
        CommentPaging.Marked(Guid.NewGuid(), linked).ShouldBeNull();
    }
}
