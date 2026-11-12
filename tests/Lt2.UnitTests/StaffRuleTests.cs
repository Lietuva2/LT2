using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 6.2: staff roles work only with the staff passkey, activated by a one-time code.</summary>
public sealed class StaffRuleTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static (RoleActivation Activation, string Code) Issue()
    {
        var id = Guid.CreateVersion7(IssuedAt);
        var code = StaffRules.GenerateActivationCode();
        return (new RoleActivation
        {
            Id = id,
            UserId = Guid.CreateVersion7(IssuedAt),
            Role = "administrator",
            CodeHash = StaffRules.HashActivationCode(id, code),
            CreatedAt = IssuedAt,
            ExpiresAt = IssuedAt + StaffRules.ActivationLifetime,
        }, code);
    }

    [Fact]
    public void An_activation_code_is_sixteen_characters_in_four_groups_without_confusable_letters()
    {
        for (var i = 0; i < 500; i++)
        {
            StaffRules.GenerateActivationCode().ShouldMatch("^[0-9ABCDEFGHJKMNPQRSTVWXYZ]{4}(-[0-9ABCDEFGHJKMNPQRSTVWXYZ]{4}){3}$");
        }
    }

    [Fact]
    public void Activation_codes_are_not_predictable_from_one_another() =>
        Enumerable.Range(0, 500).Select(_ => StaffRules.GenerateActivationCode()).Distinct().Count().ShouldBe(500);

    [Fact]
    public void An_activation_code_is_valid_for_twenty_four_hours()
    {
        var (activation, code) = Issue();
        StaffRules.ActivationLifetime.ShouldBe(TimeSpan.FromHours(24));
        StaffRules.Check(activation, code, IssuedAt.AddHours(23).AddMinutes(59)).ShouldBe(ActivationCheck.Valid);
        StaffRules.Check(activation, code, IssuedAt.AddHours(24)).ShouldBe(ActivationCheck.Expired);
    }

    [Fact]
    public void An_activation_code_works_only_once()
    {
        var (activation, code) = Issue();
        activation.UsedAt = IssuedAt.AddMinutes(5);
        StaffRules.Check(activation, code, IssuedAt.AddMinutes(6)).ShouldBe(ActivationCheck.AlreadyUsed);
    }

    [Fact]
    public void A_wrong_activation_code_is_a_mismatch() =>
        StaffRules.Check(Issue().Activation, "AAAA-BBBB-CCCC-DDDD", IssuedAt).ShouldBe(ActivationCheck.Mismatch);

    [Fact]
    public void An_activation_code_may_be_typed_in_lower_case_with_spaces_or_with_o_for_zero_and_l_for_one()
    {
        var (activation, code) = Issue();
        var typed = " " + code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal).Replace('0', 'o').Replace('1', 'l') + " ";
        StaffRules.Check(activation, typed, IssuedAt).ShouldBe(ActivationCheck.Valid);
    }

    [Fact]
    public void The_stored_hash_is_salted_with_the_activation_id()
    {
        var first = StaffRules.HashActivationCode(Guid.CreateVersion7(), "7KQ2-M9XD-4TRB-HC0P");
        var second = StaffRules.HashActivationCode(Guid.CreateVersion7(), "7KQ2-M9XD-4TRB-HC0P");
        first.ShouldNotBe(second);
        first.Length.ShouldBe(32);
    }

    [Fact]
    public void A_passkey_confirmation_is_fresh_for_ten_minutes()
    {
        StaffRules.FreshConfirmation.ShouldBe(TimeSpan.FromMinutes(10));
        StaffRules.IsFresh(IssuedAt, IssuedAt.AddMinutes(9).AddSeconds(59)).ShouldBeTrue();
        StaffRules.IsFresh(IssuedAt, IssuedAt.AddMinutes(10)).ShouldBeFalse();
    }

    [Fact]
    public void A_session_never_confirmed_with_a_passkey_is_not_fresh() =>
        StaffRules.IsFresh(null, IssuedAt).ShouldBeFalse();
}
