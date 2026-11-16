using Lt2.Appeals.Domain;
using Lt2.Web.Client.Mps;

namespace Lt2.UnitTests;

/// <summary>Design 8.5.5, 8.5.6: the rules of an MP's response and the paging of its lists.</summary>
public sealed class MpResponseRuleTests
{
    [Theory]
    [InlineData("for", "Balsuosiu už, nes skaičiai sutampa.", null)]
    [InlineData("undecided", "Dar svarstau.", null)]
    [InlineData("abstain", "Susilaikysiu.", "response.stance-invalid")]
    [InlineData(null, "Tekstas.", "response.stance-invalid")]
    [InlineData("against", "   ", "response.empty")]
    public void A_response_has_one_of_three_stances_and_a_text(string? stance, string text, string? expected) =>
        ResponseRules.CheckResponse(stance, text, maxLength: 2000).ShouldBe(expected);

    [Fact]
    public void A_response_is_as_long_as_the_members_texts_setting_allows()
    {
        ResponseRules.CheckResponse("for", new string('a', 2000), maxLength: 2000).ShouldBeNull();
        ResponseRules.CheckResponse("for", new string('a', 2001), maxLength: 2000).ShouldBe("response.too-long");
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("open", false, null)]
    [InlineData("open", true, "response.bill-not-open")]
    [InlineData("closed", false, "response.bill-not-open")]
    [InlineData("rejected", false, "response.bill-not-open")]
    [InlineData(null, true, "response.bill-not-open")]
    public void A_response_is_written_changed_and_withdrawn_only_until_the_adoption_vote(string? recordStatus, bool finished, string? expected) =>
        ResponseRules.CheckBillOpen(recordStatus, finished).ShouldBe(expected);

    [Fact]
    public void A_response_a_moderator_hid_is_not_written_over() =>
        (ResponseRules.CheckWritable("hidden"), ResponseRules.CheckWritable("withdrawn"), ResponseRules.CheckWritable("published"), ResponseRules.CheckWritable(null))
            .ShouldBe(("response.hidden", (string?)null, (string?)null, (string?)null));

    [Fact]
    public void Changing_a_published_response_keeps_it_and_a_withdrawn_one_was_kept_when_withdrawn()
    {
        ResponseRules.KeepsVersion("published").ShouldBeTrue();
        ResponseRules.KeepsVersion("withdrawn").ShouldBeFalse();
    }

    [Fact]
    public void Saving_the_same_text_again_changes_nothing_but_writing_after_a_withdrawal_does()
    {
        ResponseRules.Unchanged("published", "for", "Tekstas.", "for", "Tekstas.").ShouldBeTrue();
        ResponseRules.Unchanged("published", "for", "Tekstas.", "against", "Tekstas.").ShouldBeFalse();
        ResponseRules.Unchanged("withdrawn", "for", "Tekstas.", "for", "Tekstas.").ShouldBeFalse();
    }

    [Fact]
    public void Only_a_published_response_is_withdrawn() =>
        (ResponseRules.CheckWithdraw("published"), ResponseRules.CheckWithdraw("withdrawn"), ResponseRules.CheckWithdraw(null))
            .ShouldBe(((string?)null, "response.not-found", "response.not-found"));

    [Fact]
    public void A_moderator_hides_what_is_shown_and_restores_only_while_the_bill_is_open()
    {
        ResponseRules.MayHide("published").ShouldBeTrue();
        ResponseRules.MayHide("withdrawn").ShouldBeTrue();
        ResponseRules.MayHide("hidden").ShouldBeFalse();
        ResponseRules.MayRestore("hidden", billOpen: true).ShouldBeTrue();
        ResponseRules.MayRestore("hidden", billOpen: false).ShouldBeFalse();
        ResponseRules.MayRestore("published", billOpen: true).ShouldBeFalse();
    }

    [Fact]
    public void A_restored_response_returns_to_what_it_was_when_hidden()
    {
        ResponseRules.RestoredStatus(null).ShouldBe("published");
        ResponseRules.RestoredStatus(DateTimeOffset.UnixEpoch).ShouldBe("withdrawn");
    }

    [Fact]
    public void The_bills_page_lists_for_against_undecided_then_the_rest() =>
        new[] { ("published", "undecided"), ("withdrawn", "for"), ("published", "against"), ("hidden", "for"), ("published", "for") }
            .OrderBy(x => ResponseRules.ListOrder(x.Item1, x.Item2))
            .Select(x => ResponseRules.ListOrder(x.Item1, x.Item2))
            .ShouldBe([0, 1, 2, 3, 3]);

    [Theory]
    [InlineData(null, 25, 10)]
    [InlineData("20", 25, 20)]
    [InlineData("25", 25, 25)]
    [InlineData("26", 25, 10)]
    [InlineData("9", 25, 10)]
    [InlineData("abc", 25, 10)]
    [InlineData("-10", 25, 10)]
    [InlineData("10", 3, 10)]
    public void A_list_shows_10_and_unknown_or_oversized_values_fall_back(string? show, int total, int expected) =>
        ResponsePaging.Shown(show, total).ShouldBe(expected);

    [Fact]
    public void Rodyti_daugiau_adds_10_and_lands_on_the_first_new_item()
    {
        ResponsePaging.More(10, 25).ShouldBe(20);
        ResponsePaging.More(20, 25).ShouldBe(25);
        ResponsePaging.More(25, 25).ShouldBeNull();
        ResponsePaging.Address("/seimas/XVP-0412", 20, ResponsePaging.ItemId(11)).ShouldBe("/seimas/XVP-0412?atsakymai=20#atsakymas-11");
        ResponsePaging.Address("/p/jonas-petraitis", 10, "atsakymai").ShouldBe("/p/jonas-petraitis#atsakymai");
    }
}
