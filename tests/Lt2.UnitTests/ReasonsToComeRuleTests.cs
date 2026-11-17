using Lt2.Api.Contracts.Me;
using Lt2.Appeals.Ai;
using Lt2.Appeals.Domain;
using Lt2.Notifications.Follows;

namespace Lt2.UnitTests;

/// <summary>Design 8.11.4: the AI draft of a description.</summary>
public sealed class AiDraftRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_source_text_there_is_no_draft(string? source) => AiDraftRules.CheckRequest(source, 0).ShouldBe("draft.source-required");

    [Fact]
    public void A_source_over_twenty_thousand_characters_is_refused_not_cut() =>
        AiDraftRules.CheckRequest(new string('a', 20_001), 0).ShouldBe("draft.source-too-long");

    [Fact]
    public void A_moderator_may_ask_for_twenty_drafts_an_hour()
    {
        AiDraftRules.CheckRequest("Aiškinamasis raštas.", 19).ShouldBeNull();
        AiDraftRules.CheckRequest("Aiškinamasis raštas.", 20).ShouldBe("draft.too-many");
    }

    [Fact]
    public void A_description_counts_as_from_a_draft_only_when_a_draft_was_asked_for_on_the_bill_within_a_day()
    {
        AiDraftRules.CheckFromDraft(fromDraft: false, null, Now).ShouldBeNull();
        AiDraftRules.CheckFromDraft(fromDraft: true, null, Now).ShouldBe("draft.none");
        AiDraftRules.CheckFromDraft(fromDraft: true, Now.AddHours(-2), Now).ShouldBeNull();
        AiDraftRules.CheckFromDraft(fromDraft: true, Now.AddHours(-25), Now).ShouldBe("draft.none");
    }



    [Fact]
    public void The_public_prompt_asks_for_neutral_sentences_from_the_source_only()
    {
        var prompt = AiPrompts.DescriptionDraft;
        prompt.ShouldContain("{{numeris}}");
        prompt.ShouldContain("{{pavadinimas}}");
        prompt.ShouldContain("du–keturis");
        prompt.ShouldContain("Nepateik argumentų");
        prompt.ShouldContain("Iš pateikto teksto neaišku");
    }
}

/// <summary>Design 8.11.2: which follow events are sent, when, and to whom.</summary>
public sealed class FollowRuleTests
{
    // 2026-11-13 10:00 UTC is 12:00 in Vilnius.
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_planned_date_already_past_when_first_seen_is_never_sent()
    {
        FollowRules.PlannedIsSendable(new DateOnly(2026, 11, 12), Now).ShouldBeFalse();
        FollowRules.PlannedIsSendable(new DateOnly(2026, 11, 13), Now).ShouldBeTrue();
        FollowRules.PlannedIsSendable(new DateOnly(2026, 11, 19), Now).ShouldBeTrue();
    }

    [Fact]
    public void A_vote_held_more_than_three_days_before_it_was_first_seen_is_never_sent()
    {
        FollowRules.VoteIsSendable(Now.AddDays(-3), Now).ShouldBeTrue();
        FollowRules.VoteIsSendable(Now.AddDays(-3).AddMinutes(-1), Now).ShouldBeFalse();
        FollowRules.VoteIsSendable(Now.AddHours(-1), Now).ShouldBeTrue();
    }

    [Fact]
    public void At_most_one_planned_email_per_bill_per_Vilnius_day()
    {
        FollowRules.PlannedMaySendNow(null, Now).ShouldBeTrue();
        FollowRules.PlannedMaySendNow(Now.AddHours(-1), Now).ShouldBeFalse();

        // 22:30 UTC on the 12th is already the 13th in Vilnius.
        FollowRules.PlannedMaySendNow(new DateTimeOffset(2026, 11, 12, 22, 30, 0, TimeSpan.Zero), Now).ShouldBeFalse();
        FollowRules.PlannedMaySendNow(new DateTimeOffset(2026, 11, 12, 22, 30, 0, TimeSpan.Zero), Now.AddDays(1)).ShouldBeTrue();
        FollowRules.PlannedMaySendNow(new DateTimeOffset(2026, 11, 12, 20, 0, 0, TimeSpan.Zero), Now).ShouldBeTrue();
    }

