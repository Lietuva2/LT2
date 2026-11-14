using Lt2.Identity.Domain;
using Lt2.Notifications.Follows;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.14.14, stage B: an organisation's invitation code (works only while its organisation is active, its own uses
/// setting, nothing of it kept on the new account but the level) and the follow emails about its statements.
/// </summary>
public sealed class OrganisationCodeRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static bool Usable(int usedCount = 0, int limit = 0, string? organisationOff = null, DateTimeOffset? off = null, DateTimeOffset? replaced = null) =>
        InvitationRules.Usable(InvitationKinds.Organisation, 0, usedCount, null, off, replaced, limit, Now, organisationOff);

    [Fact]
    public void An_organisations_code_works_only_while_its_organisation_is_active()
    {
        Usable().ShouldBeTrue();
        Usable(organisationOff: OrganisationCodeStates.Blocked).ShouldBeFalse();
        Usable(organisationOff: OrganisationCodeStates.Closed).ShouldBeFalse();
    }

    [Fact]
    public void An_administrators_switch_off_and_a_replacement_stop_it_whatever_the_organisation()
    {
        Usable(off: Now.AddDays(-1)).ShouldBeFalse();
        Usable(replaced: Now.AddDays(-1)).ShouldBeFalse();
    }

    [Fact]
    public void An_organisations_code_has_its_own_uses_setting_with_no_limit_by_default()
    {
        SettingKeys.RegistrationOrganisationCodeUses.Default.ShouldBe(0);
        SettingKeys.All.ShouldContain(SettingKeys.RegistrationOrganisationCodeUses);
        Usable(usedCount: 10_000, limit: 0).ShouldBeTrue();
        Usable(usedCount: 4, limit: 5).ShouldBeTrue();
        Usable(usedCount: 5, limit: 5).ShouldBeFalse();
        InvitationRules.LimitFor(InvitationKinds.Organisation, personalLimit: 3, organisationLimit: 7).ShouldBe(7);
        InvitationRules.LimitFor(InvitationKinds.Personal, personalLimit: 3, organisationLimit: 7).ShouldBe(3);
        InvitationRules.LimitFor(InvitationKinds.Team, personalLimit: 3, organisationLimit: 7).ShouldBe(0);
    }

    [Fact]
    public void A_pending_account_never_holds_an_organisations_code()
    {
        InvitationRules.HeldWhilePending(InvitationKinds.Organisation, "ABCD-EFGH").ShouldBeNull();
        InvitationRules.HeldWhilePending(InvitationKinds.Personal, "ABCD-EFGH").ShouldBe("ABCD-EFGH");
        InvitationRules.HeldWhilePending(InvitationKinds.Team, "E2E-BANDYMAS").ShouldBe("E2E-BANDYMAS");
    }

    [Fact]
    public void A_completed_account_keeps_only_the_level_of_an_organisations_code()
    {
        var id = Guid.NewGuid();
        InvitationRules.KeptOnProfile(InvitationKinds.Organisation, id, "ABCD-EFGH", 3).ShouldBe(((Guid?)null, (string?)null, (int?)3));
        InvitationRules.KeptOnProfile(InvitationKinds.Personal, id, "ABCD-EFGH", 3).ShouldBe(((Guid?)id, (string?)"ABCD-EFGH", (int?)3));
        InvitationRules.KeptOnProfile(null, null, null, null).ShouldBe(((Guid?)null, (string?)null, (int?)null));
    }

    [Fact]
    public void An_organisations_code_brings_people_at_the_level_a_personal_code_of_its_first_asker_would()
    {
        InvitationRules.OrganisationCodeLevel(1).ShouldBe(2);
        InvitationRules.OrganisationCodeLevel(3).ShouldBe(4);
    }

    [Fact]
    public void A_statement_older_than_three_days_when_first_seen_is_never_sent()
    {
        FollowRules.StatementIsSendable(Now.AddDays(-3), Now).ShouldBeTrue();
        FollowRules.StatementIsSendable(Now.AddDays(-3).AddMinutes(-1), Now).ShouldBeFalse();
        FollowRules.StatementIsSendable(Now.AddMinutes(-5), Now).ShouldBeTrue();
    }

    [Fact]
    public void Only_those_who_followed_before_the_statement_became_public_and_before_it_was_seen_are_told()
    {
        var published = Now.AddMinutes(-10);
        FollowRules.IsStatementRecipient(Now.AddHours(-1), Now, published).ShouldBeTrue();
        FollowRules.IsStatementRecipient(Now.AddMinutes(-5), Now, published).ShouldBeFalse("followed after it became public");
        FollowRules.IsStatementRecipient(Now.AddMinutes(1), Now, published).ShouldBeFalse("followed after the job saw it");
    }
}
