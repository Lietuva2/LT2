using System.Security.Claims;
using Lt2.Identity.Application.Consents;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Profiles;

/// <summary>
/// The "Profilis" form (design 8.28.5), the person's own side: the display name (design 6.2), "Apie mane" and "Viešas profilis"
/// (design 8.25: the consent <c>public-profile</c>; switching off withdraws it, retires the id and ends every follow of it),
/// party support and the party shown on the public profile (design 5.2, 8.25.9). The form is saved whole or not at all:
/// every changed part is checked first (<see cref="ProfileFormRules"/>), then everything is written in one transaction.
/// Every rule is checked here, whoever calls (design 6.8). Audited without the name, the text or the party. A context of its
/// own per operation; switching off runs in the caller's context and transaction.
/// </summary>
internal sealed class ProfileHandlers(
    IDbContextFactory<IdentityDb> contexts,
    IParticipants participants,
    IWritingRestrictions restrictions,
    IMemberFollowEnd follows,
    ISettings settings,
    AccountEvents events,
    PartySupportHandlers parties,
    TimeProvider clock)
{
    public async Task<Result<MyProfileState>> MineAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null || profile.IsPending)
        {
            return Error.Unauthorized("auth.required");
        }

        return await StateAsync(db, profile, cancellationToken);
    }

    /// <summary>
    /// Saves the whole form (design 8.28.5): nothing is written unless every changed part may be (the answer then names every
    /// refused field); an unchanged part is not checked, written or audited; what changes is written in one transaction with
    /// each consent given or withdrawn and the audit entries each part has. <paramref name="choices"/> are the parties to
    /// choose from.
    /// </summary>
    public async Task<Result<MyProfileState>> SaveAsync(
        ClaimsPrincipal principal, ProfileForm form, IReadOnlyCollection<string> choices, string? ipHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(choices);
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        var facts = new ProfileFormFacts(
            await restrictions.IsRestrictedAsync(userId, cancellationToken),
            MpAccounts.Refusal(await participants.FindAsync(userId, cancellationToken)) is not null,
            await settings.GetAsync(SettingKeys.TextsMaxLength, cancellationToken),
            choices);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        // A pending account gives its name with the rest of its registration (design 6.2).
        if (profile.IsPending)
        {
            return Error.Conflict("registration.pending");
        }

        var isPublic = profile.PublicProfileId is not null;
        var partyBefore = await PartySupportHandlers.PartyAsync(db, userId, cancellationToken);
        var before = new ProfileFormBefore(
            profile.DisplayName,
            profile.About,
            isPublic,
            partyBefore,
            partyBefore is not null && isPublic && await PartySupportHandlers.ShownAsync(db, userId, cancellationToken));
        if (ProfileFormRules.Check(before, form, facts) is { Count: > 0 } refusals)
        {
            return Error.All(refusals);
        }

        var changes = ProfileFormRules.Changes(before, form);
        var now = clock.GetUtcNow();
        var actor = new Actor(userId, Roles.Member, ipHash);
        var dbTransaction = transaction.GetDbTransaction();

        // The display name (design 6.2): read live wherever it shows; audited that it changed, never the names.
        if (changes.Name)
        {
            profile.DisplayName = ProfileRules.DisplayName(form.DisplayName);
            await db.SaveChangesAsync(cancellationToken);
            await events.RecordAsync(dbTransaction, "profile.display-name-changed", userId, actor, null, null, cancellationToken);
        }

        // "Apie mane": a new text replaces a hidden one and shows at once (design 8.25.6).
        if (changes.About)
        {
            profile.About = ProfileRules.About(form.About);
            profile.AboutChangedAt = now;
            profile.AboutHiddenAt = null;
            profile.AboutHiddenStatement = null;
            await db.SaveChangesAsync(cancellationToken);
            await events.RecordAsync(dbTransaction, "profile.about-changed", userId, actor, null, null, cancellationToken);
        }

        if (changes.SwitchOn)
        {
            profile.PublicProfileId = await FreshIdAsync(db, cancellationToken);
            profile.PublicSince = now;
            db.Consents.Add(new Consent { Id = Ids.New(clock), UserId = userId, Kind = PublicProfileConsent.Kind, TextVersion = PublicProfileConsent.TextVersion, GivenAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await events.RecordAsync(dbTransaction, "profile.public-on", userId, actor, null, null, cancellationToken);
        }
        else if (changes.SwitchOff)
        {
            await SwitchOffAsync(db, profile, now, dbTransaction, cancellationToken);
            await events.RecordAsync(dbTransaction, "profile.public-off", userId, actor, null, null, cancellationToken);
        }

        if (changes.Party)
        {
            await parties.ChangeAsync(db, userId, before.Party, changes.NewParty, actor, dbTransaction, cancellationToken);
        }

        if (changes.GiveShown)
        {
            await parties.GiveShownAsync(db, userId, actor, dbTransaction, cancellationToken);
        }
        else if (changes.HideShown)
        {
            // Nothing more when switching off or a changed party withdrew it already.
            await parties.WithdrawShownAsync(db, userId, "box", actor, dbTransaction, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await StateAsync(db, profile, cancellationToken);
    }

    /// <summary>
    /// "Viešas profilis" off and nothing else (design 8.25.1, owner 2026-11-17): the route a member behind on the terms and a
    /// restricted member may use too. Changes nothing when the profile is off already.
    /// </summary>
    public async Task<Result<MyProfileState>> SwitchOffOwnAsync(ClaimsPrincipal principal, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        if (await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) is not { IsPending: false } profile)
        {
            return Error.Unauthorized("auth.required");
        }

        if (profile.PublicProfileId is not null)
        {
            var now = clock.GetUtcNow();
            await SwitchOffAsync(db, profile, now, transaction.GetDbTransaction(), cancellationToken);
            await events.RecordAsync(transaction.GetDbTransaction(), "profile.public-off", userId, new Actor(userId, Roles.Member, ipHash), null, null, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await StateAsync(db, profile, cancellationToken);
    }

    /// <summary>
    /// The consent text in force accepted by a person whose profile is on under an earlier text (design 8.25.1, owner 2026-11-19):
    /// the earlier public-profile consents are withdrawn and one to the version in force is recorded, in one transaction; the
    /// profile, its page and its followers are not touched. Audited without details.
    /// </summary>
    public async Task<Result<Unit>> AcceptConsentOwnAsync(ClaimsPrincipal principal, string textVersion, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        if (textVersion != PublicProfileConsent.TextVersion)
        {
            // The page showed an older wording: the person must see the text in force.
            return Error.Conflict("profile.consent-outdated");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        if (await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) is not { IsPending: false } profile)
        {
            return Error.Unauthorized("auth.required");
        }

        if (profile.PublicProfileId is null)
        {
            return Error.Conflict("profile.not-public");
        }

        if (!await PublicProfileConsents.CurrentAsync(db, userId, cancellationToken))
        {
            var now = clock.GetUtcNow();
            await db.Consents
                .Where(c => c.UserId == userId && c.Kind == PublicProfileConsent.Kind && c.WithdrawnAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, now), cancellationToken);
            db.Consents.Add(new Consent { Id = Ids.New(clock), UserId = userId, Kind = PublicProfileConsent.Kind, TextVersion = PublicProfileConsent.TextVersion, GivenAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await events.RecordAsync(transaction.GetDbTransaction(), "profile.consent-accepted", userId, new Actor(userId, Roles.Member, ipHash), null, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Switching off (design 8.25.1), in the caller's context and transaction (<paramref name="db"/> tracks
    /// <paramref name="profile"/>): the consent withdrawn, the id retired for good (410), every follow of it ended (no follower
    /// is told). Also used when the account is deleted and when the MP's own account takes the profile over.
    /// </summary>
    public async Task SwitchOffAsync(IdentityDb db, Profile profile, DateTimeOffset now, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (profile.PublicProfileId is not { } id)
        {
            return;
        }

        db.RetiredProfileIds.Add(new RetiredProfileId { Id = id, RetiredAt = now });
        profile.PublicProfileId = null;
        profile.PublicSince = null;
        await db.SaveChangesAsync(cancellationToken);
        await db.Consents
            .Where(c => c.UserId == profile.UserId && c.Kind == PublicProfileConsent.Kind && c.WithdrawnAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, now), cancellationToken);

        // Design 8.25.9: the party shown on the page goes with it, and switching on again does not bring it back.
        await parties.WithdrawShownAsync(db, profile.UserId, "profile", new Actor(profile.UserId, Roles.Member, null), transaction, cancellationToken);
        await follows.EndAsync(id, transaction, cancellationToken);
    }

    /// <summary>A random id never issued before: not in use and not retired.</summary>
    private static async Task<string> FreshIdAsync(IdentityDb db, CancellationToken cancellationToken)
    {
        while (true)
        {
            var id = ProfileRules.NewId();
            if (!await db.Profiles.AnyAsync(p => p.PublicProfileId == id, cancellationToken)
                && !await db.RetiredProfileIds.AnyAsync(r => r.Id == id, cancellationToken))
            {
                return id;
            }
        }
    }

    /// <summary>The form's state after <paramref name="profile"/>, read in <paramref name="db"/>.</summary>
    private async Task<MyProfileState> StateAsync(IdentityDb db, Profile profile, CancellationToken cancellationToken)
    {
        var party = await PartySupportHandlers.PartyAsync(db, profile.UserId, cancellationToken);
        var shown = party is not null && profile.PublicProfileId is not null && await PartySupportHandlers.ShownAsync(db, profile.UserId, cancellationToken);
        return new(
            profile.DisplayName,
            profile.About,
            profile.AboutHiddenAt is not null,
            profile.AboutHiddenStatement,
            profile.PublicProfileId,
            profile.PublicSince,
            PublicProfileConsent.TextVersion,
            await settings.GetAsync(SettingKeys.TextsMaxLength, cancellationToken),
            profile.PublicProfileId is { } id ? MemberAddress.Page(id, profile.DisplayName) : null,
            party,
            shown);
    }
}
