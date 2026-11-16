using Lt2.Appeals.Domain;
using Lt2.Appeals.Screening;
using Lt2.Notifications.Follows;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Lt2.Web.Client.Comments;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.23: who may comment where and in whose name, the text's rules, deleting, "Sutinku", what is shown, the paging and
/// the links, and the author's notice.
/// </summary>
public sealed class CommentRuleTests
{
    private static readonly Guid Target = Guid.Parse("0193a1b2-0000-7000-8000-0000000000c1");

    [Fact]
    public void A_comment_is_as_long_as_the_members_texts_setting_allows_and_not_blank()
    {
        CommentRules.CheckText(new string('a', 2000), 2000).ShouldBeNull();
        CommentRules.CheckText(new string('a', 2001), 2000).ShouldBe("comment.too-long");
        CommentRules.CheckText(new string('a', 5000), 0).ShouldBeNull("0 is no limit");
        CommentRules.CheckText("  \n ", 2000).ShouldBe("comment.empty");
    }

    [Theory]
    [InlineData("Rašykite man: ruta@pavyzdys.test", "email")]
    [InlineData("Skambinkite +370 612 34567", "phone")]
    [InlineData("Mano kodas 39001010000", "personal-code")]
    public void Personal_data_refuses_a_comment_and_says_what_was_found(string text, string flag) =>
        CommentRules.RefusedBy(RuleBasedPreScreening.Screen(text)).ShouldBe([flag]);

    [Fact]
    public void A_link_alone_does_not_refuse_a_comment() =>
        CommentRules.RefusedBy(RuleBasedPreScreening.Screen("Šaltinis: https://pavyzdys.test/tyrimas")).ShouldBeEmpty();

    [Fact]
    public void A_kind_switched_off_takes_no_new_comments()
    {
        CommentRules.CheckSwitch(true).ShouldBeNull();
        CommentRules.CheckSwitch(false).ShouldBe("comments.off");
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("open", false, null)]
    [InlineData("open", true, "comment.bill-not-open")]
    [InlineData("closed", false, "comment.bill-not-open")]
    [InlineData("rejected", false, "comment.bill-not-open")]
    public void Comments_are_written_only_while_the_bill_takes_part(string? record, bool finished, string? expected) =>
        CommentRules.CheckBillOpen(record, finished).ShouldBe(expected);

    [Fact]
    public void A_representative_under_its_organisations_own_statement_speaks_as_the_organisation() =>
        CommentRules.Voice("statement", representsTargetOrganisation: true, null, null, isMpAccount: false).ShouldBe(("organisation", (string?)null));

    [Fact]
    public void A_representative_under_any_other_text_is_a_person() =>
        CommentRules.Voice("reason", representsTargetOrganisation: true, null, null, isMpAccount: false).ShouldBe(("person", (string?)null));

    [Fact]
    public void The_mps_account_or_office_under_that_mps_own_response_speaks_as_the_mp()
    {
        CommentRules.Voice("response", false, targetMp: 1001, speaksForMp: 1001, isMpAccount: true).ShouldBe(("mp", (string?)null));
        CommentRules.Voice("response", false, targetMp: 1001, speaksForMp: 1001, isMpAccount: false).ShouldBe(("mp", (string?)null), "an office member");
    }

    [Fact]
    public void The_mps_own_account_comments_nowhere_else_and_an_office_member_elsewhere_is_a_person()
    {
        CommentRules.Voice("response", false, targetMp: 1002, speaksForMp: 1001, isMpAccount: true).ShouldBe(((string?)null, "mp.takes-no-part"));
        CommentRules.Voice("reason", false, null, speaksForMp: 1001, isMpAccount: true).ShouldBe(((string?)null, "mp.takes-no-part"));
        CommentRules.Voice("response", false, targetMp: 1002, speaksForMp: 1001, isMpAccount: false).ShouldBe(("person", (string?)null));
    }