    [Fact]
    public void A_waiting_date_that_moved_again_is_not_sent_the_current_one_is()
    {
        var waiting = new DateOnly(2026, 11, 17);
        FollowRules.PlannedStillHolds(waiting, waiting, Now).ShouldBeTrue();
        FollowRules.PlannedStillHolds(waiting, new DateOnly(2026, 11, 19), Now).ShouldBeFalse();
        FollowRules.PlannedStillHolds(waiting, null, Now).ShouldBeFalse();
        FollowRules.PlannedStillHolds(new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12), Now).ShouldBeFalse();
    }

    [Fact]
    public void An_event_goes_only_to_those_who_followed_before_it_was_first_seen()
    {
        FollowRules.IsRecipient(Now.AddMinutes(-1), Now, null).ShouldBeTrue();
        FollowRules.IsRecipient(Now, Now, null).ShouldBeFalse();
        FollowRules.IsRecipient(Now.AddMinutes(1), Now, null).ShouldBeFalse();
    }

    [Fact]
    public void Nobody_is_told_about_a_vote_held_before_they_followed()
    {
        var held = Now.AddHours(-2);
        FollowRules.IsRecipient(held.AddMinutes(-1), Now, held).ShouldBeTrue();
        FollowRules.IsRecipient(held.AddMinutes(1), Now, held).ShouldBeFalse();
    }

    [Fact]
    public void A_planned_event_is_keyed_by_its_date_and_a_vote_by_its_id()
    {
        FollowRules.PlannedKey(new DateOnly(2026, 11, 19)).ShouldBe("2026-11-19");
        FollowRules.PlannedDateOf("2026-11-19").ShouldBe(new DateOnly(2026, 11, 19));
        FollowRules.VotedKey(99100102).ShouldBe("99100102");
    }
}

/// <summary>Design 8.11.5: the page a share link lands on.</summary>
public sealed class LandingPathTests
{
    [Theory]
    [InlineData("/seimas/XVP-0412/kreipimasis")]
    [InlineData("/seimas/XVP-0412")]
    public void A_path_on_this_site_under_seimas_is_accepted(string path) => LandingPath.Accept(path).ShouldBe(path);

    /// <summary>Design 8.21, 8.25.2: an account screen (a visitor sent to sign in comes back to it) and a member's public page.</summary>
    [Theory]
    [InlineData("/paskyra")]
    [InlineData("/paskyra/sekimas")]
    [InlineData("/paskyra/saugumas")]
    [InlineData("/nariai/abcdefgh23")]
    [InlineData("/nariai/ruta-mockute-abcdefgh23")]
    public void An_account_screen_and_a_members_page_are_accepted(string path) => LandingPath.Accept(path).ShouldBe(path);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://piktas.example/seimas/XVP-0412")]
    [InlineData("//piktas.example/seimas/XVP-0412")]
    [InlineData("/\\piktas.example")]
    [InlineData("\\seimas\\XVP-0412")]
    [InlineData("/seimas//piktas.example")]
    [InlineData("/seimas/../paskyra")]
    [InlineData("/paskyra/sekimas/kita")]
    [InlineData("/paskyra/Sekimas")]
    [InlineData("/nariai/trumpas")]
    [InlineData("/sveiki")]
    [InlineData("/nariai/Ruta-abcdefgh23")]
    [InlineData("/nariai/abcdefgh23/x")]
    [InlineData("/administravimas/nustatymai")]
    [InlineData("seimas/XVP-0412")]
    [InlineData("/seimas/XVP-0412?x=1")]
    [InlineData("/seimas/XVP-0412#x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/seimas/XVP 0412")]
    public void Anything_else_is_ignored(string? path) => LandingPath.Accept(path).ShouldBeNull();

