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
        return new SignInCodeTemplate(new StringLocalizer<EmailTemplates>(factory));
    }

    [Fact]
    public void The_sign_in_code_email_is_in_lithuanian_and_carries_the_code()
    {
        var email = Template().Render(new SignInCodeEmail("jonas.petraitis@pavyzdys.test", "482915", 10));

        email.To.ShouldBe("jonas.petraitis@pavyzdys.test");
        email.Subject.ShouldBe("Lietuva 2.0 prisijungimo kodas: 482915");
        email.TextBody.ShouldContain("482915");
        email.TextBody.ShouldContain("Kodas galioja 10 min.");
        email.TextBody.ShouldContain("Laba diena");
        email.HtmlBody.ShouldNotBeNull().ShouldContain("lang=\"lt\"");
    }

    [Fact]
    public void The_sign_in_code_email_does_not_say_whether_the_person_registered_or_signed_in()
    {
        var text = Template().Render(new SignInCodeEmail("jonas@pavyzdys.test", "000000", 10)).TextBody;
        text.ShouldNotContain("registr", Case.Insensitive);
    }

    [Fact]
    public void Failed_messages_are_retried_with_growing_delays_up_to_an_hour()
    {
        OutboxDispatcher.Backoff(1).ShouldBe(TimeSpan.FromSeconds(10));
        OutboxDispatcher.Backoff(2).ShouldBe(TimeSpan.FromSeconds(20));
        OutboxDispatcher.Backoff(4).ShouldBe(TimeSpan.FromSeconds(80));
        OutboxDispatcher.Backoff(20).ShouldBe(TimeSpan.FromHours(1));
    }
}
