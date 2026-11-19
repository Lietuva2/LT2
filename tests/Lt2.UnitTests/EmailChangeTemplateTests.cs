using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 6.2, "Changing the email address": the code to the new address, the email to an address that already has an account (no
/// code, no link), the email to the old address (when, the new address in part, the link back with its code after the <c>#</c>
/// and the code to type without it), the email when the old address was put back, and the code to the address in use.
/// </summary>
public sealed class EmailChangeTemplateTests
{
    private static StringLocalizer<EmailTemplates> Texts() =>
        new(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static EmailChangeTemplates Templates() => new(Texts(), Options.Create(new SiteOptions { Address = "https://lt2.test" }));

    [Fact]
    public void The_code_to_the_new_address_is_shown_large_with_its_validity()
    {
        var email = Templates().Code(new EmailChangeCodeEmail("ruta.nauja@pavyzdys.test", "123456", 10));

        email.To.ShouldBe("ruta.nauja@pavyzdys.test");
        email.Subject.ShouldEndWith(": 123456");
        email.TextBody.ShouldContain("\n\n123456\n\n");
        email.TextBody.ShouldContain("10 min.");
        email.TextBody.ShouldContain("https://lt2.test/privatumas", customMessage: "design 8.22.3: every email has the footer");
        email.HtmlBody!.ShouldContain("letter-spacing:4px\">123456</p>");
    }

    [Fact]
    public void An_address_that_has_an_account_is_told_nothing_changed_without_a_code_or_a_link()
    {
        var email = Templates().Taken(new EmailChangeTakenEmail("ona.jonaitiene@pavyzdys.test"));

        email.To.ShouldBe("ona.jonaitiene@pavyzdys.test");
        email.TextBody.ShouldContain("niekas nepakeista");
        email.TextBody.ShouldNotMatch(@"\b\d{6}\b");
        email.TextBody.ShouldNotContain("#");
        email.HtmlBody!.ShouldNotContain("<img", customMessage: "no tracking pixel");
    }

    [Fact]
    public void The_old_address_is_told_when_and_to_which_address_in_part_with_the_link_back()
    {
        var email = Templates().Changed(new EmailChangedEmail(
            "ruta.mockute@pavyzdys.test",
            "r***@pavyzdys.test",
            new DateTimeOffset(2026, 11, 19, 10, 30, 0, TimeSpan.Zero),
            "/paskyra/el-pastas/grazinti#ABCD-EFGH-JKMN",
            "/paskyra/el-pastas/grazinti",
            "ABCD-EFGH-JKMN",
            new DateTimeOffset(2026, 11, 26, 10, 30, 0, TimeSpan.Zero)));

        email.To.ShouldBe("ruta.mockute@pavyzdys.test");
        email.TextBody.ShouldContain("2026-11-19 12:30", customMessage: "when, Vilnius time");
        email.TextBody.ShouldContain("r***@pavyzdys.test");
        email.TextBody.ShouldContain("Ne jūs? Grąžinti ankstesnį adresą: https://lt2.test/paskyra/el-pastas/grazinti#ABCD-EFGH-JKMN");
        email.TextBody.ShouldContain("atverkite https://lt2.test/paskyra/el-pastas/grazinti ir įveskite šį kodą:\n\nABCD-EFGH-JKMN\n\n");
        email.TextBody.ShouldContain("2026-11-26 12:30", customMessage: "until when the link works");
        email.TextBody.ShouldNotContain("grazinti?", customMessage: "design 6.8: no secret in a query");
        email.HtmlBody!.ShouldContain("Ne jūs? <a href=\"https://lt2.test/paskyra/el-pastas/grazinti#ABCD-EFGH-JKMN\" style=\"font-weight:600;color:#1f6b3a\">Grąžinti ankstesnį adresą</a></p>");
        email.HtmlBody!.ShouldContain("letter-spacing:4px\">ABCD-EFGH-JKMN</p>");
    }

    [Fact]
    public void The_address_the_account_had_moved_to_is_told_the_old_one_was_put_back()
    {
        var email = Templates().Restored(new EmailRestoredEmail("ruta.nauja@pavyzdys.test", new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero)));

        email.To.ShouldBe("ruta.nauja@pavyzdys.test");
        email.TextBody.ShouldContain("2026-11-20 10:00");
        email.TextBody.ShouldContain("grąžintas ankstesnis el. pašto adresas");
        email.TextBody.ShouldContain("sesijos baigtos");
    }

    [Fact]
    public void A_code_to_the_address_in_use_before_a_change_says_what_it_is_for()
    {
        var site = Options.Create(new SiteOptions { Address = "https://lt2.test" });
        var proof = new SignInCodeTemplate(Texts(), site).Render(new SignInCodeEmail("ruta@pavyzdys.test", "654321", 10, SignInCodePurposes.EmailChange));
        var signIn = new SignInCodeTemplate(Texts(), site).Render(new SignInCodeEmail("ruta@pavyzdys.test", "654321", 10));

        proof.Subject.ShouldNotBe(signIn.Subject);
        proof.Subject.ShouldEndWith(": 654321");
        proof.TextBody.ShouldContain("el. pašto adresą");
        proof.TextBody.ShouldContain("\n\n654321\n\n");
        signIn.Subject.ShouldStartWith("Lietuva 2.0 prisijungimo kodas");
    }
}
