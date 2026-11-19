using Lt2.Notifications;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 8.19.3 to 8.19.5, appendix A: the summary an MP reads, its copy, the introduction letter and the notices.</summary>
public sealed class MpMailTemplateTests
{
    private const string Official = "pavyzdys.laima.uosyte@lrs.lt";
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abcde";
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static MpMailTemplates Templates() => new(
        new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
        Options.Create(new SiteOptions { Address = "https://lt2.test" }));

    private static MpDigestBill Bill(string number, MpStance? response = null) => new(
        new DigestItemInfo(
            number, "Gyventojų pajamų mokesčio įstatymo pataisa", "adoption", Monday.AddDays(3), 6800, 6150, 4280, 3900, null, null,
            [new DigestReason("for", "Gynyba finansuojama nekeliant PVM.", 1310), new DigestReason("against", "Skatina keisti rezidavimo vietą.", 970)],
            ForReasonCount: 14, AgainstReasonCount: 9),
        new DeliveryParty(number, "Reformų partija", 410, 520),
        response);

    private static MpDigestEmail Email(string recipient = "official", MpStance? response = null) => new(
        recipient == "official" ? Official : "padejeja@pavyzdys.test",
        recipient,
        "Laima Uosytė",
        "weekly",
        Monday,
        [Bill("XVP-0412", response)],
        MpMailPaths.PublicCopy(Monday),
        recipient == "official" ? MpMailPaths.Preferences(Token) : null,
        recipient == "official" ? MpMailPaths.OneClick(Token) : null,
        recipient == "official" ? null : MpMailPaths.StopCopies(Token),
        new DeliveryRef(Guid.NewGuid(), "laima-uosyte", recipient));

    [Fact]
    public void The_summary_has_both_sides_with_the_30_day_counts_and_the_arguments_without_authors()
    {
        var email = Templates().Digest(Email());

        email.To.ShouldBe(Official);
        email.Subject.ShouldBe("Lietuva 2.0 suvestinė Seimo nariams, 2026-11-16");
        email.TextBody.ShouldContain("XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa");
        email.TextBody.ShouldContain("Priėmimas · planuojama lapkričio 19 d.");
        email.TextBody.ShouldContain("Prašo balsuoti už: 6 800 (iš jų 6 150 – paskyros senesnės nei 30 d.)");
        email.TextBody.ShouldContain("Prašo balsuoti prieš: 4 280 (iš jų 3 900 – paskyros senesnės nei 30 d.)");
        email.TextBody.ShouldContain("Iš nurodžiusių, kad palaiko Reformų partija: 410 prašo balsuoti už.");
        email.TextBody.ShouldContain("· Gynyba finansuojama nekeliant PVM. (1 310)");
        email.TextBody.ShouldContain("· Skatina keisti rezidavimo vietą. (970)");
        email.TextBody.ShouldContain("Dažniausi argumentai už (iš 14):");
        email.TextBody.ShouldContain("Dažniausi argumentai prieš (iš 9):");
        email.HtmlBody.ShouldNotBeNull();
        email.HtmlBody.ShouldContain("Prašo balsuoti prieš: 4 280");
        email.Stream.ShouldBe("mp", "MP mail has its own stream");
    }

    [Theory]
    [InlineData(0, 0, "Argumentų už dar nėra.")]
    [InlineData(1, 1, "Dažniausi argumentai už:")]
    [InlineData(2, 2, "Dažniausi argumentai už:")]
    [InlineData(3, 2, "Dažniausi argumentai už (iš 3):")]
    [InlineData(14, 2, "Dažniausi argumentai už (iš 14):")]
    [InlineData(1400, 2, "Dažniausi argumentai už (iš 1 400):")]
    public void The_heading_of_a_side_says_how_many_published_arguments_it_has_only_when_the_ones_shown_are_not_all(int published, int shown, string heading)
    {
        var plain = Bill("XVP-0412");
        var reasons = Enumerable.Range(1, shown).Select(i => new DigestReason("for", "Argumentas " + i + ".", 10 - i)).ToList();
        var bill = plain with { Item = plain.Item with { Reasons = reasons, ForReasonCount = published, AgainstReasonCount = 0 } };
        var text = Templates().Digest(Email() with { Bills = [bill] }).TextBody;

        text.ShouldContain(heading);
        if (!heading.Contains("(iš ", StringComparison.Ordinal))
        {
            text.ShouldNotContain("argumentai už (iš ");
        }
    }

