using Lt2.Identity.Domain;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>Design 6.2 (personal codes decided 2026-11-13): code format, look-up, who gets through, counts and levels.</summary>
public sealed class InvitationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static bool Usable(string kind, int usesLeft = 0, int usedCount = 0, DateTimeOffset? expires = null, DateTimeOffset? off = null, DateTimeOffset? replaced = null, int limit = 0) =>
        InvitationRules.Usable(kind, usesLeft, usedCount, expires, off, replaced, limit, Now);

    [Fact]
    public void A_personal_code_is_eight_characters_without_look_alikes_in_two_groups_of_four()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = InvitationRules.GeneratePersonal();
            code.Length.ShouldBe(9);
            code[4].ShouldBe('-');
            code.Replace("-", "", StringComparison.Ordinal).ShouldAllBe(c => InvitationRules.Alphabet.Contains(c));
        }

        InvitationRules.Alphabet.ShouldNotContain('0');
        InvitationRules.Alphabet.ShouldNotContain('O');
        InvitationRules.Alphabet.ShouldNotContain('1');
        InvitationRules.Alphabet.ShouldNotContain('I');
        InvitationRules.Alphabet.ShouldNotContain('L');
        InvitationRules.Alphabet.ShouldNotContain('U');
    }

    [Fact]
    public void Case_dashes_and_spaces_do_not_matter_when_a_code_is_typed() =>
        InvitationRules.Lookup(" abcd efgh ").ShouldBe(InvitationRules.Lookup("ABCD-EFGH"));

    [Fact]
    public void A_personal_code_has_no_limit_when_the_setting_is_0_and_the_set_limit_otherwise()
    {
        Usable(InvitationKinds.Personal, usedCount: 10_000, limit: 0).ShouldBeTrue();
        SettingKeys.RegistrationPersonalCodeUses.Default.ShouldBe(0);
        Usable(InvitationKinds.Personal, usedCount: 2, limit: 3).ShouldBeTrue();
        Usable(InvitationKinds.Personal, usedCount: 3, limit: 3).ShouldBeFalse();
    }

    [Fact]
    public void A_team_code_keeps_its_own_uses_whatever_the_personal_setting()
    {
        Usable(InvitationKinds.Team, usesLeft: 1, limit: 0).ShouldBeTrue();
        Usable(InvitationKinds.Team, usesLeft: 0, limit: 0).ShouldBeFalse();
    }

    [Fact]
    public void A_switched_off_replaced_or_expired_code_lets_nobody_through()
    {
        Usable(InvitationKinds.Personal, off: Now.AddDays(-1)).ShouldBeFalse();
        Usable(InvitationKinds.Personal, replaced: Now.AddDays(-1)).ShouldBeFalse();
        Usable(InvitationKinds.Team, usesLeft: 5, expires: Now).ShouldBeFalse();
        Usable(InvitationKinds.Team, usesLeft: 5, expires: Now.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void A_member_sees_everyone_who_registered_with_any_of_their_codes_replaced_ones_included() =>
        InvitationRules.MemberCount([4, 0, 3]).ShouldBe(7);

    [Fact]
    public void Team_codes_are_level_1_and_a_personal_code_brings_people_one_level_below_its_member()
    {
        InvitationRules.MemberLevel(null).ShouldBe(1, "registered without a code");
        InvitationRules.PersonalCodeLevel(InvitationRules.MemberLevel(1)).ShouldBe(2, "a member who came by a team code");
        InvitationRules.PersonalCodeLevel(InvitationRules.MemberLevel(2)).ShouldBe(3);
    }
}
