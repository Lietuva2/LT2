using Lt2.Api.Contracts.Me;
using Lt2.Identity.Domain;
using Lt2.Notifications.Follows;
using Lt2.SharedKernel;
using Lt2.Web.Client;

namespace Lt2.UnitTests;

/// <summary>Design 8.25: a member's public profile and "Apie mane", as rules.</summary>
public sealed class ProfileRuleTests
{
    // An invented personal code with a valid birth date (1990-01-01) and check digit.
    private const string PersonalCode = "39001010000";

    [Fact]
    public void The_invented_personal_code_of_these_tests_is_a_valid_one() => PersonalCodes.Is(PersonalCode).ShouldBeTrue();

    [Fact]
    public void A_personal_code_in_about_me_is_refused() =>
        ProfileRules.CheckAbout($"Mano asmens kodas {PersonalCode}.", 2000).ShouldBe("about.personal-code");

    [Theory]
    [InlineData("Rašykite: ruta.mockute@pavyzdys.test")]
    [InlineData("Tel. +370 600 00000")]
    [InlineData("Daugiau: https://pavyzdys.test/ruta")]
    [InlineData("Mokytoja iš Kauno, Pilietinės sąjungos narė.")]
    public void Links_and_the_persons_own_contacts_are_allowed_in_about_me(string text) => ProfileRules.CheckAbout(text, 2000).ShouldBeNull();

    [Fact]
    public void About_me_is_as_long_as_the_members_texts_setting_allows()
    {
        ProfileRules.CheckAbout(new string('a', 2000), 2000).ShouldBeNull();
        ProfileRules.CheckAbout(new string('a', 2001), 2000).ShouldBe("about.too-long");
        ProfileRules.CheckAbout(new string('a', 2001), 0).ShouldBeNull("0 = no limit but the technical ceiling");
        ProfileRules.CheckAbout(new string('a', Lt2.SharedKernel.MemberTextLength.Ceiling + 1), 0).ShouldBe("about.too-long");
    }

    [Fact]
    public void An_empty_about_me_is_none_and_is_always_allowed()
    {
        ProfileRules.About("  \n ").ShouldBeNull();
        ProfileRules.CheckAbout(null, 10).ShouldBeNull();
        ProfileRules.About(" Pirma eilutė\r\nantra\u0007 ").ShouldBe("Pirma eilutė\nantra");
    }

    [Fact]
    public void Only_a_different_about_me_is_a_change()
    {
        ProfileRules.IsAboutChange("Tekstas", " Tekstas ").ShouldBeFalse();
        ProfileRules.IsAboutChange(null, "").ShouldBeFalse();
        ProfileRules.IsAboutChange("Tekstas", "Kitas").ShouldBeTrue();
    }

    [Fact]
    public void A_public_profile_needs_a_display_name()
    {
        ProfileRules.CheckNameWhilePublic(true, null).ShouldBe("profile.name-required");
        ProfileRules.CheckNameWhilePublic(true, "  ").ShouldBe("profile.name-required");
        ProfileRules.CheckNameWhilePublic(true, "Rūta M.").ShouldBeNull();
        ProfileRules.CheckNameWhilePublic(false, null).ShouldBeNull();
    }

