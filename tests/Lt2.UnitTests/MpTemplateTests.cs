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

    [Fact]
    public void The_takeover_link_is_a_full_address_with_its_token_after_the_hash()
    {
        var email = new MpTemplates(Texts(), Options.Create(new SiteOptions { Address = "https://lt2.test" }))
            .Render(new MpTakeoverLinkEmail(Official, "Ona Jonaitienė", "/seimo-narys/perimti#abcDEF_123-xyz", 24));

        email.To.ShouldBe(Official);
        email.Subject.ShouldBe("Lietuva 2.0: Seimo nario profilio perėmimas");
        email.TextBody.ShouldContain("https://lt2.test/seimo-narys/perimti#abcDEF_123-xyz");
        email.TextBody.ShouldContain("Seimo narys: Ona Jonaitienė.", Case.Sensitive, "design 8.5.1: the name in the nominative, after a colon");
        email.TextBody.ShouldContain("galioja 24 val.");
        email.TextBody.ShouldNotContain("?");
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
