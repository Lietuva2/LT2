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