    [Fact]
    public void A_snapshot_from_before_the_number_was_kept_shows_no_number()
    {
        var plain = Bill("XVP-0412");
        var bill = plain with { Item = plain.Item with { ForReasonCount = null, AgainstReasonCount = null } };
        var text = Templates().Digest(Email() with { Bills = [bill] }).TextBody;

        text.ShouldContain("Dažniausi argumentai už:");
        text.ShouldContain("Dažniausi argumentai prieš:");
        text.ShouldNotContain("(iš 1");
    }

    [Theory]
    [InlineData(null, 2, null)]
    [InlineData(0, 0, null)]
    [InlineData(1, 1, null)]
    [InlineData(2, 2, null)]
    [InlineData(3, 2, 3)]
    [InlineData(14, 2, 14)]
    public void A_sides_published_count_is_shown_only_when_the_shown_arguments_are_not_all_of_them(int? published, int shown, int? expected) =>
        DigestReasonCount.Shown(published, shown).ShouldBe(expected);

    [Fact]
    public void A_long_argument_is_shortened_like_the_page_with_a_link_to_the_whole_one_on_the_public_copy()
    {
        var run = Guid.Parse("0193a1b2-0000-7000-8000-0000000000aa");
        var longText = string.Concat(Enumerable.Repeat("Pajamų mokestis turi būti teisingas visiems dirbantiems. ", 30)).Trim();
        var plain = Bill("XVP-0412");
        var bill = plain with
        {
            Item = plain.Item with { Reasons = [new DigestReason("for", "Trumpas argumentas.", 12), new DigestReason("for", longText, 1310)] },
        };
        var model = Email() with { Bills = [bill], RunId = run };
        var text = Templates().Digest(model).TextBody;

        var shown = TextExcerpt.Of(longText, TextExcerpt.ArgumentShown, TextExcerpt.ArgumentMargin).Shown;
        text.ShouldContain("· " + shown + "… (1 310)");
        text.ShouldNotContain(longText, Case.Sensitive, "the email carries the beginning only");
        var anchor = Lt2.Api.Contracts.Digests.DigestRoutes.ReasonAnchor(run, "XVP-0412", "for", 2);
        text.ShouldContain("Visas argumentas: https://lt2.test/suvestines/2026-11-16#" + anchor);
        text.ShouldContain("· Trumpas argumentas. (12)");
        text.Split('\n').Count(l => l.Contains("Visas argumentas", StringComparison.Ordinal)).ShouldBe(1, "only a cut argument gets the link");

        // The text part is the same paragraphs.
        string.Join("\n\n", Templates().Text(model).Paragraphs).ShouldContain("Visas argumentas: https://lt2.test/suvestines/2026-11-16#" + anchor);
    }

    [Fact]
    public void An_office_members_copy_says_whose_it_is_carries_its_own_one_click_stop_and_never_the_mps_link()
    {
        var model = Email("extra") with
        {
            To = "biuras@pavyzdys.test",
            Recipient = "office",
            UnsubscribePath = MpMailPaths.OneClick(Token),
            Delivery = new DeliveryRef(Guid.NewGuid(), "laima-uosyte", "office", Guid.NewGuid()),
        };
        var email = Templates().Digest(model);
        email.Subject.ShouldBe("Lietuva 2.0 suvestinės kopija, 2026-11-16");
        email.TextBody.ShouldContain("Tai Seimo nario suvestinės kopija, siunčiama jums kaip jo biuro nariui. Seimo narys: Laima Uosytė.");
        email.TextBody.ShouldContain("pasirenkate savo paskyroje: https://lt2.test/paskyra/dalyvavimas");
        email.TextBody.ShouldContain("Nebegauti kopijų: https://lt2.test/suvestines/kopijos/" + Token);
        email.TextBody.ShouldNotContain("/suvestines/nustatymai/", Case.Sensitive, "never the MP's own link");
        email.Headers!["List-Unsubscribe"].ShouldBe("<https://lt2.test/api/v1/mp-mail/" + Token + "/stop>");
        email.Headers["List-Unsubscribe-Post"].ShouldBe("List-Unsubscribe=One-Click", "design 8.19.3: the member's one-click stop");
    }

