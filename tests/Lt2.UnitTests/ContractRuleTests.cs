using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

public sealed class ContractRuleTests
{
    private static readonly IStringLocalizer<ValidationMessages> L = new StringLocalizer<ValidationMessages>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    [Fact]
    public void Registration_needs_only_a_valid_address_and_the_invitation_code() =>
        new RegisterRequestValidator(L)
            .Validate(new RegisterRequest { Email = "ruta@pavyzdys.test", InvitationCode = "BANDYMAS-2026" })
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Completing_a_registration_needs_the_eighteen_or_older_declaration()
    {
        var result = new CompleteRegistrationRequestValidator(L).Validate(new CompleteRegistrationRequest { DisplayName = "Rūta M." });
        result.IsValid.ShouldBeFalse();
        result.Errors.Single().ErrorMessage.ShouldBe("Registruotis gali tik 18 metų ir vyresni asmenys.");
    }

    [Fact]
    public void Completing_a_registration_with_the_declaration_and_no_name_passes() =>
        new CompleteRegistrationRequestValidator(L)
            .Validate(new CompleteRegistrationRequest { ConfirmsAdult = true })
            .IsValid.ShouldBeTrue();

    [Fact]
    public void A_display_name_is_at_most_sixty_characters() =>
        new CompleteRegistrationRequestValidator(L)
            .Validate(new CompleteRegistrationRequest { ConfirmsAdult = true, DisplayName = new string('a', 61) })
            .Errors.Single().ErrorMessage.ShouldBe("Vardas per ilgas: ne daugiau kaip 60 ženklų.");

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("")]
    public void A_sign_in_code_must_be_six_digits(string code) =>
        new SignInRequestValidator(L).Validate(new SignInRequest { Email = "ruta@pavyzdys.test", Code = code }).IsValid.ShouldBeFalse();

    [Fact]
    public void Settings_reject_values_of_the_wrong_type_or_range()
    {
        SettingKeys.RegistrationRequiresInvitation.IsValid(JsonSerializer.SerializeToElement(false)).ShouldBeTrue();
        SettingKeys.RegistrationRequiresInvitation.IsValid(JsonSerializer.SerializeToElement("no")).ShouldBeFalse();
        SettingKeys.DigestMaxPerWeek.IsValid(JsonSerializer.SerializeToElement(2)).ShouldBeTrue();
        SettingKeys.DigestMaxPerWeek.IsValid(JsonSerializer.SerializeToElement(0)).ShouldBeFalse();
        SettingKeys.SurgeMinimumShare.IsValid(JsonSerializer.SerializeToElement(1.5)).ShouldBeFalse();
    }

    [Fact]
    public void Setting_defaults_follow_the_design()
    {
        SettingKeys.RegistrationRequiresInvitation.Default.ShouldBeTrue();
        SettingKeys.DigestThresholdDefault.Default.ShouldBe(100);
        SettingKeys.DigestMaxPerWeek.Default.ShouldBe(2);
        SettingKeys.SurgeMinimumShare.Default.ShouldBe(0.30m);
        SettingKeys.SurgeMinimumAccounts.Default.ShouldBe(50);
        SettingKeys.SurgeWindowHours.Default.ShouldBe(72);
    }
}
