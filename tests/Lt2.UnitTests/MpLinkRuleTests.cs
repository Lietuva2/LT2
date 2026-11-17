using Lt2.Identity.Domain;
using Lt2.Parliament.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>Design 8.5.1 to 8.5.4: the official address, the one-time tokens and who may do what as or for an MP.</summary>
public sealed class MpLinkRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static (long, string, string) Email(long person, string address) => (person, OfficialAddresses.EmailKind, address);

    [Theory]
    [InlineData("pavyzdys.ona.jonaitiene@lrs.lt", "pavyzdys.ona.jonaitiene@lrs.lt")]
    [InlineData("  Pavyzdys.Ona.Jonaitiene@LRS.LT ", "pavyzdys.ona.jonaitiene@lrs.lt")]
    public void An_official_address_is_on_the_Seimas_domain_compared_ignoring_case(string typed, string normalised) =>
        OfficialAddresses.Normalise(typed).ShouldBe(normalised);

    [Theory]
    [InlineData("ona@pavyzdys.test")]
    [InlineData("ona@posedziai.lrs.lt")]
    [InlineData("ona@lrs.lt.pavyzdys.test")]
    [InlineData("lrs.lt")]
    [InlineData("@lrs.lt")]
    [InlineData("ona@@lrs.lt")]
    [InlineData("ona jonaitiene@lrs.lt")]
    [InlineData("")]
    [InlineData(null)]
    public void Any_other_address_is_not_official(string? address) => OfficialAddresses.Normalise(address).ShouldBeNull();

    [Fact]
    public void Only_an_email_contact_is_an_official_address()
    {
        OfficialAddresses.IsOfficial(OfficialAddresses.EmailKind, "pavyzdys.ona.jonaitiene@lrs.lt").ShouldBeTrue();
        OfficialAddresses.IsOfficial("Tel.", "pavyzdys.ona.jonaitiene@lrs.lt").ShouldBeFalse();
    }

    [Fact]
    public void The_owner_of_an_official_address_is_found_whatever_its_case()
    {
        var contacts = new[] { Email(1, "pavyzdys.jonas.petraitis@lrs.lt"), Email(5, "Pavyzdys.Ona.Jonaitiene@lrs.lt") };

        OfficialAddresses.Owner(contacts, "PAVYZDYS.ONA.JONAITIENE@lrs.lt").ShouldBe(5);
        OfficialAddresses.Owner(contacts, "kita@lrs.lt").ShouldBeNull();
    }

    [Fact]
    public void An_address_listed_for_more_than_one_mp_belongs_to_nobody()
    {
        var contacts = new[] { Email(1, "bendras@lrs.lt"), Email(2, "BENDRAS@lrs.lt"), Email(3, "pavyzdys.trecias@lrs.lt") };

        OfficialAddresses.Owner(contacts, "bendras@lrs.lt").ShouldBeNull();
        OfficialAddresses.Owned(contacts).Values.ShouldBe([3L]);
    }

    [Fact]
    public void An_mp_listing_the_same_address_twice_still_owns_it() =>
        OfficialAddresses.Owner([Email(4, "pavyzdys.ketvirtas@lrs.lt"), Email(4, "pavyzdys.ketvirtas@LRS.lt")], "pavyzdys.ketvirtas@lrs.lt").ShouldBe(4);

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void An_mp_makes_at_most_ten_office_invitations_a_day(int made, bool more) =>
        PoliticianRules.MayMakeOfficeInvitation(made).ShouldBe(more);

    private static PoliticianToken Takeover(Guid account, DateTimeOffset? usedAt = null) => new()
    {
        Kind = PoliticianTokenKinds.Takeover,
        SeimasPersonId = 99000005,
        UserId = account,
        CreatedAt = Now,
        ExpiresAt = Now + PoliticianRules.TakeoverLifetime,
        UsedAt = usedAt,
    };

    [Fact]
    public void A_takeover_link_works_once_within_24_hours_and_only_for_its_account()
    {
        var account = Guid.CreateVersion7(Now);
        var token = Takeover(account);

        PoliticianRules.Check(token, PoliticianTokenKinds.Takeover, account, Now.AddHours(23)).ShouldBe(PoliticianTokenCheck.Valid);
        PoliticianRules.Check(token, PoliticianTokenKinds.Takeover, account, Now.AddHours(24)).ShouldBe(PoliticianTokenCheck.Expired);
        PoliticianRules.Check(Takeover(account, Now), PoliticianTokenKinds.Takeover, account, Now).ShouldBe(PoliticianTokenCheck.Used);
        PoliticianRules.Check(token, PoliticianTokenKinds.Takeover, Guid.CreateVersion7(Now), Now).ShouldBe(PoliticianTokenCheck.OtherAccount);
        PoliticianRules.Check(null, PoliticianTokenKinds.Takeover, account, Now).ShouldBe(PoliticianTokenCheck.Unknown);
        PoliticianRules.Check(token, PoliticianTokenKinds.Office, account, Now).ShouldBe(PoliticianTokenCheck.WrongKind);
    }

    [Fact]
    public void An_office_invitation_is_for_whoever_the_mps_account_gives_it_to_within_seven_days()
    {
        var mpAccount = Guid.CreateVersion7(Now);
        var invitation = new PoliticianToken
        {
            Kind = PoliticianTokenKinds.Office, SeimasPersonId = 99000005, UserId = mpAccount, CreatedAt = Now, ExpiresAt = Now + PoliticianRules.OfficeInvitationLifetime,
        };

        PoliticianRules.Check(invitation, PoliticianTokenKinds.Office, Guid.CreateVersion7(Now), Now.AddDays(6)).ShouldBe(PoliticianTokenCheck.Valid);
        PoliticianRules.Check(invitation, PoliticianTokenKinds.Office, Guid.CreateVersion7(Now), Now.AddDays(7)).ShouldBe(PoliticianTokenCheck.Expired);
        PoliticianRules.Check(invitation, PoliticianTokenKinds.Takeover, mpAccount, Now).ShouldBe(PoliticianTokenCheck.WrongKind);
    }

    [Fact]
    public void At_most_three_takeover_links_a_day_and_five_office_members()
    {
        PoliticianRules.MaySendTakeoverLink(2).ShouldBeTrue();
        PoliticianRules.MaySendTakeoverLink(3).ShouldBeFalse();
        PoliticianRules.MayAddOfficeMember(4).ShouldBeTrue();
        PoliticianRules.MayAddOfficeMember(5).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Speaking_for_an_mp_needs_a_link_and_a_passkey_session(bool linked, bool passkeySession, bool may) =>
        PoliticianRules.MaySpeakForMp(linked, passkeySession).ShouldBe(may);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void A_code_session_never_changes_the_passkeys_of_a_linked_account(bool linked, bool passkeySession, bool may) =>
        PoliticianRules.MayChangePasskeys(linked, passkeySession).ShouldBe(may);

    [Fact]
    public void The_mps_own_account_takes_no_part_and_an_office_member_does()
    {
        var mp = new ParticipantFacts(Guid.CreateVersion7(Now), "Ona J.", "pavyzdys.ona.jonaitiene@lrs.lt", Now, true, IsMpAccount: true);
        var office = mp with { IsMpAccount = false };

        MpAccounts.Refusal(mp).ShouldBe(MpAccounts.TakesNoPart);
        MpAccounts.TakesNoPart.Code.ShouldBe("mp.takes-no-part");
        MpAccounts.Refusal(office).ShouldBeNull();
        MpAccounts.Refusal(null).ShouldBeNull();
    }
}
