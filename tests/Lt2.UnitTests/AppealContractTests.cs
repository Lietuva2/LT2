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
        Lt2.Api.Contracts.MemberTextLimits.Ceiling.ShouldBe(SettingKeys.MemberTextCeiling);
        Lt2.Api.Contracts.MemberTextLimits.Ceiling.ShouldBe(MemberTextLength.Ceiling);
        foreach (var setting in new[] { 0, 1, 2000, 20_000, 50_000 })
        {
            Lt2.Api.Contracts.MemberTextLimits.Allowed(setting).ShouldBe(MemberTextLength.Allowed(setting));
            Lt2.Web.Client.MemberTextLength.Allowed(setting).ShouldBe(MemberTextLength.Allowed(setting), "the browser's copy is the same file");
        }

        AppealLimits.DescriptionMaxLength.ShouldBe(AppealRules.DescriptionMaxLength);
        Sides.All.ShouldBe(["for", "against"], "the wire format; the browser's and the server's copy are the same file");
    }

    [Fact]
    public void The_consent_text_the_client_shows_is_the_version_in_force() =>
        PositionConsentText.Version.ShouldBe(PositionConsent.TextVersion);

    /// <summary>
    /// Modules do not reference the contracts (they run in the browser), so the few rules both state are pinned here: the
    /// form and the module that stores the data refuse the same things (design 6.8).
    /// </summary>
    [Fact]
    public void The_account_and_feedback_forms_and_the_identity_module_use_the_same_limits()
    {
        Lt2.Api.Contracts.Auth.AuthRules.DisplayNameMaxLength.ShouldBe(Lt2.Identity.Domain.ProfileRules.DisplayNameMaxLength);
        Lt2.Api.Contracts.Feedback.FeedbackLimits.TextMaxLength.ShouldBe(Lt2.Identity.Domain.FeedbackRules.TextMaxLength);
        Lt2.Api.Contracts.Feedback.FeedbackLimits.Kinds.ShouldBe(Lt2.Identity.Domain.FeedbackRules.Kinds);
        Lt2.Api.Contracts.Feedback.FeedbackLimits.Statuses.ShouldBe(Lt2.Identity.Domain.FeedbackRules.Statuses);
        AppealLimits.StatementMaxLength.ShouldBe(StaffStatements.MaxLength);
    }

    /// <summary>
    /// Owner, 2026-11-22 (design 8.3): the refusal names the author only where the author receives the statement by email; a
    /// statement shown on a page or read by staff only has no author. Same field, same request, another message.
    /// </summary>
    [Fact]
    public void Returning_or_hiding_needs_a_statement_of_reasons_whose_refusal_names_the_author_only_when_it_goes_to_one()
    {
        var plain = new StatementRequestValidator(L);
        var toAuthor = new StatementToAuthorRequestValidator(L);
        var blank = new StatementRequest { Statement = " " };

        plain.Validate(blank).Errors.Single().ErrorMessage.ShouldBe("Parašykite paaiškinimą.");
        toAuthor.Validate(blank).Errors.Single().ErrorMessage.ShouldBe("Parašykite paaiškinimą autoriui.");
        plain.Validate(blank).Errors.Single().PropertyName.ShouldBe(toAuthor.Validate(blank).Errors.Single().PropertyName);
        new MergeReasonRequestValidator(L).Validate(new MergeReasonRequest { IntoReasonId = Guid.NewGuid(), Statement = "" })
            .Errors.Single().ErrorMessage.ShouldBe("Parašykite paaiškinimą autoriui.");
        new CloseRecordRequestValidator(L)
            .Validate(new CloseRecordRequest { Reason = "rejected", DecidedOn = new DateOnly(2026, 11, 2), SourceUrl = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas", Statement = "" })
            .Errors.Single().ErrorMessage.ShouldBe("Parašykite paaiškinimą.");
        plain.Validate(new StatementRequest { Statement = "Aprašymas nėra neutralus." }).IsValid.ShouldBeTrue();
        toAuthor.Validate(new StatementRequest { Statement = "Įžeidus tonas." }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void The_browser_refuses_a_statement_or_a_response_only_over_the_technical_ceiling()
    {
        var statements = new Lt2.Api.Contracts.Organisations.OrganisationStatementRequestValidator(L);
        statements.Validate(new Lt2.Api.Contracts.Organisations.OrganisationStatementRequest { Stance = "for", Text = new string('a', 1501) }).IsValid.ShouldBeTrue("the setting is checked where it is written");
        statements.Validate(new Lt2.Api.Contracts.Organisations.OrganisationStatementRequest { Stance = "for", Text = new string('a', 20_001) })
            .Errors.Single().ErrorMessage.ShouldBe("Pozicija per ilga: ne daugiau kaip 20 000 ženklų.");
        var responses = new Lt2.Api.Contracts.Mps.MpResponseRequestValidator(L);
        responses.Validate(new Lt2.Api.Contracts.Mps.MpResponseRequest { Stance = "for", Text = new string('a', 1501) }).IsValid.ShouldBeTrue("the setting is checked where it is written");
        responses.Validate(new Lt2.Api.Contracts.Mps.MpResponseRequest { Stance = "for", Text = new string('a', 20_001) })
            .Errors.Single().ErrorMessage.ShouldBe("Atsakymas per ilgas: ne daugiau kaip 20 000 ženklų.");
    }

    [Fact]
    public void The_browser_refuses_only_an_argument_over_the_technical_ceiling()
    {
        var validator = new ReasonRequestValidator(L);
        validator.Validate(new ReasonRequest { Side = "for", Text = new string('a', 2001) }).IsValid.ShouldBeTrue("the setting is checked where the argument is written");
        validator.Validate(new ReasonRequest { Side = "for", Text = new string('a', Lt2.Api.Contracts.MemberTextLimits.Ceiling + 1) })
            .Errors.Single().ErrorMessage.ShouldBe("Argumentas per ilgas: ne daugiau kaip 20 000 ženklų.");
    }
}