    /// <summary>Design 8.11.5 (owner, 2026-11-17): the share link is the bill's own address, so a link preview shows the bill.</summary>
    [Fact]
    public void The_share_link_is_the_bills_own_address_with_the_invitation_code_after_the_hash() =>
        LandingPath.ShareLink("ABCD-EFGH", "/seimas/XVP-0412").ShouldBe("/seimas/XVP-0412#kodas=ABCD-EFGH");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_usable_code_the_share_link_is_the_plain_address(string? code) =>
        LandingPath.ShareLink(code, "/seimas/XVP-0412").ShouldBe("/seimas/XVP-0412");

    [Fact]
    public void The_code_in_a_share_link_is_escaped_and_never_in_the_path_or_a_query()
    {
        var link = LandingPath.ShareLink("AB&C D", "/seimas/XVP-0412");
        link.ShouldBe("/seimas/XVP-0412#kodas=AB%26C%20D");
        link.Split('#')[0].ShouldNotContain("AB");
        link.ShouldNotContain("?");
        InvitationLink.FromFragment("https://lt2.test" + link, InvitationLink.CodeParameter).ShouldBe("AB&C D");
    }

    [Fact]
    public void The_code_and_the_page_are_read_back_from_after_the_hash_and_nothing_from_the_query()
    {
        const string Address = "https://lt2.test/prisijungti?x=1#kodas=ABCD-EFGH&tikslas=%2Fseimas%2FXVP-0412";
        InvitationLink.FromFragment(Address, InvitationLink.CodeParameter).ShouldBe("ABCD-EFGH");
        InvitationLink.FromFragment(Address, LandingPath.Parameter).ShouldBe("/seimas/XVP-0412");
        InvitationLink.FromFragment("https://lt2.test/prisijungti?kodas=ABCD-EFGH", InvitationLink.CodeParameter).ShouldBeNull();
        InvitationLink.Fragment("https://lt2.test/organizacijos/kvietimas#tok%2Ben").ShouldBe("tok+en");
        InvitationLink.Fragment("https://lt2.test/organizacijos/kvietimas").ShouldBeNull();
    }

    [Theory]
    [InlineData("https://piktas.example/")]
    [InlineData("/administravimas")]
    [InlineData("/seimas/XVP-0412?x=1")]
    public void A_share_link_never_points_off_the_site_or_to_a_page_not_on_the_list(string path) =>
        Should.Throw<ArgumentException>(() => LandingPath.ShareLink("ABCD-EFGH", path));
    /// <summary>Design 6.9 (owner, 2026-11-15): a sign-in link on a bill's page brings the person back to the bill.</summary>
    [Fact]
    public void A_sign_in_link_on_a_bills_page_carries_the_page_after_the_hash_and_only_a_page_under_seimas()
    {
        LandingPath.SignInLink("/seimas/XVP-0412").ShouldBe("/prisijungti#tikslas=%2Fseimas%2FXVP-0412");
        LandingPath.SignInLink("/administravimas").ShouldBe("/prisijungti");
        LandingPath.SignInLink("https://piktas.example/seimas/XVP-0412").ShouldBe("/prisijungti");
        LandingPath.SignInLink(null).ShouldBe("/prisijungti");
        InvitationLink.FromFragment("https://lt2.test" + LandingPath.SignInLink("/seimas/XVP-0412"), LandingPath.Parameter).ShouldBe("/seimas/XVP-0412");
    }

    [Theory]
    [InlineData("https://lt2.test/seimas/XVP-0412", "/seimas/XVP-0412")]
    [InlineData("https://lt2.test/seimas/XVP-0412?x=1#argumentai", "/seimas/XVP-0412")]
    [InlineData("https://lt2.test/", "/")]
    public void The_page_path_is_read_from_the_address_without_query_or_fragment(string address, string path) =>
        LandingPath.PathOf(address, "https://lt2.test/").ShouldBe(path);
}
