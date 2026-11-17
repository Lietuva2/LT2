using Lt2.Api.Contracts.Me;
using Lt2.Appeals.Domain;
using Lt2.Notifications.Follows;
using Lt2.Web.Client.Appeals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.8: what is new to a member on a bill they follow (made public or changed after their last visit, never their own),
/// the moment a visit keeps, and how the lists say it in Lithuanian.
/// </summary>
public sealed class BillNewsRuleTests
{
    private static readonly DateTimeOffset LastVisit = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Me = Guid.Parse("0193a1b2-0000-7000-8000-0000000000e1");
    private static readonly Guid Someone = Guid.Parse("0193a1b2-0000-7000-8000-0000000000e2");
    private static readonly Guid MyOrganisation = Guid.Parse("0193a1b2-0000-7000-8000-0000000000e3");
    private static readonly Guid OtherOrganisation = Guid.Parse("0193a1b2-0000-7000-8000-0000000000e4");
    private const long Petraitis = 1001;
    private const long Jonaitiene = 1002;

    [Fact]
    public void A_text_made_public_after_the_last_visit_is_new() =>
        NewsRules.IsNew(LastVisit.AddMinutes(1), null, LastVisit, own: false).ShouldBeTrue();

    [Fact]
    public void A_text_made_public_at_or_before_the_last_visit_is_not_new()
    {
        NewsRules.IsNew(LastVisit, null, LastVisit, own: false).ShouldBeFalse("seen at that moment");
        NewsRules.IsNew(LastVisit.AddDays(-1), null, LastVisit, own: false).ShouldBeFalse();
    }

    [Fact]
    public void An_older_text_changed_after_the_last_visit_is_new_again() =>
        NewsRules.IsNew(LastVisit.AddDays(-3), LastVisit.AddHours(2), LastVisit, own: false).ShouldBeTrue();

    [Fact]
    public void The_members_own_text_is_never_new() =>
        NewsRules.IsNew(LastVisit.AddHours(1), LastVisit.AddHours(2), LastVisit, own: true).ShouldBeFalse();

    [Fact]
    public void A_statement_is_own_when_written_by_the_member_or_of_an_organisation_they_represent()
    {
        var represented = new HashSet<Guid> { MyOrganisation };
        NewsRules.OwnStatement(Someone, MyOrganisation, Me, represented).ShouldBeTrue("a co-representative wrote it");
        NewsRules.OwnStatement(Me, OtherOrganisation, Me, represented).ShouldBeTrue("written before leaving that organisation");
        NewsRules.OwnStatement(Someone, OtherOrganisation, Me, represented).ShouldBeFalse();
        NewsRules.OwnStatement(null, OtherOrganisation, Me, represented).ShouldBeFalse();
    }

    [Fact]
    public void A_response_is_own_for_whoever_speaks_for_that_mp()
    {
        NewsRules.OwnResponse(Someone, Petraitis, Me, speaksFor: Petraitis).ShouldBeTrue("the MP's account or the office");
        NewsRules.OwnResponse(Me, Jonaitiene, Me, speaksFor: null).ShouldBeTrue("written by the member");
        NewsRules.OwnResponse(Someone, Jonaitiene, Me, speaksFor: Petraitis).ShouldBeFalse();
    }

    [Fact]
    public void A_comment_is_own_when_written_by_the_member_or_in_a_voice_they_share()
    {
        var represented = new HashSet<Guid> { MyOrganisation };
        NewsRules.OwnComment(Me, null, null, Me, represented, null).ShouldBeTrue();
        NewsRules.OwnComment(Someone, MyOrganisation, null, Me, represented, null).ShouldBeTrue();
        NewsRules.OwnComment(Someone, null, Petraitis, Me, represented, Petraitis).ShouldBeTrue();
        NewsRules.OwnComment(Someone, OtherOrganisation, null, Me, represented, Petraitis).ShouldBeFalse();
        NewsRules.OwnComment(null, null, null, Me, represented, null).ShouldBeFalse("a deleted account's comment");
    }

    [Fact]
    public void A_visit_keeps_now_and_never_goes_back()
    {
        FollowRules.Seen(LastVisit, LastVisit.AddHours(1)).ShouldBe(LastVisit.AddHours(1));
        FollowRules.Seen(LastVisit, LastVisit.AddHours(-1)).ShouldBe(LastVisit, "two pages opened at once, or a clock behind");
    }

    [Theory]
    [InlineData(1, NumberForm.One)]
    [InlineData(21, NumberForm.One)]
    [InlineData(101, NumberForm.One)]
    [InlineData(2, NumberForm.Few)]
    [InlineData(9, NumberForm.Few)]
    [InlineData(22, NumberForm.Few)]
    [InlineData(0, NumberForm.Many)]
    [InlineData(10, NumberForm.Many)]
    [InlineData(11, NumberForm.Many)]
    [InlineData(12, NumberForm.Many)]
    [InlineData(19, NumberForm.Many)]
    [InlineData(20, NumberForm.Many)]
    [InlineData(111, NumberForm.Many)]
    [InlineData(1000, NumberForm.Many)]
    public void A_noun_after_a_number_takes_its_lithuanian_form(int n, NumberForm form) =>
        NumberForms.Of(n).ShouldBe(form);

    [Theory]
    [InlineData(1, 0, 0, 0, "1 naujas argumentas")]
    [InlineData(3, 5, 0, 0, "3 nauji argumentai · 5 nauji komentarai")]
    [InlineData(10, 21, 0, 0, "10 naujų argumentų · 21 naujas komentaras")]
    [InlineData(0, 12, 1, 0, "12 naujų komentarų · 1 nauja organizacijos pozicija")]
    [InlineData(0, 0, 2, 0, "2 naujos organizacijų pozicijos")]
    [InlineData(0, 0, 15, 0, "15 naujų organizacijų pozicijų")]
    [InlineData(0, 0, 0, 1, "1 naujas atsakymas")]
    [InlineData(0, 0, 0, 4, "4 nauji atsakymai")]
    [InlineData(0, 0, 0, 30, "30 naujų atsakymų")]
    public void The_lists_say_what_is_new_with_each_kinds_number_form(int reasons, int comments, int statements, int responses, string expected) =>
        NewsLine.Of(Localizer(), new BillNewsCountsResponse(reasons, comments, statements, responses)).ShouldBe(expected);

    [Fact]
    public void The_new_texts_keep_a_text_new_itself_or_with_new_comments_under_only_new()
    {
        var reason = Guid.NewGuid();
        var discussed = Guid.NewGuid();
        var other = Guid.NewGuid();
        var comment = Guid.NewGuid();
        var news = new NewTexts(new BillSeenResponse(true, LastVisit, [reason], [new NewCommentResponse(comment, "reason", discussed)], [], []));

        news.Any.ShouldBeTrue();
        news.IsNew(reason).ShouldBeTrue();
        news.IsNew(comment).ShouldBeTrue();
        news.IsNew(discussed).ShouldBeFalse();
        news.Keeps(discussed).ShouldBeTrue();
        news.Keeps(other).ShouldBeFalse();
        new NewTexts(BillSeenResponse.NotFollowing).Any.ShouldBeFalse();
    }

    private static IStringLocalizer<AppealUi> Localizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        return services.BuildServiceProvider().GetRequiredService<IStringLocalizer<AppealUi>>();
    }
}
