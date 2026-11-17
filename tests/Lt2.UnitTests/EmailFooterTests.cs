using Lt2.Notifications;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 8.22.3: every email ends with where the privacy notice is, a plain link in the HTML part.</summary>
public sealed class EmailFooterTests
{
    private static readonly IStringLocalizer<EmailTemplates> Texts = new StringLocalizer<EmailTemplates>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private const string Footer = "Kaip tvarkome duomenis, rašoma privatumo pranešime: https://lt2.test/privatumas";

    [Fact]
    public void An_email_ends_with_the_privacy_notice_in_text_and_as_a_plain_link_in_html()
    {
        var email = new SignInCodeTemplate(Texts, Options.Create(new SiteOptions { Address = "https://lt2.test" }))
            .Render(new SignInCodeEmail("ruta.mockute@pavyzdys.test", "482915", 10));

        email.TextBody.TrimEnd().ShouldEndWith(Footer);
        email.HtmlBody.ShouldNotBeNull().ShouldContain(
            "font-size:13px;color:#64748b\"><a href=\"https://lt2.test/privatumas\">Kaip tvarkome duomenis, rašoma privatumo pranešime</a></p>");
        email.HtmlBody!.IndexOf("privatumas", StringComparison.Ordinal).ShouldBeGreaterThan(email.HtmlBody.IndexOf("482915", StringComparison.Ordinal), "at the end");

        // Only the emails of the member's rhythm carry "Keisti periodiškumą arba atsisakyti laiškų" (design 8.24.1).
        email.TextBody.ShouldNotContain("periodiškumą", Case.Insensitive);
        email.TextBody.ShouldNotContain("Atsisakyti", Case.Insensitive);
        email.Headers.ShouldBeNull();
    }

    [Fact]
    public void An_email_to_an_mp_ends_with_it_too() =>
        EmailLayout.BuildMp("ona.jonaitiene@lrs.test", "Tema", ["Laba diena,", "Lietuva 2.0"], EmailLayout.Footer(Texts, "https://lt2.test"), null)
            .TextBody.TrimEnd().ShouldEndWith(Footer);

    [Fact]
    public void Without_the_sites_address_the_footer_names_the_path() =>
        EmailLayout.Footer(Texts, null).ShouldBe("Kaip tvarkome duomenis, rašoma privatumo pranešime: /privatumas");
}