    [Fact]
    public void A_profile_id_is_ten_random_letters_and_digits_never_the_name()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => ProfileRules.NewId()).ToList();
        ids.ShouldAllBe(id => ProfileRules.IsWellFormedId(id));
        ids.ShouldAllBe(id => id.Length == 10);
        ids.Distinct().Count().ShouldBe(ids.Count);
        ProfileRules.IsWellFormedId("ruta-mockute").ShouldBeFalse();
        ProfileRules.IsWellFormedId("ABCDEFGH23").ShouldBeFalse();
        ProfileRules.IsWellFormedId(null).ShouldBeFalse();
    }

    [Fact]
    public void A_hidden_about_me_is_shown_nowhere()
    {
        ProfileRules.ShownAbout("Tekstas", DateTimeOffset.UnixEpoch).ShouldBeNull();
        ProfileRules.ShownAbout("Tekstas", null).ShouldBe("Tekstas");
    }

    [Fact]
    public void A_hidden_about_me_may_be_restored_only_while_it_is_hidden()
    {
        var hidden = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        ProfileRules.MayRestore(hidden, hidden.AddDays(-1)).ShouldBeTrue();
        ProfileRules.MayRestore(null, null).ShouldBeFalse("a text the person replaced is no longer hidden");
    }

    [Fact]
    public void The_consent_the_form_shows_is_the_version_the_server_takes() =>
        ProfileLimits.ConsentVersion.ShouldBe(PublicProfileConsent.TextVersion);

    /// <summary>Design 8.25.9: the party's box sends the version of the text it showed, the one the server takes.</summary>
    [Fact]
    public void The_party_box_shows_the_version_the_server_takes()
    {
        PartyPublicLimits.ConsentVersion.ShouldBe(PartyPublicConsent.TextVersion);
        PartyPublicConsent.Kind.ShouldBe("party-public");
        new MyProfileResponse().PartyPublicConsentVersion.ShouldBe(PartyPublicConsent.TextVersion);
        new MyProfileResponse().PublicConsentVersion.ShouldBe(PublicProfileConsent.TextVersion);
    }

    [Fact]
    public void A_profile_report_offers_impersonation_first() =>
        ProfileLimits.ReportGrounds.ShouldBe(Lt2.Identity.ProfileReportGrounds.All);

    [Theory]
    [InlineData(null, 10)]
    [InlineData(3, 10)]
    [InlineData(20, 20)]
    [InlineData(100000, ProfileLimits.MaxShown)]
    public void A_members_page_shows_ten_then_more_by_its_address(int? asked, int shown) => ProfileLimits.Shown(asked).ShouldBe(shown);
}

/// <summary>Design 8.25.5: what following a member sends, as rules.</summary>
public sealed class MemberFollowRuleTests
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_follower_hears_of_an_argument_only_when_they_followed_before_it_became_public()
    {
        var author = Guid.NewGuid();
        var follower = Guid.NewGuid();
        var published = Seen.AddMinutes(-10);
        FollowRules.IsMemberArgumentRecipient(follower, author, published.AddMinutes(-1), Seen, published).ShouldBeTrue();
        FollowRules.IsMemberArgumentRecipient(follower, author, published.AddMinutes(1), Seen, published).ShouldBeFalse();
        FollowRules.IsMemberArgumentRecipient(author, author, published.AddMinutes(-1), Seen, published).ShouldBeFalse("never the author");
    }

    [Fact]
    public void An_argument_older_than_three_days_when_first_seen_is_never_sent()
    {
        FollowRules.MemberArgumentIsSendable(Seen.AddDays(-3), Seen).ShouldBeTrue();
        FollowRules.MemberArgumentIsSendable(Seen.AddDays(-3).AddMinutes(-1), Seen).ShouldBeFalse();
    }
}

/// <summary>Design 8.25.3: links in "Apie mane": only https, shown as written, everything else text.</summary>
public sealed class UserTextTests
{
    [Fact]
    public void An_https_address_becomes_a_link_shown_as_written_and_its_trailing_dot_stays_text()
    {
        var parts = UserText.Parts("Mano svetainė: https://pavyzdys.test/apie. Ačiū");
        parts.Select(p => (p.Text, p.Link)).ShouldBe(
        [
            ("Mano svetainė: ", null),
            ("https://pavyzdys.test/apie", "https://pavyzdys.test/apie"),
            (". Ačiū", null),
        ]);
    }

    [Theory]
    [InlineData("http://pavyzdys.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("www.pavyzdys.test")]
    [InlineData("<a href=\"https://pavyzdys.test\">x</a>")]
    public void Nothing_else_becomes_a_link_and_markup_stays_text(string text) =>
        UserText.Parts(text).Where(p => p.Link is not null).Select(p => p.Link).ShouldAllBe(l => l!.StartsWith("https://pavyzdys.test", StringComparison.Ordinal));

    [Fact]
    public void Text_without_links_is_one_part() => UserText.Parts("Tik tekstas").ShouldHaveSingleItem().Link.ShouldBeNull();
}
