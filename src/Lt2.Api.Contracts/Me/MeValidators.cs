using FluentValidation;
using Lt2.Api.Contracts.Auth;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Me;

public sealed class CompleteRegistrationRequestValidator : AbstractValidator<CompleteRegistrationRequest>
{
    public CompleteRegistrationRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.DisplayName).MaximumLength(AuthRules.DisplayNameMaxLength).WithMessage(l["DisplayName.TooLong"]);
        RuleFor(x => x.ConfirmsAdult).Equal(true).WithMessage(l["Adult.Required"]);
        RuleFor(x => x.AcceptsTerms).Equal(true).WithMessage(l["Terms.Required"]);
        RuleFor(x => x.InvitationCode).MaximumLength(AuthRules.InvitationCodeMaxLength).WithMessage(l["Invitation.TooLong"]);
    }
}

public sealed class RoleActivationRequestValidator : AbstractValidator<RoleActivationRequest>
{
    public RoleActivationRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(l["Activation.Required"])
            .MaximumLength(40).WithMessage(l["Activation.Required"]);
    }
}


public sealed class PositionConsentRequestValidator : AbstractValidator<PositionConsentRequest>
{
    public PositionConsentRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.TextVersion).NotEmpty().WithMessage(l["Consent.Required"]).MaximumLength(50).WithMessage(l["Consent.Required"]);
}

public sealed class TermsAcceptanceRequestValidator : AbstractValidator<TermsAcceptanceRequest>
{
    public TermsAcceptanceRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.TextVersion).NotEmpty().WithMessage(l["Terms.Version"]).MaximumLength(50).WithMessage(l["Terms.Version"]);
}

public sealed class PartySupportRequestValidator : AbstractValidator<PartySupportRequest>
{
    public PartySupportRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Consent).Equal(true).When(x => x.Party is not null).WithMessage(l["Consent.Required"]);
        RuleFor(x => x.Party).MaximumLength(200);
    }
}