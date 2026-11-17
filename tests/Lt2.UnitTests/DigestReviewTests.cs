using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Notifications.Digests;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.19.2 (owner, 2026-11-17): which arguments a prepared summary holds, how a hidden one is replaced, and the moderators'
/// notice of a prepared summary.
/// </summary>
public sealed class DigestReviewTests
{
    private static readonly Guid Record = Guid.NewGuid();
    private static readonly DateTimeOffset Prepared = new(2026, 11, 15, 5, 0, 0, TimeSpan.Zero);

    private static DigestPoolReason Reason(string side, int marks, int minutesBefore = 60, Guid? record = null) =>
        new(Guid.NewGuid(), record ?? Record, side, side + " " + marks, marks, Prepared.AddMinutes(-minutesBefore));

    [Fact]
    public void A_summary_holds_the_most_endorsed_published_arguments_of_each_side_in_that_order()
    {
        var a = Reason("for", 5);
        var b = Reason("for", 9);
        var c = Reason("for", 1);
        var d = Reason("against", 2);
        var chosen = ChoiceRules.Choose([a, b, c, d], perSide: 2);
        chosen.Where(x => x.Side == "for").OrderBy(x => x.Place).Select(x => x.ReasonId).ShouldBe([b.Id, a.Id]);
        chosen.Single(x => x.Side == "against").ReasonId.ShouldBe(d.Id, "a side with fewer holds what it has");
        chosen.ShouldAllBe(x => !x.Replacement);
    }

    [Fact]
    public void Equal_marks_go_to_the_argument_public_first()
    {
        var later = Reason("for", 3, minutesBefore: 10);
        var earlier = Reason("for", 3, minutesBefore: 90);
        ChoiceRules.Choose([later, earlier], 1).Single().ReasonId.ShouldBe(earlier.Id);
    }

    [Fact]
    public void Hiding_a_chosen_argument_brings_in_the_next_of_its_side_by_marks_now()
    {
        var first = Reason("for", 9);
        var second = Reason("for", 5);
        var third = Reason("for", 1);
        var fourth = Reason("for", 0);
        var against = Reason("against", 4);
        var chosen = ChoiceRules.Choose([first, second, third, fourth, against], 2);

        // The first is hidden; since preparation the fourth gained more marks than the third.
        var now = new[] { second, third, fourth with { Endorsements = 7 }, against };
        var change = ChoiceRules.Replace(chosen, now);
        change.Leaving.ShouldBe([first.Id]);
        var coming = change.Coming.ShouldHaveSingleItem();
        (coming.ReasonId, coming.Side, coming.Place, coming.Replacement).ShouldBe((fourth.Id, "for", 3, true));
    }

    [Fact]
    public void Nothing_changes_while_every_chosen_argument_is_still_published()
    {
        var pool = new[] { Reason("for", 3), Reason("for", 2), Reason("for", 1) };
        ChoiceRules.Replace(ChoiceRules.Choose(pool, 2), pool).IsEmpty.ShouldBeTrue("a third argument does not come in by itself");
    }

    [Fact]
    public void An_argument_once_chosen_never_comes_back_as_a_replacement_and_a_side_without_more_holds_fewer()
    {
        var a = Reason("for", 3);
        var b = Reason("for", 2);
        var chosen = new List<ChosenReason>
        {
            new(a.Id, Record, "for", 1, Left: true),
            new(b.Id, Record, "for", 2),
        };

        // a was hidden earlier and restored since: it is in the pool again but was chosen before; b is hidden now.
        var change = ChoiceRules.Replace(chosen, [a]);
        change.Leaving.ShouldBe([b.Id]);
        change.Coming.ShouldBeEmpty();
    }

    [Fact]
    public void A_replacement_comes_from_the_same_bill_and_side_only()
    {
        var other = Guid.NewGuid();
        var mine = Reason("for", 5);
        var elsewhere = Reason("for", 50, record: other);
        var otherSide = Reason("against", 50);
        var chosen = ChoiceRules.Choose([mine], 1);
        ChoiceRules.Replace(chosen, [elsewhere, otherSide]).Coming.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, "1 projektas")]
    [InlineData(2, "2 projektai")]
    [InlineData(9, "9 projektai")]
    [InlineData(10, "10 projektų")]
    [InlineData(11, "11 projektų")]
    [InlineData(12, "12 projektų")]
    [InlineData(21, "21 projektas")]
    [InlineData(22, "22 projektai")]
    [InlineData(0, "0 projektų")]
    public void A_count_takes_its_lithuanian_form(int n, string text) =>
        LithuanianCounts.Pick(n, "{0} projektas", "{0} projektai", "{0} projektų").Replace("{0}", n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(text);

    [Fact]
    public void The_moderators_are_told_when_it_goes_how_many_bills_and_arguments_and_where_to_review_it()
    {
        var template = new DigestReviewTemplate(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = "https://lt2.test" }));
        var monday = DigestRules.At(new DateOnly(2026, 11, 16), new TimeOnly(7, 0));
        var email = template.Render(new DigestReviewEmail("moderatore@pavyzdys.test", "weekly", monday, 3, 5, DigestService.ReviewPath));
        email.Subject.ShouldBe("Suvestinė Seimo nariams išeis 2026-11-16 07:00");
        email.TextBody.ShouldContain("Savaitinė suvestinė Seimo nariams paruošta ir išeis 2026-11-16 07:00: 3 projektai, 5 argumentai.");
        email.TextBody.ShouldContain("https://lt2.test/moderavimas/suvestines");
        email.TextBody.ShouldContain("patvirtinti suvestinę arba jos nesiųsti");
    }

    [Fact]
    public void The_notice_links_to_the_moderators_summaries_screen() =>
        DigestService.ReviewPath.ShouldBe(DigestRoutes.ModerationPage);
}
