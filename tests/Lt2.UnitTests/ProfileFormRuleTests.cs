using Lt2.Identity;
using Lt2.Identity.Domain;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.28.5: "Profilis" is one form saved whole or not at all. Every refusal is found before anything is written, each
/// named by its field; a part that did not change is not checked.
/// </summary>
public sealed class ProfileFormRuleTests
{
    private const string Party = "Reformų partija";

    private static readonly ProfileFormFacts Member = new(Restricted: false, MpAccount: false, AboutMaxLength: 2000, Parties: [Party, "Pilietinė sąjunga"]);

    private static readonly ProfileFormBefore Held = new("Rūta M.", "Mokytoja iš Kauno.", Public: false, Party: null, PartyShown: false);

    /// <summary>The form as it stands: what is held, sent back.</summary>
    private static ProfileForm Unchanged(ProfileFormBefore before) =>
        new(before.DisplayName, before.About, before.Public, before.Public ? PublicProfileConsent.TextVersion : null, before.Party, false, before.PartyShown,
            before.PartyShown ? PartyPublicConsent.TextVersion : null);

    private static IEnumerable<(string? Field, string Code)> Refusals(ProfileFormBefore before, ProfileForm form, ProfileFormFacts? facts = null) =>
        ProfileFormRules.Check(before, form, facts ?? Member).Select(e => (e.Field, e.Code));

    [Fact]
    public void The_form_as_it_stands_changes_nothing_and_is_refused_nothing_even_under_a_writing_restriction()
    {
        var shown = Held with { Public = true, Party = Party, PartyShown = true };
        foreach (var before in new[] { Held, shown, new ProfileFormBefore(null, null, false, null, false) })
        {
            ProfileFormRules.Changes(before, Unchanged(before)).Any.ShouldBeFalse();
            Refusals(before, Unchanged(before), Member with { Restricted = true }).ShouldBeEmpty();
            Refusals(before, Unchanged(before), Member with { MpAccount = true }).ShouldBeEmpty("what an MP's account already holds is no new part");
        }

        // An "Apie mane" over a lowered limit or holding what is refused now is not refused while it is not changed.
        Refusals(Held with { About = new string('a', 3000) }, Unchanged(Held with { About = new string('a', 3000) })).ShouldBeEmpty();
    }

    [Fact]
    public void Under_a_writing_restriction_only_a_changed_name_or_a_written_text_is_refused()
    {
        var restricted = Member with { Restricted = true };
        Refusals(Held, Unchanged(Held) with { Party = Party, PartyConsent = true }, restricted).ShouldBeEmpty("the party is no writing");
        Refusals(Held, Unchanged(Held) with { About = null }, restricted).ShouldBeEmpty("clearing the text is erasure");
        Refusals(Held, Unchanged(Held) with { About = "Kitas tekstas." }, restricted).ShouldBe([("about", "writing.restricted")]);
        Refusals(Held, Unchanged(Held) with { DisplayName = "Rūta Mockutė", About = "Kitas." }, restricted)
            .ShouldBe([("displayName", "writing.restricted"), ("about", "writing.restricted")]);
    }

    [Fact]
    public void Every_refused_field_is_named_at_once()
    {
        var form = new ProfileForm(
            DisplayName: new string('a', 61),
            About: "Asmens kodas 39001010000",
            Public: true,
            PublicConsentVersion: "2020-01-01",
            Party: "Išgalvota partija",
            PartyConsent: true,
            PartyPublic: true,
            PartyPublicConsentVersion: "2020-01-01");

        Refusals(Held, form).ShouldBe([
            ("displayName", "display-name.too-long"),
            ("about", "about.personal-code"),
            ("public", "profile.consent-outdated"),
            ("party", "party.unknown"),
            ("partyPublic", "party.consent-outdated"),
        ]);
        Error.All(ProfileFormRules.Check(Held, form, Member)).Code.ShouldBe("validation");
        var one = Error.All(ProfileFormRules.Check(Held, form with { DisplayName = Held.DisplayName, Public = false, Party = null, PartyPublic = false }, Member));
        (one.Code, one.Field, one.Refusals.Count).ShouldBe(("about.personal-code", "about", 0), "one refusal stays itself");
    }

    [Fact]
    public void A_public_profile_needs_a_name_named_where_it_is_missing()
    {
        var nameless = Held with { DisplayName = null };
        var on = Unchanged(nameless) with { Public = true, PublicConsentVersion = PublicProfileConsent.TextVersion };
        Refusals(nameless, on).ShouldBe([("public", "profile.name-required")]);
        Refusals(nameless, on with { DisplayName = "Rūta M." }).ShouldBeEmpty("a name given in the same save");

        var open = Held with { Public = true };
        Refusals(open, Unchanged(open) with { DisplayName = " " }).ShouldBe([("displayName", "profile.name-required")]);
        Refusals(open, Unchanged(open) with { DisplayName = " ", Public = false }).ShouldBeEmpty("switched off in the same save");
    }

    [Fact]
    public void The_party_needs_its_consent_and_is_refused_to_an_mps_own_account_which_may_remove_one()
    {
        Refusals(Held, Unchanged(Held) with { Party = Party }).ShouldBe([("partyConsent", "party.consent-required")]);
        Refusals(Held, Unchanged(Held) with { Party = Party, PartyConsent = true }).ShouldBeEmpty();
        Refusals(Held, Unchanged(Held) with { Party = Party, PartyConsent = true }, Member with { MpAccount = true }).ShouldBe([("party", "mp.takes-no-part")]);
        var mpWithParty = Held with { Party = Party };
        Refusals(mpWithParty, Unchanged(mpWithParty) with { Party = "" }, Member with { MpAccount = true }).ShouldBeEmpty();
        Refusals(Held, Unchanged(Held) with { Public = true, PublicConsentVersion = PublicProfileConsent.TextVersion }, Member with { MpAccount = true })
            .ShouldBe([("public", "mp.takes-no-part")]);
    }

    [Fact]
    public void The_party_is_shown_only_with_its_own_consent_a_party_and_a_public_profile_as_the_form_saves_them()
    {
        var withParty = Held with { Party = Party };
        var tick = Unchanged(withParty) with { PartyPublic = true, PartyPublicConsentVersion = PartyPublicConsent.TextVersion };
        Refusals(withParty, tick).ShouldBe([("partyPublic", "party.profile-off")]);
        Refusals(withParty, tick with { Public = true, PublicConsentVersion = PublicProfileConsent.TextVersion }).ShouldBeEmpty("switched on in the same save");
        Refusals(Held, Unchanged(Held) with { Public = true, PublicConsentVersion = PublicProfileConsent.TextVersion, PartyPublic = true, PartyPublicConsentVersion = PartyPublicConsent.TextVersion })
            .ShouldBe([("partyPublic", "party.none")]);

        var shown = Held with { Public = true, Party = Party, PartyShown = true };
        var changes = ProfileFormRules.Changes(shown, Unchanged(shown) with { Party = "Pilietinė sąjunga", PartyConsent = true });
        (changes.Party, changes.GiveShown).ShouldBe((true, true), "the box still ticked beside a new party gives the consent again");
        ProfileFormRules.Changes(shown, Unchanged(shown) with { PartyPublic = false }).HideShown.ShouldBeTrue();
        Refusals(shown, Unchanged(shown) with { Public = false, PartyPublic = true }).ShouldBe([("partyPublic", "party.profile-off")]);
    }
}
