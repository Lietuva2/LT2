using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 8.5.2, 8.5.3: the take-over link and the notices to an MP's official address.</summary>
public sealed class MpTemplateTests
{
    private const string Official = "pavyzdys.ona.jonaitiene@lrs.lt";

    private static StringLocalizer<EmailTemplates> Texts() =>
        new(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static AccountNoticeTemplate Notices() => new(Texts(), Options.Create(new SiteOptions { Address = "https://lt2.test" }));

    private static MpTemplates Mp() => new(Texts(), Options.Create(new SiteOptions { Address = "https://lt2.test" }));

    [Fact]
    public void The_takeover_link_is_a_full_address_with_its_code_after_the_hash_and_the_code_to_type_without_it()
    {
        var email = Mp().Render(new MpTakeoverLinkEmail(Official, "Ona Jonaitienė", "/seimo-narys/perimti#ABCD-EFGH-JKMN", 24, "/seimo-narys/perimti", "ABCD-EFGH-JKMN"));

        email.To.ShouldBe(Official);
        email.Subject.ShouldBe("Lietuva 2.0: Seimo nario profilio perėmimas");
        email.TextBody.ShouldContain("Perimti profilį: https://lt2.test/seimo-narys/perimti#ABCD-EFGH-JKMN");
        email.TextBody.ShouldContain("Seimo narys: Ona Jonaitienė.", Case.Sensitive, "design 8.5.1: the name in the nominative, after a colon");
        email.TextBody.ShouldContain("galioja 24 val.");
        email.TextBody.ShouldNotContain("perimti?", customMessage: "design 6.8: no secret in a query");

        // Owner, 2026-11-17: the way without the link: the page to open and the code to type there.
        email.TextBody.ShouldContain("Naršyklėje patys atverkite https://lt2.test/seimo-narys/perimti ir įveskite šį kodą:\n\nABCD-EFGH-JKMN\n\n");
        email.HtmlBody!.ShouldContain("<a href=\"https://lt2.test/seimo-narys/perimti#ABCD-EFGH-JKMN\" style=\"font-weight:600;color:#1f6b3a\">Perimti profilį</a></p>", customMessage: "a bold green link, the address not printed under it (owner, 2026-11-17)");
        email.HtmlBody!.ShouldContain("letter-spacing:4px\">ABCD-EFGH-JKMN</p>", customMessage: "the code shown large");
    }

    [Fact]
    public void An_office_invitation_names_the_mp_links_the_page_and_gives_the_code_to_type()
    {
        var email = Mp().OfficeInvitation(new MpOfficeInvitationEmail(
            "biuras@pavyzdys.test", "Ona Jonaitienė", "/seimo-narys/biuras#ABCD-EFGH-JKMN", "/seimo-narys/biuras", "ABCD-EFGH-JKMN",
            new DateTimeOffset(2026, 11, 24, 10, 30, 0, TimeSpan.Zero)));

        email.To.ShouldBe("biuras@pavyzdys.test");
        email.Subject.ShouldBe("Kvietimas į Seimo nario biurą Lietuva 2.0");
        email.TextBody.ShouldContain("Seimo narys: Ona Jonaitienė.");
        email.TextBody.ShouldContain("Priimti kvietimą: https://lt2.test/seimo-narys/biuras#ABCD-EFGH-JKMN");
        email.TextBody.ShouldContain("atverkite https://lt2.test/seimo-narys/biuras ir įveskite šį kodą:\n\nABCD-EFGH-JKMN\n\n");
        email.TextBody.ShouldContain("galioja iki 2026-11-24 12:30", customMessage: "Vilnius time");
        email.TextBody.ShouldContain("kito kvietimo nereikia", customMessage: "the office invitation counts as the invitation");
        email.TextBody.ShouldContain("https://lt2.test/privatumas", customMessage: "design 8.22.3: every email has the footer");
        email.HtmlBody!.ShouldContain("<a href=\"https://lt2.test/seimo-narys/biuras#ABCD-EFGH-JKMN\" style=\"font-weight:600;color:#1f6b3a\">Priimti kvietimą</a></p>");
        email.HtmlBody!.ShouldContain("<a href=\"https://lt2.test/seimo-narys/biuras\" style=\"color:#1f6b3a;overflow-wrap:anywhere;word-break:break-word\">https://lt2.test/seimo-narys/biuras</a>");
        email.HtmlBody!.ShouldNotContain("<img", customMessage: "no tracking pixel");
    }

    [Theory]
    [InlineData(AccountNotices.MpLinked, "dabar tvarko Seimo nario profilį")]
    [InlineData(AccountNotices.MpGivenUp, "nebetvarko Seimo nario profilio")]
    [InlineData(AccountNotices.MpLinkEnded, "administratorius nutraukė")]
    [InlineData(AccountNotices.MpOfficeAdded, "prisijungė naujas narys")]
    [InlineData(AccountNotices.MpOfficeRemoved, "pašalintas narys")]
    [InlineData(AccountNotices.MpOfficeLeft, "išėjo iš biuro")]
    public void Every_change_to_an_mps_profile_is_told_to_the_official_address_without_naming_anyone(string notice, string expected)
    {
        var email = Notices().Render(new AccountNoticeEmail(Official, notice, MpName: "Ona Jonaitienė"));

        email.To.ShouldBe(Official);
        email.Subject.ShouldStartWith("Lietuva 2.0: ");
        email.TextBody.ShouldContain(expected);
        email.TextBody.ShouldContain("Seimo narys: Ona Jonaitienė.", Case.Sensitive, "design 8.5.1: the name in the nominative, after a colon");
        email.TextBody.ShouldContain("Jei to nedarėte jūs");
    }

    [Theory]
    [InlineData(MpResponseNotices.Hidden, "Lietuva 2.0: Seimo nario atsakymas paslėptas", "paslėpė Seimo nario atsakymą")]
    [InlineData(MpResponseNotices.Restored, "Lietuva 2.0: Seimo nario atsakymas atkurtas", "atkūrė Seimo nario atsakymą")]
    public void A_moderators_action_on_a_response_is_told_with_the_bill_and_the_statement(string notice, string subject, string expected)
    {
        var email = new MpTemplates(Texts(), Options.Create(new SiteOptions { Address = "https://lt2.test" }))
            .Response(new MpResponseNoticeEmail(Official, notice, "Ona Jonaitienė", "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "/seimas/XVP-0412#atsakymai", "Asmens duomenys.", "moderatoriai@pavyzdys.test"));

        email.To.ShouldBe(Official);
        email.Subject.ShouldBe(subject);
        email.TextBody.ShouldContain(expected);
        email.TextBody.ShouldContain("Seimo narys: Ona Jonaitienė.");
        email.TextBody.ShouldContain("XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa");
        email.TextBody.ShouldContain("https://lt2.test/seimas/XVP-0412#atsakymai");
        email.TextBody.ShouldContain("Moderatoriaus paaiškinimas: Asmens duomenys.");
        email.TextBody.ShouldContain("moderatoriai@pavyzdys.test");
    }

    [Fact]
    public void An_administrators_end_of_a_link_carries_the_statement() =>
        Notices().Render(new AccountNoticeEmail(Official, AccountNotices.MpLinkEnded, MpName: "Ona Jonaitienė", Statement: "Susieta per klaidą."))
            .TextBody.ShouldContain("Administratoriaus paaiškinimas: Susieta per klaidą.");

    [Fact]
    public void A_takeover_counts_the_passkeys_removed() =>
        Notices().Render(new AccountNoticeEmail(Official, AccountNotices.MpLinked, PasskeysRemoved: 2, MpName: "Ona Jonaitienė"))
            .TextBody.ShouldContain("Pašalinta prieigos raktų: 2.");
}
