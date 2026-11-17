using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Auth;

public static class AuthRules
{
    public const int EmailMaxLength = 254;
    public const int InvitationCodeMaxLength = 64;
    public const int DisplayNameMaxLength = 60;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(l["Email.Required"])
            .MaximumLength(EmailMaxLength).WithMessage(l["Email.TooLong"])
            .EmailAddress().WithMessage(l["Email.Invalid"]);
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
        RuleFor(x => x.InvitationCode).MaximumLength(AuthRules.InvitationCodeMaxLength).WithMessage(l["Invitation.TooLong"]);
        RuleFor(x => x.OfficeInvitation).MaximumLength(AuthRules.InvitationCodeMaxLength).WithMessage(l["Invitation.TooLong"]);
    }
}

public sealed class RequestCodeRequestValidator : AbstractValidator<RequestCodeRequest>
{
    public RequestCodeRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
    }
}

public sealed class SignInRequestValidator : AbstractValidator<SignInRequest>
{
    public SignInRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(l["Code.Required"])
            .Matches("^[0-9]{6}$").WithMessage(l["Code.Format"]);
    }
}

public sealed class PasskeyCredentialRequestValidator : AbstractValidator<PasskeyCredentialRequest>
{
    public PasskeyCredentialRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Credential).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(l["Passkey.Required"])
            .MaximumLength(16_384).WithMessage(l["Passkey.Required"]);
        RuleFor(x => x.Name).MaximumLength(100).WithMessage(l["Passkey.NameTooLong"]);
    }
}
