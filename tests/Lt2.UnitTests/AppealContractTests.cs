using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Appeals.Domain;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>The browser checks the same limits as the server, and asks consent to the text version the server accepts.</summary>
public sealed class AppealContractTests
{
    private static readonly IStringLocalizer<Lt2.Api.Contracts.ValidationMessages> L = new StringLocalizer<Lt2.Api.Contracts.ValidationMessages>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    [Fact]
    public void The_client_and_the_server_use_the_same_limits()
    {
        AppealLimits.ReasonMaxLength.ShouldBe(AppealRules.ReasonMaxLength);
        AppealLimits.ReasonsPerPerson.ShouldBe(AppealRules.ReasonsPerPersonPerBill);
        AppealLimits.EndorsementsPerPerson.ShouldBe(AppealRules.EndorsementsPerPersonPerBill);
        AppealLimits.DescriptionMaxLength.ShouldBe(AppealRules.DescriptionMaxLength);
        AppealLimits.Sides.ShouldBe(Sides.All);
    }

    [Fact]
    public void The_consent_text_the_client_shows_is_the_version_in_force() =>
        PositionConsentText.Version.ShouldBe(PositionConsent.TextVersion);

    [Fact]
    public void Returning_or_hiding_needs_a_statement_of_reasons()
    {
        var validator = new StatementRequestValidator(L);
        validator.Validate(new StatementRequest { Statement = " " }).Errors.Single().ErrorMessage.ShouldBe("Parašykite paaiškinimą autoriui.");
        validator.Validate(new StatementRequest { Statement = "Aprašymas nėra neutralus." }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void An_argument_over_500_characters_is_refused_in_the_browser_too() =>
        new ReasonRequestValidator(L).Validate(new ReasonRequest { Side = "for", Text = new string('a', 501) })
            .Errors.Single().ErrorMessage.ShouldBe("Argumentas per ilgas: ne daugiau kaip 500 ženklų.");
}
