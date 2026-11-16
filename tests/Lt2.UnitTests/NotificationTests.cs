using Lt2.Notifications.Outbox;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

public sealed class NotificationTests
{
    private static SignInCodeTemplate Template()
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new SignInCodeTemplate(new StringLocalizer<EmailTemplates>(factory), Options.Create(new SiteOptions()));
    }

    [Fact]
    public void The_sign_in_code_email_is_in_lithuanian_and_carries_the_code()
    {
        var email = Template().Render(new SignInCodeEmail("ruta.mockute@pavyzdys.test", "482915", 10));

        email.To.ShouldBe("ruta.mockute@pavyzdys.test");
        email.Subject.ShouldBe("Lietuva 2.0 prisijungimo kodas: 482915");
        email.TextBody.ShouldContain("482915");
        email.TextBody.ShouldContain("Kodas galioja 10 min.");
        email.TextBody.ShouldContain("Laba diena");
        email.HtmlBody.ShouldNotBeNull().ShouldContain("lang=\"lt\"");
    }

    [Fact]
    public void The_sign_in_code_email_does_not_say_whether_the_person_registered_or_signed_in()
    {
        var text = Template().Render(new SignInCodeEmail("ruta@pavyzdys.test", "000000", 10)).TextBody;
        text.ShouldNotContain("registr", Case.Insensitive);
    }

    [Fact]
    public void The_sign_in_code_email_says_only_the_newest_code_works() =>
        Template().Render(new SignInCodeEmail("ruta@pavyzdys.test", "000000", 10)).TextBody
            .ShouldContain("tinka tik naujausias kodas");

    private static AccountNoticeTemplate NoticeTemplate()
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new AccountNoticeTemplate(new StringLocalizer<EmailTemplates>(factory), Options.Create(new SiteOptions()));
    }

    [Theory]
    [InlineData(AccountNotices.PasskeyAdded, null, "pridėtas naujas prieigos raktas")]
    [InlineData(AccountNotices.RoleGranted, "moderator", "suteiktas moderatoriaus vaidmuo")]
    [InlineData(AccountNotices.RoleActivated, "administrator", "administratoriaus vaidmuo aktyvintas")]
    [InlineData(AccountNotices.AccountReset, null, "iš naujo nustatė jūsų paskyrą")]
    public void Every_account_change_has_a_lithuanian_notice_that_says_what_to_do_if_it_was_not_you(string notice, string? role, string expected)
    {
        var email = NoticeTemplate().Render(new AccountNoticeEmail("ruta.mockute@pavyzdys.test", notice, role));

        email.To.ShouldBe("ruta.mockute@pavyzdys.test");
        email.Subject.ShouldStartWith("Lietuva 2.0: ");
        email.TextBody.ShouldContain(expected);
        email.TextBody.ShouldContain("Jei to nedarėte jūs");
        email.HtmlBody.ShouldNotBeNull().ShouldContain("lang=\"lt\"");
    }

    [Fact]
    public void The_role_notice_says_the_activation_code_is_never_emailed() =>
        NoticeTemplate().Render(new AccountNoticeEmail("ruta@pavyzdys.test", AccountNotices.RoleGranted, "administrator")).TextBody
            .ShouldContain("Šio kodo el. paštu nesiunčiame");

    [Fact]
    public void The_activation_notice_counts_the_passkeys_removed() =>
        NoticeTemplate().Render(new AccountNoticeEmail("ruta@pavyzdys.test", AccountNotices.RoleActivated, "administrator", PasskeysRemoved: 2)).TextBody
            .ShouldContain("Pašalinta prieigos raktų: 2.");

    [Fact]
    public void Failed_messages_are_retried_with_growing_delays_up_to_an_hour()
    {
        OutboxDispatcher.Backoff(1).ShouldBe(TimeSpan.FromSeconds(10));
        OutboxDispatcher.Backoff(2).ShouldBe(TimeSpan.FromSeconds(20));
        OutboxDispatcher.Backoff(4).ShouldBe(TimeSpan.FromSeconds(80));
        OutboxDispatcher.Backoff(20).ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_failed_send_is_described_by_the_error_type_and_status_code_never_the_servers_text()
    {
        var rejected = new MailKit.Net.Smtp.SmtpCommandException(
            MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable, "5.1.1 <ruta@pavyzdys.test>: Recipient address rejected");

        OutboxDispatcher.Describe(rejected).ShouldBe("SmtpCommandException 550");
        OutboxDispatcher.Describe(new IOException("ruta@pavyzdys.test")).ShouldBe("IOException");
    }
}
