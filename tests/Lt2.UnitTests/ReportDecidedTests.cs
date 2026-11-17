using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Owner, 2026-11-17 (Digital Services Act art. 16(5)): the member who reported is told the decision: which text, what became of
/// it, the moderator's explanation when there is one, the link while it stands, the moderation rules; never the moderator.
/// </summary>
public sealed class ReportDecidedTests
{
    private static readonly ReportDecidedTemplate Template = new(
        new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
        Options.Create(new SiteOptions { Address = "https://lt2.test" }));

    [Theory]
    [InlineData("keep", ReportOutcomes.Kept)]
    [InlineData("publish", ReportOutcomes.Kept)]
    [InlineData("restore", ReportOutcomes.Kept)]
    [InlineData("hide", ReportOutcomes.Hidden)]
    [InlineData("block", ReportOutcomes.Blocked)]
    [InlineData("unconfirm", ReportOutcomes.Unconfirmed)]
    [InlineData("merge", ReportOutcomes.Merged)]
    [InlineData("describe", ReportOutcomes.Corrected)]
    [InlineData("undescribe", ReportOutcomes.Removed)]
    public void A_moderators_action_means_an_outcome_for_the_reporters(string action, string outcome) =>
        ReportOutcomes.Of(action).ShouldBe(outcome);

    [Fact]
    public void A_kept_argument_is_named_by_its_bill_with_the_note_the_link_and_the_rules()
    {
        var email = Template.Render(new ReportDecidedEmail(
            "ruta.mockute@pavyzdys.test", "reason", ReportOutcomes.Kept, "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", null,
            "/seimas/XVP-0412?argumentas=1#reason-1", "Kritika – ne įžeidimas."));

        email.Subject.ShouldBe("Jūsų pranešimas išnagrinėtas");
        email.TextBody.ShouldContain("išnagrinėjome jūsų pranešimą apie argumentą projekte XVP-0412 „Gyventojų pajamų mokesčio įstatymo pataisa“. Jį palikome, nes neradome taisyklių pažeidimo.");
        email.TextBody.ShouldContain("Moderatoriaus paaiškinimas: Kritika – ne įžeidimas.");
        email.TextBody.ShouldContain("https://lt2.test/seimas/XVP-0412?argumentas=1#reason-1");
        email.TextBody.ShouldContain("https://lt2.test/moderavimo-taisykles");
        email.TextBody.ShouldContain("/privatumas", Case.Sensitive, "the footer");
    }

    [Fact]
    public void A_hidden_statement_agrees_in_gender_and_has_no_link()
    {
        var email = Template.Render(new ReportDecidedEmail(
            "ruta.mockute@pavyzdys.test", "statement", ReportOutcomes.Hidden, "XVP-0412", "Pataisa", null, null, "Reklama."));
        email.TextBody.ShouldContain("apie organizacijos poziciją projekte XVP-0412 „Pataisa“. Ją paslėpėme.");
        email.TextBody.ShouldNotContain("Kur tai skelbiama");
    }
}