    [Fact]
    public void Speaking_as_the_mp_needs_a_passkey_session_and_a_mandate_not_ended()
    {
        CommentRules.CheckMpVoice(passkeySession: true, sitting: true).ShouldBeNull();
        CommentRules.CheckMpVoice(passkeySession: false, sitting: true).ShouldBe("mp.passkey-required");
        CommentRules.CheckMpVoice(passkeySession: true, sitting: false).ShouldBe("response.mandate-ended");
    }

    [Fact]
    public void The_ceiling_per_person_per_text_is_off_at_zero_and_counts_only_shown_comments()
    {
        CommentRules.CheckCeiling(shownByPerson: 50, limit: 0).ShouldBeNull();
        CommentRules.CheckCeiling(shownByPerson: 2, limit: 3).ShouldBeNull();
        CommentRules.CheckCeiling(shownByPerson: 3, limit: 3).ShouldBe("comment.limit");
    }

    [Theory]
    [InlineData("person", true, false, false, false, true)]
    [InlineData("person", false, true, true, true, false)]
    [InlineData("organisation", false, true, false, false, true)]
    [InlineData("organisation", true, false, false, false, false)]
    [InlineData("mp", false, false, true, true, true)]
    [InlineData("mp", false, false, true, false, false)]
    public void Who_may_delete_a_comment_follows_its_voice(string voice, bool isAuthor, bool represents, bool speaksForTheMp, bool passkey, bool expected) =>
        CommentRules.MayDelete(voice, isAuthor, represents, speaksForTheMp, passkey).ShouldBe(expected);

    [Fact]
    public void Sutinku_is_for_any_member_but_the_mps_account_never_on_ones_own_comment_and_only_while_the_bill_is_open()
    {
        CommentRules.CheckAgreement(wroteIt: false, isMpAccount: false, billOpen: true).ShouldBeNull();
        CommentRules.CheckAgreement(wroteIt: true, isMpAccount: false, billOpen: true).ShouldBe("agreement.own-comment");
        CommentRules.CheckAgreement(wroteIt: false, isMpAccount: true, billOpen: true).ShouldBe("mp.takes-no-part");
        CommentRules.CheckAgreement(wroteIt: false, isMpAccount: false, billOpen: false).ShouldBe("comment.bill-not-open");
    }

    [Theory]
    [InlineData("published", true, false, true)]
    [InlineData("hidden", true, false, false)]
    [InlineData("published", false, false, false)]
    [InlineData("published", true, true, false)]
    public void A_comment_is_shown_while_it_is_published_its_target_shown_and_the_record_not_hidden(string status, bool targetShown, bool recordHidden, bool expected) =>
        CommentRules.IsShown(status, targetShown, recordHidden).ShouldBe(expected);

    [Fact]
    public void A_moderator_hides_and_keeps_a_shown_comment_and_restores_a_hidden_one()
    {
        (CommentRules.MayHide("published"), CommentRules.MayKeep("published"), CommentRules.MayRestore("published")).ShouldBe((true, true, false));
        (CommentRules.MayHide("hidden"), CommentRules.MayKeep("hidden"), CommentRules.MayRestore("hidden")).ShouldBe((false, false, true));
    }

    [Fact]
    public void A_thread_shows_the_first_ten_and_ten_more_at_a_time()
    {
        CommentPaging.Shown(null, 25).ShouldBe(10);
        CommentPaging.Shown(20, 25).ShouldBe(20);
        CommentPaging.Shown(3, 25).ShouldBe(10, "never fewer than the first ten");
        CommentPaging.Shown(null, 4).ShouldBe(4);
        CommentPaging.More(10, 25).ShouldBe(20);
        CommentPaging.More(20, 25).ShouldBe(25);
        CommentPaging.More(25, 25).ShouldBeNull();
    }

    [Fact]
    public void The_pages_address_asks_for_more_of_one_target_and_lands_on_the_first_new_comment()
    {
        var address = CommentPaging.Address("/seimas/XVP-0412", Target, 20, "komentaras-x");
        address.ShouldBe($"/seimas/XVP-0412?komentarai={Target:N}-20#komentaras-x");
        CommentPaging.Address("/seimas/XVP-0412?atsakymai=20", Target, 20, "a").ShouldStartWith("/seimas/XVP-0412?atsakymai=20&komentarai=");
        CommentPaging.Asked($"{Target:N}-20", Target).ShouldBe(20);
        CommentPaging.Asked($"{Target:N}-20", Guid.NewGuid()).ShouldBeNull("another target's comments");
        CommentPaging.Asked("bet-kas", Target).ShouldBeNull();
    }

