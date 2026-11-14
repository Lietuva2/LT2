using System.Resources;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 8.14.14: the confirmation code email and the moderators' notices to an organisation's representatives.</summary>
public sealed class OrganisationTemplateTests
{
    private static OrganisationTemplates Templates(string? site = "https://lt2.pavyzdys.test") =>
        new(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = site }));

    [Fact]
    public void The_code_email_names_the_organisation_and_the_domain_and_carries_the_code()
    {
        var email = Templates().Render(new OrganisationCodeEmail("info@mokesciai.pavyzdys.test", "123456", 10, "Pavyzdinė asociacija", "mokesciai.pavyzdys.test"));
        email.To.ShouldBe("info@mokesciai.pavyzdys.test");
        email.Subject.ShouldContain("123456");
        email.TextBody.ShouldContain("„Pavyzdinė asociacija“");
        email.TextBody.ShouldContain("mokesciai.pavyzdys.test");
        email.TextBody.ShouldContain("Patvirtinta per el. pašto adresą @mokesciai.pavyzdys.test");
        email.TextBody.ShouldContain("10 min.");
        email.HtmlBody!.ShouldNotContain("<img", Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Notices))]
    public void Every_notice_has_a_subject_and_a_body_with_the_organisation_and_a_full_link(string notice)
    {
        var bill = notice.StartsWith("statement-", StringComparison.Ordinal);
        var email = Templates().Render(new OrganisationNoticeEmail(
            "atstovas@pavyzdys.test", notice, "Pavyzdinė asociacija", bill ? "/seimas/XVP-0412" : "/o/pavyzdine-asociacija", "Paaiškinimas.", "moderatoriai@pavyzdys.test",
            bill ? "XVP-0412" : null, bill ? "Gyventojų pajamų mokesčio įstatymo pataisa" : null));
        email.Subject.ShouldContain("Pavyzdinė asociacija");
        email.TextBody.ShouldContain(bill ? "https://lt2.pavyzdys.test/seimas/XVP-0412" : "https://lt2.pavyzdys.test/o/pavyzdine-asociacija");
        email.TextBody.ShouldContain("Moderatoriaus paaiškinimas: Paaiškinimas.");
        email.TextBody.ShouldContain("moderatoriai@pavyzdys.test");
        email.TextBody.ShouldNotContain("{", Case.Sensitive, "every placeholder is filled");
        if (bill)
        {
            email.TextBody.ShouldContain("XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa");
        }
    }

    public static TheoryData<string> Notices() => new(OrganisationNotices.All);

    [Fact]
    public void Without_the_sites_address_the_link_stays_a_path()
    {
        var email = Templates(site: null).Render(new OrganisationNoticeEmail("a@pavyzdys.test", OrganisationNotices.Blocked, "Pavyzdinė asociacija", "/o/pavyzdine-asociacija", "Kodėl.", null));
        email.TextBody.ShouldContain("/o/pavyzdine-asociacija");
        email.TextBody.ShouldNotContain("Jei nesutinkate", Case.Sensitive, "no contact line without a contact address");
    }

    [Fact]
    public void An_unknown_notice_is_refused() =>
        Should.Throw<InvalidOperationException>(() => Templates().Render(new OrganisationNoticeEmail("a@pavyzdys.test", "unknown", "X", "/o/x", null, null)));

    [Fact]
    public void Every_organisation_text_on_the_pages_exists_in_lithuanian()
    {
        var resources = new ResourceManager("Lt2.Web.Client.Appeals.AppealUi", typeof(Lt2.Web.Client.Appeals.AppealUi).Assembly);
        foreach (var key in new[]
        {
            "Org.Section.Title", "Org.Label.none", "Org.Label.domain", "Org.Label.moderator", "Org.Stance.for", "Org.Stance.against", "Org.Stance.remarks",
            "Org.Goal.One", "Org.Goal.Many", "Org.Goal.OneOnly", "Org.Goal.ManyOnly", "Org.MyList.Title", "Report.Ground.impersonation", "OrgMod.Action.keep",
            "Org.Field.Lobbyist", "Admin.Setting.Organisations:PerAccount",
        })
        {
            resources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("lt-LT")).ShouldNotBeNullOrWhiteSpace(key);
        }

        resources.GetString("Org.Section.Title", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("Organizacijų pozicijos");
        resources.GetString("Report.Ground.impersonation", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("Apsimeta kita organizacija");
        resources.GetString("Org.MyList.Title", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("Mano organizacijos");
        resources.GetString("Org.Field.Lobbyist", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("Esame registruoti lobistai");
        resources.GetString("Org.Goal.Many", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("Siekiame {0} dalyvių · dabar {1}");
    }
}
