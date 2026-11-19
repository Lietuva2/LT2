using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 6.2, "Changing the email address": the new address must differ from the one in use once normalised; the old address is
/// told the new one in part; a stolen session is not enough to move an account.
/// </summary>
public sealed class EmailChangeRuleTests
{
    [Theory]
    [InlineData("ruta.mockute@pavyzdys.test", "Ruta.Mockute@Pavyzdys.TEST")]
    [InlineData("ruta.mockute@pavyzdys.test", "ruta.mockute+lt2@pavyzdys.test")]
    [InlineData("rutamockute@gmail.com", "Ruta.Mockute@googlemail.com")]
    [InlineData(" ruta@pavyzdys.test ", "ruta@pavyzdys.test")]
    public void An_address_is_the_one_in_use_when_it_is_the_same_once_normalised(string inUse, string typed) =>
        EmailAddress.Same(inUse, typed).ShouldBeTrue();

    [Theory]
    [InlineData("ruta.mockute@pavyzdys.test", "ruta_mockute@pavyzdys.test")]
    [InlineData("ruta.mockute@pavyzdys.test", "rutamockute@pavyzdys.test")]
    [InlineData("ruta@pavyzdys.test", "ruta@kitas.test")]
    [InlineData("ruta@pavyzdys.test", "ne adresas")]
    [InlineData(null, null)]
    public void Another_address_or_no_address_is_not_the_one_in_use(string? inUse, string? typed) =>
        EmailAddress.Same(inUse, typed).ShouldBeFalse();

    [Theory]
    [InlineData("ruta.mockute@pavyzdys.test", "r***@pavyzdys.test")]
    [InlineData("Ruta@Pavyzdys.TEST", "R***@pavyzdys.test")]
    [InlineData(" o@pavyzdys.test ", "o***@pavyzdys.test")]
    [InlineData("ruta+lt2@sub.pavyzdys.test", "r***@sub.pavyzdys.test")]
    public void An_address_in_part_is_its_first_character_three_stars_and_the_domain(string address, string shown) =>
        EmailAddress.Mask(address).ShouldBe(shown);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ruta")]
    [InlineData("@pavyzdys.test")]
    [InlineData("ruta@")]
    public void A_text_that_is_no_address_is_shown_as_three_stars(string? address) =>
        EmailAddress.Mask(address).ShouldBe("***");

    [Fact]
    public void An_address_in_part_never_holds_more_of_the_name_than_its_first_character() =>
        EmailAddress.Mask("jonas.petraitis@pavyzdys.test").ShouldNotContain("onas");

    [Fact]
    public void A_passkey_session_proves_it_without_a_code() =>
        EmailChangeRules.Proof(staffAccount: false, staffSession: false, fresh: false, mpAccount: false, passkeySession: true, codeGiven: false)
            .ShouldBe(EmailChangeProof.Passkey);

    [Fact]
    public void A_code_session_proves_it_only_with_the_code_sent_to_the_address_in_use()
    {
        EmailChangeRules.Proof(false, false, false, false, passkeySession: false, codeGiven: false).ShouldBe(EmailChangeProof.ProofRequired);
        EmailChangeRules.Proof(false, false, false, false, passkeySession: false, codeGiven: true).ShouldBe(EmailChangeProof.Code);
    }

    [Fact]
    public void A_staff_account_needs_its_staff_passkey_session_freshly_confirmed_and_no_code_proves_it()
    {
        EmailChangeRules.Proof(staffAccount: true, staffSession: false, fresh: false, false, passkeySession: false, codeGiven: true)
            .ShouldBe(EmailChangeProof.StaffPasskeyRequired);
        EmailChangeRules.Proof(staffAccount: true, staffSession: false, fresh: false, false, passkeySession: true, codeGiven: false)
            .ShouldBe(EmailChangeProof.StaffPasskeyRequired, "another passkey of the account is not the staff passkey");
        EmailChangeRules.Proof(staffAccount: true, staffSession: true, fresh: false, false, passkeySession: true, codeGiven: true)
            .ShouldBe(EmailChangeProof.ConfirmationRequired);
        EmailChangeRules.Proof(staffAccount: true, staffSession: true, fresh: true, false, passkeySession: true, codeGiven: false)
            .ShouldBe(EmailChangeProof.Passkey);
    }

    [Fact]
    public void The_mps_own_account_needs_a_passkey_session_and_no_code_proves_it()
    {
        EmailChangeRules.Proof(false, false, false, mpAccount: true, passkeySession: false, codeGiven: true).ShouldBe(EmailChangeProof.PasskeyRequired);
        EmailChangeRules.Proof(false, false, false, mpAccount: true, passkeySession: true, codeGiven: false).ShouldBe(EmailChangeProof.Passkey);
    }

    [Fact]
    public void The_link_back_to_the_old_address_is_good_for_seven_days() =>
        EmailChangeRules.RestoreLifetime.ShouldBe(TimeSpan.FromDays(7));
}
