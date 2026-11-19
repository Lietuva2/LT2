using Lt2.SharedKernel;

namespace Lt2.Identity.Domain;

/// <summary>What the "Profilis" form holds now (design 8.28.5): the values a save compares the form with.</summary>
/// <param name="PartyShown">The consent <c>party-public</c> is in force (only with a party and a public profile).</param>
internal sealed record ProfileFormBefore(string? DisplayName, string? About, bool Public, string? Party, bool PartyShown);

/// <summary>
/// What a save is checked against besides the form: a writing restriction, whether the account is an MP's own,
/// <c>Texts:MaxLength</c> and the parties to choose from.
/// </summary>
internal sealed record ProfileFormFacts(bool Restricted, bool MpAccount, int AboutMaxLength, IReadOnlyCollection<string> Parties);

/// <summary>
/// What a save changes (design 8.28.5): only a part that differs from what is held is a change; the rest is not checked,
/// written or audited.
/// </summary>
/// <param name="GiveShown">The consent <c>party-public</c> is given (the box ticked where it was not, or beside a new party).</param>
/// <param name="HideShown">The box unticked while the party was shown (switching off and a changed party withdraw it too).</param>
internal sealed record ProfileFormChanges(
    bool Name, bool About, bool SwitchOn, bool SwitchOff, bool Party, string? NewParty, bool GiveShown, bool HideShown)
{
    public bool Any => Name || About || SwitchOn || SwitchOff || Party || GiveShown || HideShown;
}

/// <summary>
/// "Profilis" as one form saved whole or not at all (design 8.28.5): which parts change, and every refusal, each named by the
/// request's field, before anything is written. The same rules each part had on its own (design 6.2, 8.25, 8.25.9, 5.2).
/// </summary>
internal static class ProfileFormRules
{
    /// <summary>The party as stored: null when none is chosen.</summary>
    public static string? Party(string? chosen) => string.IsNullOrWhiteSpace(chosen) ? null : chosen;

    /// <summary>What the form changes against what is held.</summary>
    public static ProfileFormChanges Changes(ProfileFormBefore before, ProfileForm form)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(form);
        var party = Party(form.Party);
        var partyChanged = !string.Equals(party, before.Party, StringComparison.Ordinal);
        return new ProfileFormChanges(
            Name: ProfileRules.IsChange(before.DisplayName, form.DisplayName),
            About: ProfileRules.IsAboutChange(before.About, form.About),
            SwitchOn: form.Public && !before.Public,
            SwitchOff: !form.Public && before.Public,
            Party: partyChanged,
            NewParty: party,
            GiveShown: form.PartyPublic && (!before.PartyShown || partyChanged),
            HideShown: !form.PartyPublic && before.PartyShown);
    }

    /// <summary>
    /// Every refusal of the form, each with the request's field it concerns (empty: the form may be saved). Only changed parts
    /// are checked: an unchanged "Apie mane" is not refused under a writing restriction, nor an unchanged name.
    /// </summary>
    public static IReadOnlyList<Error> Check(ProfileFormBefore before, ProfileForm form, ProfileFormFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var changes = Changes(before, form);
        var refusals = new List<Error>();
        var name = ProfileRules.DisplayName(form.DisplayName);

        // The display name (design 6.2): the registration's rule, a writing restriction, and a name while public (8.25.6).
        if (changes.Name)
        {
            if (ProfileRules.CheckDisplayName(form.DisplayName) is { } tooLong)
            {
                refusals.Add(Error.Validation(tooLong, Fields.DisplayName));
            }
            else if (facts.Restricted)
            {
                refusals.Add(Error.Forbidden("writing.restricted") with { Field = Fields.DisplayName });
            }
            else if (ProfileRules.CheckNameWhilePublic(form.Public, name) is { } required)
            {
                refusals.Add(Error.Validation(required, Fields.DisplayName));
            }
        }

        // "Apie mane" (design 8.25.3): its length and no personal code; writing or changing it is blocked by a writing
        // restriction, clearing it is erasure and stays open (owner, 2026-11-17).
        if (changes.About)
        {
            if (ProfileRules.CheckAbout(form.About, facts.AboutMaxLength) is { } aboutError)
            {
                refusals.Add(aboutError == "about.too-long"
                    ? Error.Validation(aboutError, Fields.About) with { Numbers = [MemberTextLength.Allowed(facts.AboutMaxLength)] }
                    : Error.Validation(aboutError, Fields.About));
            }
            else if (ProfileRules.About(form.About) is not null && facts.Restricted)
            {
                refusals.Add(Error.Forbidden("writing.restricted") with { Field = Fields.About });
            }
        }

        // "Viešas profilis" switched on (design 8.25.1): the consent text in force, a name, never the MP's own account (8.5.4).
        if (changes.SwitchOn)
        {
            if (form.PublicConsentVersion != PublicProfileConsent.TextVersion)
            {
                refusals.Add(Error.Conflict("profile.consent-outdated") with { Field = Fields.Public });
            }
            else if (!changes.Name && ProfileRules.CheckNameWhilePublic(true, name) is { } required)
            {
                refusals.Add(Error.Validation(required, Fields.Public));
            }
            else if (facts.MpAccount)
            {
                refusals.Add(MpAccounts.TakesNoPart with { Field = Fields.Public });
            }
        }

        // Party support (design 5.2): one of the parties, with the explicit consent; the MP's own account declares none (8.5.4),
        // removing one stays open.
        if (changes.Party && changes.NewParty is { } party)
        {
            if (!facts.Parties.Contains(party))
            {
                refusals.Add(Error.Validation("party.unknown", Fields.Party));
            }
            else if (!form.PartyConsent)
            {
                refusals.Add(Error.Validation("party.consent-required", Fields.PartyConsent));
            }
            else if (facts.MpAccount)
            {
                refusals.Add(MpAccounts.TakesNoPart with { Field = Fields.Party });
            }
        }

        // The party on the public profile (design 8.25.9): its own consent against the text in force, never for the MP's own
        // account, only with a party and a public profile as the form saves them.
        if (form.PartyPublic)
        {
            if (changes.GiveShown && form.PartyPublicConsentVersion != PartyPublicConsent.TextVersion)
            {
                refusals.Add(Error.Conflict("party.consent-outdated") with { Field = Fields.PartyPublic });
            }
            else if (changes.GiveShown && facts.MpAccount)
            {
                refusals.Add(MpAccounts.TakesNoPart with { Field = Fields.PartyPublic });
            }
            else if (changes.NewParty is null)
            {
                refusals.Add(Error.Conflict("party.none") with { Field = Fields.PartyPublic });
            }
            else if (!form.Public)
            {
                refusals.Add(Error.Conflict("party.profile-off") with { Field = Fields.PartyPublic });
            }
        }

        return refusals;
    }

    /// <summary>The request's fields, as the answer names them (camelCase, as the JSON of <c>ProfileRequest</c>).</summary>
    internal static class Fields
    {
        public const string DisplayName = "displayName";
        public const string About = "about";
        public const string Public = "public";
        public const string Party = "party";
        public const string PartyConsent = "partyConsent";
        public const string PartyPublic = "partyPublic";
    }
}