    [Fact]
    public void The_email_links_an_argument_at_the_same_place_the_public_copy_gives_it()
    {
        var run = Guid.NewGuid();
        MpMailPaths.ReasonOnPublicCopy(Monday, run, "XIVP-1234(2)", "against", 1)
            .ShouldBe(MpMailPaths.PublicCopy(Monday) + "#" + Lt2.Api.Contracts.Digests.DigestRoutes.ReasonAnchor(run, "XIVP-1234(2)", "against", 1));
    }

    [Fact]
    public void Without_a_response_the_mp_gets_the_link_to_answer_once_for_everyone()
    {
        var text = Templates().Digest(Email()).TextBody;

        text.ShouldContain("Atsakyti vieną kartą visiems: https://lt2.test/seimas/XVP-0412");
        text.ShouldNotContain("Jūsų atsakymas");
    }

    [Fact]
    public void With_a_published_response_the_mp_reads_it_with_its_date()
    {
        var response = new MpStance(99000006, "XVP-0412", "against", new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var text = Templates().Digest(Email(response: response)).TextBody;

        text.ShouldContain("Jūsų atsakymas: balsuosiu prieš (2026-11-14). Keisti arba papildyti: https://lt2.test/seimas/XVP-0412");
        text.ShouldNotContain("Atsakyti vieną kartą visiems");
    }

    [Fact]
    public void Every_summary_ends_with_what_the_numbers_are_the_public_copy_the_preferences_and_the_take_over()
    {
        var text = Templates().Digest(Email()).TextBody;

        text.ShouldContain("Tai nėra reprezentatyvi apklausa");
        text.ShouldContain("Ta pati suvestinė viešai: https://lt2.test/suvestines/2026-11-16");
        text.ShouldContain("Keisti suvestinių dažnumą arba jų atsisakyti: https://lt2.test/suvestines/nustatymai/" + Token);
        text.ShouldContain("galite perimti savo Seimo nario profilį ir atsakyti visiems iš karto: https://lt2.test/prisijungti");
        text.IndexOf("Tai nėra reprezentatyvi apklausa", StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("XVP-0412", StringComparison.Ordinal));
    }

    [Fact]
    public void The_one_click_stop_is_in_the_headers_of_rfc_8058()
    {
        var email = Templates().Digest(Email());

        email.Headers.ShouldNotBeNull();
        email.Headers[MpMailTemplates.ListUnsubscribe].ShouldBe("<https://lt2.test/api/v1/mp-mail/" + Token + "/stop>");
        email.Headers[MpMailTemplates.ListUnsubscribePost].ShouldBe("List-Unsubscribe=One-Click");
    }

    [Fact]
    public void The_html_part_has_plain_links_to_the_sites_own_pages_and_no_tracking()
    {
        var html = Templates().Digest(Email()).HtmlBody!;

        html.ShouldContain("<a href=\"https://lt2.test/suvestines/nustatymai/" + Token + "\">");
        html.ShouldNotContain("<img", Case.Insensitive, "no tracking pixel");
        System.Text.RegularExpressions.Regex.Matches(html, "href=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
            .ShouldAllBe(href => href.StartsWith("https://lt2.test/", StringComparison.Ordinal), "links are the site's own addresses, never rewritten");
    }

    [Fact]
    public void A_copy_says_whose_summary_it_is_has_its_own_stop_link_and_never_the_mps_preferences_link()
    {
        var email = Templates().Digest(Email("extra"));

        email.Subject.ShouldBe("Lietuva 2.0 suvestinės kopija, 2026-11-16");
        email.TextBody.ShouldContain("Seimo narys: Laima Uosytė.", Case.Sensitive, "the name in the nominative after a colon");
        email.TextBody.ShouldContain("Nebegauti kopijų: https://lt2.test/suvestines/kopijos/" + Token);
        email.TextBody.ShouldNotContain("/suvestines/nustatymai/");
        email.TextBody.ShouldNotContain("perimti savo Seimo nario profilį");
        email.Headers.ShouldNotBeNull();
        email.Headers[MpMailTemplates.ListUnsubscribe].ShouldBe("<https://lt2.test/suvestines/kopijos/" + Token + ">");
        email.Headers.ContainsKey(MpMailTemplates.ListUnsubscribePost).ShouldBeFalse("the one-click form is the MP's alone");
    }

    [Fact]
    public void The_introduction_letter_is_the_persons_text_and_signature_followed_by_what_every_mp_email_carries()
    {
        var email = Templates().Introduction(new MpIntroductionEmail(
            Official, 1, "Lietuva 2.0 – kas tai ir ko tikėtis", "Gerbiama Seimo nare,\n\npirma pastraipa.\n\nAntra pastraipa.", "Rūta Mockutė", "VšĮ LT2.0 vadovė",
            MpMailPaths.Preferences(Token), MpMailPaths.OneClick(Token), Test: false, null));

        email.Subject.ShouldBe("Lietuva 2.0 – kas tai ir ko tikėtis");
        email.TextBody.ShouldStartWith("Gerbiama Seimo nare,\n\npirma pastraipa.\n\nAntra pastraipa.\n\nRūta Mockutė\nVšĮ LT2.0 vadovė\n\nKeisti suvestinių dažnumą");
        email.TextBody.ShouldContain("perimti savo Seimo nario profilį");
        email.Headers![MpMailTemplates.ListUnsubscribePost].ShouldBe("List-Unsubscribe=One-Click");
    }

    [Fact]
    public void The_letters_lists_are_lists_in_the_html_part_and_stay_as_typed_in_the_plain_part()
    {
        // Design 8.2, "A text's layout" (owner, 2026-11-17).
        const string Letter = "Ką rasite:\n- projektų puslapius\n- argumentus <b>abiem</b> pusėms\nKaip tai veikia:\n1. Žmonės renkasi pusę.\n2. Suvestinę gaunate jūs.";
        var email = Templates().Introduction(new MpIntroductionEmail(
            Official, 1, "Tema", Letter, "Rūta Mockutė", "VšĮ LT2.0 vadovė", MpMailPaths.Preferences(Token), MpMailPaths.OneClick(Token), Test: false, null));

        email.TextBody.ShouldStartWith(Letter + "\n\nRūta Mockutė");
        email.HtmlBody!.ShouldContain("<p>Ką rasite:</p><ul><li>projektų puslapius</li><li>argumentus &lt;b&gt;abiem&lt;/b&gt; pusėms</li></ul>"
            + "<p>Kaip tai veikia:</p><ol><li value=\"1\">Žmonės renkasi pusę.</li><li value=\"2\">Suvestinę gaunate jūs.</li></ol>");
        email.HtmlBody!.ShouldNotContain("<b>abiem</b>", Case.Sensitive, "nothing typed becomes markup");
    }

    [Fact]
    public void A_test_copy_of_the_letter_says_so_and_carries_no_personal_link()
    {
        var email = Templates().Introduction(new MpIntroductionEmail(
            "administratore@pavyzdys.test", 1, "Tema", "Tekstas.", "Rūta Mockutė", "Vadovė", null, null, Test: true, null));

        email.Subject.ShouldBe("[Bandomoji kopija] Tema");
        email.TextBody.ShouldStartWith("[Bandomoji kopija");
        email.Headers.ShouldBeNull("no one-click stop on a test copy");
    }

    [Theory]
    [InlineData(MpMailNotices.Changed, "suvestinių nustatymai pakeisti")]
    [InlineData(MpMailNotices.NewLink, "nauja suvestinių nustatymų nuoroda")]
    [InlineData(MpMailNotices.ExtraConfirm, "patvirtinkite papildomą gavėją")]
    [InlineData(MpMailNotices.ExtraConfirmed, "papildomas gavėjas patvirtintas")]
    [InlineData(MpMailNotices.ExtraWelcome, "gausite Seimo nario suvestinių kopijas")]
    [InlineData(MpMailNotices.ExtraRemoved, "papildomas gavėjas pašalintas")]
    public void Every_change_of_the_mps_choices_is_confirmed_by_email_with_the_mps_name_in_the_nominative(string notice, string subject)
    {
        var email = Templates().Notice(new MpMailNoticeEmail(Official, notice, "Laima Uosytė", "weekly", MpMailPaths.Preferences(Token), "padejeja@pavyzdys.test", 24));

        email.Subject.ShouldBe("Lietuva 2.0: " + subject);
        email.TextBody.ShouldContain("Seimo narys: Laima Uosytė.");
        email.Stream.ShouldBe("mp");
    }

    [Fact]
    public void The_stop_is_confirmed_too_and_says_nothing_more_will_come()
    {
        var text = Templates().Notice(new MpMailNoticeEmail(Official, MpMailNotices.Changed, "Laima Uosytė", "stopped", MpMailPaths.Preferences(Token), null)).TextBody;

        text.ShouldContain("Dabar: Nesiųsti.");
        text.ShouldContain("Daugiau suvestinių nesiųsime");
        text.ShouldContain("Atsiųsti naują nuorodą");
    }
}
