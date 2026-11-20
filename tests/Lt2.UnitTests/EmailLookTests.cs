using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Notifications;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Rhythm;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// How an email looks (design 8.19.3, 8.19.5, 8.24): one light shell for members' and MPs' emails, links as their labels, the
/// takeover as a button, nothing loaded and nothing tracked. Rendering the samples also writes them to
/// <c>output/email-samples/</c> (git-ignored) so they can be opened in a browser or a screenshot taken.
/// </summary>
public sealed class EmailLookTests
{
    private const string Site = "https://lietuva2.test";
    private const string Token = "kQ3vXz9LmN2pRtYb7WcHdJf5GsAe8UoIx1VnBqZk4MyTlPa6CwDrEgSh0FjOiKu";
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static readonly IStringLocalizer<EmailTemplates> L =
        new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static readonly IOptions<SiteOptions> SiteOptions = Options.Create(new SiteOptions { Address = Site });

    private static string Samples
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir?.FullName ?? Path.GetTempPath(), "output", "email-samples");
        }
    }

    private static MpMailTemplates Mp() => new(L, SiteOptions);

    private static EmailMessage Introduction() => Mp().Introduction(new MpIntroductionEmail(
        "ona.jonaitiene@lrs.test",
        1,
        "Lietuva 2.0: Jūsų rinkėjų nuomonė vienoje vietoje",
        "Gerbiama Seimo nare,\n\nLietuva 2.0 yra savanorių sukurta platforma, kurioje gyventojai pasako, ko prašo iš Seimo narių ir kodėl.\n\nKiekvieną savaitę atsiųsime suvestinę: kiek žmonių prašo balsuoti už ir prieš kiekvieną projektą ir kokie argumentai dažniausi.\n\nBūsime dėkingi už Jūsų atsiliepimą.",
        "Donatas Šimelis",
        "VšĮ Lietuva 2.0 direktorius",
        MpMailPaths.Preferences(Token),
        MpMailPaths.OneClick(Token),
        Test: false,
        null));

    private static MpDigestBill Bill(string number, string title, MpStance? response, params DigestReason[] reasons) => Bill(number, title, response, 6150, 3900, reasons);

    private static MpDigestBill Bill(string number, string title, MpStance? response, int forOlder, int againstOlder, params DigestReason[] reasons) => new(
        new DigestItemInfo(number, title, "adoption", Monday.AddDays(3), 6800, forOlder, 4280, againstOlder, null, null, reasons, ForReasonCount: reasons.Any(r => r.Side == "for") ? 14 : 0, AgainstReasonCount: reasons.Any(r => r.Side == "against") ? 9 : 0),
        new DeliveryParty(number, "Reformų partija", 410, 520),
        response);

    private static EmailMessage Digest()
    {
        var response = new MpStance(99000006, "XVP-0413", "against", new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var longReason = string.Concat(Enumerable.Repeat("Pajamų mokestis turi būti teisingas visiems dirbantiems ir nepriklausyti nuo to, kur žmogus gyvena. ", 5)).Trim();
        return Mp().Digest(new MpDigestEmail(
            "ona.jonaitiene@lrs.test",
            "official",
            "Ona Jonaitienė",
            "weekly",
            Monday,
            [
                Bill("XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", null, new DigestReason("for", "Gynyba finansuojama nekeliant PVM.", 1310), new DigestReason("for", longReason, 880), new DigestReason("against", "Skatina keisti rezidavimo vietą.", 970)),
                Bill("XVP-0413", "Akcizų įstatymo pataisa", response, 0, 0, new DigestReason("for", "Mažina žalą sveikatai.", 540), new DigestReason("for", "Dažniau tikrinami pardavėjai.", 120)),
            ],
            MpMailPaths.PublicCopy(Monday),
            MpMailPaths.Preferences(Token),
            MpMailPaths.OneClick(Token),
            null,
            new DeliveryRef(Guid.NewGuid(), "ona-jonaitiene", "official"),
            RunId: Guid.Parse("0193a1b2-0000-7000-8000-0000000000aa")));
    }

    private static EmailMessage Code() =>
        new SignInCodeTemplate(L, SiteOptions).Render(new SignInCodeEmail("ruta.mockute@pavyzdys.test", "482915", 10));

    private static EmailMessage Gathered()
    {
        var follow = new FollowTemplates(L, SiteOptions);
        var planned = new FollowPlannedEmail("ruta.mockute@pavyzdys.test", "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "adoption", new DateOnly(2026, 11, 19), "/seimas/XVP-0412", AccountScreens.EmailRhythm, Token);
        var other = planned with { BillNumber = "XVP-0413", BillTitle = "Akcizų įstatymo pataisa", Date = new DateOnly(2026, 11, 20), BillPath = "/seimas/XVP-0413" };
        INoticePart[] parts = [new NoticePart<FollowPlannedEmail>(MessageKinds.FollowPlannedEmail, 10, follow.PlannedPart)];
        GatheredNotice Item(FollowPlannedEmail e) => new(MessageKinds.FollowPlannedEmail, JsonSerializer.SerializeToElement(e, JsonSerializerOptions.Web));
        return new GatheredTemplates(L, SiteOptions, parts).Render(new GatheredNoticesEmail(planned.To, "daily", [Item(planned), Item(other)], AccountScreens.EmailRhythm, Token));
    }

    private static IEnumerable<(string Name, EmailMessage Email)> Every()
    {
        yield return ("introduction", Introduction());
        yield return ("summary", Digest());
        yield return ("code", Code());
        yield return ("gathered", Gathered());
    }

    [Fact]
    public void The_samples_are_written_for_a_look_at_them()
    {
        Directory.CreateDirectory(Samples);
        foreach (var (name, email) in Every())
        {
            File.WriteAllText(Path.Combine(Samples, name + ".html"), email.HtmlBody!);
            File.WriteAllText(Path.Combine(Samples, name + ".txt"), email.TextBody);
        }

        File.Exists(Path.Combine(Samples, "introduction.html")).ShouldBeTrue();
    }

    [Fact]
    public void Every_email_is_one_light_shell_with_a_header_a_column_and_a_footer_and_nothing_to_load()
    {
        foreach (var (name, email) in Every())
        {
            var html = email.HtmlBody!;
            html.ShouldContain("max-width:36rem", Case.Sensitive, name);
            html.ShouldContain("name=\"viewport\"", Case.Sensitive, name);
            html.ShouldContain(">Lietuva 2.0</div>", Case.Sensitive, name + ": the wordmark is text");
            html.ShouldContain("border-top:1px solid #cbd5e1", Case.Sensitive, name + ": the footer's rule");
            html.ShouldNotContain("<img", Case.Insensitive, name);
            html.ShouldNotContain("url(", Case.Insensitive, name);
            html.ShouldNotContain("<link", Case.Insensitive, name);
            html.ShouldNotContain("<script", Case.Insensitive, name);
            html.ShouldNotContain("position:", Case.Insensitive, name);
            html.ShouldNotContain("display:flex", Case.Insensitive, name);
            html.ShouldNotContain("display:grid", Case.Insensitive, name);
            html.ShouldNotContain("utm_", Case.Insensitive, name);
        }
    }

    [Fact]
    public void Nothing_has_a_fixed_width_wider_than_a_phone_and_no_long_address_is_printed()
    {
        foreach (var (name, email) in Every())
        {
            var html = email.HtmlBody!;
            foreach (Match m in Regex.Matches(html, @"(?<![-\w])(?:width|min-width)\s*[:=]\s*""?(\d+)(?:px)?", RegexOptions.IgnoreCase))
            {
                if (m.Value.Contains("100", StringComparison.Ordinal) && !m.Value.Contains("px", StringComparison.Ordinal))
                {
                    continue;
                }

                int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).ShouldBeLessThanOrEqualTo(390, name + ": " + m.Value);
            }

            // Outside an attribute (href is one), no address of over 40 characters is printed as text.
            var printed = Regex.Replace(html, "<[^>]*>", " ");
            Regex.Matches(printed, @"https?://\S+").Where(a => a.Value.Length > 40).ShouldBeEmpty(name + ": a bare address is printed");
        }
    }

    [Fact]
    public void An_mps_links_are_their_labels_and_the_plain_part_keeps_every_address()
    {
        var preferences = Site + "/suvestines/nustatymai/" + Token;
        foreach (var email in new[] { Introduction(), Digest() })
        {
            email.HtmlBody!.ShouldContain($"<a href=\"{preferences}\" style=\"color:#475569;text-decoration:underline\">Keisti suvestinių dažnumą arba jų atsisakyti</a>");
            email.TextBody.ShouldContain("Keisti suvestinių dažnumą arba jų atsisakyti: " + preferences);
            email.TextBody.ShouldContain("galite perimti savo Seimo nario profilį ir atsakyti visiems iš karto: " + Site + "/prisijungti");
            email.HtmlBody!.ShouldNotContain(">" + preferences, Case.Sensitive);
            email.Headers![MpMailTemplates.ListUnsubscribe].ShouldBe("<" + Site + "/api/v1/mp-mail/" + Token + "/stop>");
            email.Headers[MpMailTemplates.ListUnsubscribePost].ShouldBe("List-Unsubscribe=One-Click");
        }
    }

    [Fact]
    public void The_takeover_is_a_button_to_the_sign_in_page_after_a_short_lead_below_a_rule()
    {
        var html = Introduction().HtmlBody!;

        html.ShouldContain("<a href=\"" + Site + "/prisijungti\" style=\"display:block;padding:12px 20px;");
        html.ShouldContain(">Perimti profilį ir atsakyti visiems iš karto</a>");
        html.ShouldContain("color:#ffffff;text-decoration:none", Case.Sensitive, "white on the green");
        html.ShouldContain("bgcolor=\"#1f6b3a\"", Case.Sensitive);
        html.ShouldContain("Prisijungę prie Lietuva 2.0 šiuo adresu, galite perimti savo Seimo nario profilį ir atsakyti visiems iš karto.");
        html.IndexOf("border-top:1px solid #cbd5e1\"><p", StringComparison.Ordinal).ShouldBeGreaterThan(html.IndexOf("VšĮ Lietuva 2.0 direktorius", StringComparison.Ordinal), "below the signature");
    }

    [Fact]
    public void The_signature_is_the_name_in_semibold_and_the_role_muted_below()
    {
        var html = Introduction().HtmlBody!;

        html.ShouldContain("<p style=\"margin:32px 0 0;font-weight:600\">Donatas Šimelis</p><p style=\"margin:2px 0 24px;color:#475569\">VšĮ Lietuva 2.0 direktorius</p>");
        Introduction().TextBody.ShouldContain("Donatas Šimelis\nVšĮ Lietuva 2.0 direktorius\n\nKeisti suvestinių dažnumą");
    }

    [Fact]
    public void What_the_system_adds_comes_below_the_signature_and_the_letter_has_no_second_sign_off()
    {
        var intro = Introduction();
        intro.TextBody.ShouldNotContain("\n\nLietuva 2.0\n\n", Case.Sensitive, "the signer signs the letter");
        intro.TextBody.IndexOf("VšĮ Lietuva 2.0 direktorius", StringComparison.Ordinal).ShouldBeLessThan(intro.TextBody.IndexOf("Keisti suvestinių", StringComparison.Ordinal));

        var summary = Digest().TextBody;
        var signOff = summary.IndexOf("\n\nLietuva 2.0\n\nKeisti suvestinių", StringComparison.Ordinal);
        signOff.ShouldBeGreaterThan(summary.IndexOf("Ta pati suvestinė viešai", StringComparison.Ordinal));
        var html = Digest().HtmlBody!;
        html.IndexOf(">Ši suvestinė viešai<", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("font-weight:600\">Lietuva 2.0</p>", StringComparison.Ordinal));
        html.IndexOf("font-weight:600\">Lietuva 2.0</p>", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Perimti profilį ir atsakyti visiems iš karto</a>", StringComparison.Ordinal));
        Introduction().HtmlBody!.ShouldNotContain("font-weight:600\">Lietuva 2.0</p>", Case.Sensitive);
    }

    [Fact]
    public void A_bill_in_a_summary_is_a_card_with_a_linked_heading_the_counts_a_bar_and_the_arguments_per_side()
    {
        var html = Digest().HtmlBody!;

        html.ShouldContain($"<span style=\"color:#64748b;font-weight:600\">XVP-0412</span> <a href=\"{Site}/seimas/XVP-0412\" style=\"color:#1e293b\">Gyventojų pajamų mokesčio įstatymo pataisa</a>");
        html.ShouldContain(">Prašo balsuoti už</div><div style=\"font-size:30px;line-height:1.2;font-weight:700;color:#1b6034\">6 800</div>");
        html.ShouldContain("color:#85322f\">4 280</div>");
        html.ShouldContain("iš jų 0 – paskyros senesnės nei 30 d.");
        html.ShouldContain("width=\"61%\"", Case.Sensitive, "the proportion bar");
        html.ShouldContain("Gynyba finansuojama nekeliant PVM. <span style=\"color:#64748b;white-space:nowrap\">(1 310)</span>");
        html.ShouldContain(">Skaityti visą</a>", Case.Sensitive, "a cut argument links to the whole one by its label");
        html.ShouldContain(">Argumentų prieš dar nėra.</div>", Case.Sensitive, "a side without arguments says so under its heading");
        html.ShouldContain(">Atsakyti visiems</a>", Case.Sensitive, "one button to answer, not two links");
        html.ShouldNotContain("Visi skaičiai ir argumentai");
        html.ShouldNotContain("Atsakyti vieną kartą visiems");
        html.ShouldContain($"<a href=\"{Site}/seimas/XVP-0413\" style=\"font-weight:600;color:#1f6b3a\">Keisti arba papildyti</a>", Case.Sensitive, "the MP's own response with its link");
        html.ShouldContain($">Ši suvestinė viešai</a>", Case.Sensitive);
    }

    [Fact]
    public void The_text_part_shows_each_bills_address_once_and_every_address_in_full()
    {
        var text = Digest().TextBody;

        text.Split("https://lietuva2.test/seimas/XVP-0412").Length.ShouldBe(2, "answer once, no second line to the same page");
        text.Split("https://lietuva2.test/seimas/XVP-0413").Length.ShouldBe(2, "the response's change link, no second line");
        text.ShouldNotContain("Visi skaičiai ir argumentai");
        text.ShouldContain("Atsakyti vieną kartą visiems: https://lietuva2.test/seimas/XVP-0412");
    }

    [Fact]
    public void A_line_of_a_gathered_part_leads_on_by_its_label_on_its_own_indented_line_not_by_a_bare_address()
    {
        var email = Gathered();

        email.TextBody.ShouldContain("Planuojama 2026-11-19 (priėmimas).\n  Projektas: https://lietuva2.test/seimas/XVP-0412");
        email.HtmlBody!.ShouldContain("&nbsp;&nbsp;<a href=\"https://lietuva2.test/seimas/XVP-0412\" style=\"font-weight:600;color:#1f6b3a\">Projektas</a>");
        email.HtmlBody!.ShouldNotContain(">https://lietuva2.test/seimas/XVP-0412<", Case.Sensitive);
    }

    [Fact]
    public void The_code_stays_large_and_every_member_email_keeps_its_stop_less_footer()
    {
        var html = Code().HtmlBody!;

        html.ShouldContain("font-size:28px;line-height:1.3;font-weight:700;letter-spacing:4px\">482915</p>");
        Gathered().HtmlBody!.ShouldContain("atsisakyti laiškų</a>");
    }
}
