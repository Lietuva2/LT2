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
        RuleFor(x => x.OfficeInvitation).MaximumLength(AuthRules.InvitationCodeMaxLength).WithMessage(l["Invitation.TooLong"]);
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


public sealed class EmailChangeRequestValidator : AbstractValidator<EmailChangeRequest>
{
    public EmailChangeRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
        RuleFor(x => x.ProofCode).Matches("^[0-9]{6}$").When(x => !string.IsNullOrEmpty(x.ProofCode)).WithMessage(l["Code.Format"]);
    }
}

public sealed class EmailConfirmRequestValidator : AbstractValidator<EmailConfirmRequest>
{
    public EmailConfirmRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(l["Code.Required"])
            .Matches("^[0-9]{6}$").WithMessage(l["Code.Format"]);
}

/// <summary>Present and not too long: whether the text is a code at all is the server's one answer for every link that does not work.</summary>
public sealed class EmailRestoreRequestValidator : AbstractValidator<EmailRestoreRequest>
{
    public EmailRestoreRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Token).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(l["EmailRestore.Required"])
            .MaximumLength(100).WithMessage(l["EmailRestore.Required"]);
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