    [Fact]
    public void An_https_address_becomes_a_link_and_anything_else_stays_text()
    {
        var parts = CommentLinks.Split("Žr. https://pavyzdys.test/a, http://kitas.pavyzdys.test ir lrs.lt bei javascript:alert(1).");
        parts.Where(p => p.Href is not null).Select(p => p.Href).ShouldBe(["https://pavyzdys.test/a"], "the comma after it is not part of it");
        string.Concat(parts.Select(p => p.Text)).ShouldBe("Žr. https://pavyzdys.test/a, http://kitas.pavyzdys.test ir lrs.lt bei javascript:alert(1).");
        CommentLinks.Split("https://vartotojas:slaptas@pavyzdys.test/").ShouldAllBe(p => p.Href == null, "no user name in a link");
    }

    [Fact]
    public void A_long_address_is_shortened_after_its_host_never_before_it()
    {
        var address = "https://labai-ilgas-pavyzdinis-adresas.pavyzdys.test/" + new string('k', 100);
        var shown = CommentLinks.Split(address).Single().Shown!;
        shown.ShouldStartWith("https://labai-ilgas-pavyzdinis-adresas.pavyzdys.test/");
        shown.ShouldEndWith("…");
        shown.Length.ShouldBeLessThanOrEqualTo(CommentLinks.ShownMaxLength);
        CommentLinks.Rel.ShouldBe("nofollow ugc noopener noreferrer");
    }

    [Fact]
    public void The_comments_notice_reads_since_the_last_run_that_told_someone_and_never_more_than_three_days_back()
    {
        var now = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        FollowRules.CommentsSince(null, now).ShouldBe(now.AddHours(-1), "the first run reads the last hour");
        FollowRules.CommentsSince(now.AddMinutes(-15), now).ShouldBe(now.AddMinutes(-15));
        FollowRules.CommentsSince(now.AddDays(-10), now).ShouldBe(now.AddDays(-3));
        FollowRules.CommentKey(Target, now).Length.ShouldBeLessThanOrEqualTo(40, "notify.bill_event.key");
    }

    [Fact]
    public void The_notice_names_each_argument_with_its_bill_and_link_and_never_a_comment()
    {
        var template = new FollowTemplates(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = "https://lt2.test" }));
        var email = template.Comments(new CommentNoticeEmail(
            "ruta.mockute@pavyzdys.test",
            [new CommentedArgumentLine("XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", $"/seimas/XVP-0412#reason-{Target}")],
            "/sveiki#sekami"));
        email.Subject.ShouldBe("Lietuva 2.0: nauji komentarai po jūsų argumentu");
        email.TextBody.ShouldContain($"XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa: https://lt2.test/seimas/XVP-0412#reason-{Target}");
        email.TextBody.ShouldContain("https://lt2.test/sveiki#sekami");
    }

    [Fact]
    public void A_hidden_comments_author_is_told_with_the_statement_and_the_moderators_address()
    {
        var template = new ModerationNoticeTemplate(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = "https://lt2.test" }));
        var email = template.Render(new ModerationNoticeEmail(
            "ruta.mockute@pavyzdys.test", ModerationNotices.CommentHidden, "XVP-0412", "Pataisa", "Įžeidžiantis tonas.", "Mano komentaras", "/seimas/XVP-0412", "moderatoriai@pavyzdys.test"));
        email.Subject.ShouldBe("Lietuva 2.0: jūsų komentaras paslėptas");
        email.TextBody.ShouldContain("Komentaras: „Mano komentaras“");
        email.TextBody.ShouldContain("Įžeidžiantis tonas.");
        email.TextBody.ShouldContain("moderatoriai@pavyzdys.test");
    }
}
