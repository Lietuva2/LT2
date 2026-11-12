using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Admin;

public sealed class UpdateSettingRequestValidator : AbstractValidator<UpdateSettingRequest>
{
    public UpdateSettingRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Value.ValueKind)
            .NotEqual(JsonValueKind.Undefined).WithMessage(l["Setting.ValueRequired"])
            .OverridePropertyName("value");
    }
}
