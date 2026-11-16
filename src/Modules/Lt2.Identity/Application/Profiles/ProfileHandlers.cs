using System.Security.Claims;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Profiles;

/// <summary>
/// A member's public profile and "Apie mane" (design 8.25), the person's own side: reading the state, writing the text,
/// switching "Viešas profilis" on (the consent <c>public-profile</c>) and off (the consent withdrawn, the id retired, every
/// follow of it ended, in one transaction). Every rule is checked here, whoever calls (design 6.8). Audited without the text.
/// </summary>
internal sealed class ProfileHandlers(
    IdentityDb db,
    IParticipants participants,
    IWritingRestrictions restrictions,
    IMemberFollowEnd follows,
    ISettings settings,
    AccountEvents events,
    TimeProvider clock)
{
    public async Task<Result<MyProfileState>> MineAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null || profile.IsPending)
        {
            return Error.Unauthorized("auth.required");
        }

        return await StateAsync(profile, cancellationToken);
    }

    public async Task<Result<MyProfileState>> ChangeAsync(ClaimsPrincipal principal, ProfileChange change, string? ipHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        var maxLength = await settings.GetAsync(SettingKeys.TextsMaxLength, cancellationToken);
        if (ProfileRules.CheckAbout(change.About, maxLength) is { } aboutError)
        {
            return aboutError == "about.too-long"
                ? Error.Validation(aboutError, "about") with { Numbers = [MemberTextLength.Allowed(maxLength)] }
                : Error.Validation(aboutError, "about");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        if (profile.IsPending)
        {
            return Error.Conflict("registration.pending");
        }

        var now = clock.GetUtcNow();
        var actor = new Actor(userId, Roles.Member, ipHash);
        var dbTransaction = transaction.GetDbTransaction();

        // "Apie mane": the text is for others to read, so a writing restriction blocks writing or changing it (design 8.25.3);
        // clearing it is erasure, not writing, and stays open (owner, 2026-11-17). A new text replaces a hidden one and shows at
        // once (design 8.25.6).
        if (ProfileRules.IsAboutChange(profile.About, change.About))
        {
            if (ProfileRules.About(change.About) is not null && await restrictions.IsRestrictedAsync(userId, cancellationToken))
            {
                return Error.Forbidden("writing.restricted");
            }

            profile.About = ProfileRules.About(change.About);
            profile.AboutChangedAt = now;
            profile.AboutHiddenAt = null;
            profile.AboutHiddenStatement = null;
            await events.RecordAsync(dbTransaction, "profile.about-changed", userId, actor, null, null, cancellationToken);
        }

        var isPublic = profile.PublicProfileId is not null;
        if (change.Public && !isPublic)
        {
            if (change.ConsentVersion != PublicProfileConsent.TextVersion)
            {
                // The form showed another wording: the person must see the text in force.
                return Error.Conflict("profile.consent-outdated");
            }

            if (ProfileRules.CheckNameWhilePublic(true, profile.DisplayName) is { } nameError)
            {
                return Error.Validation(nameError, "public");
            }

            // Design 8.5.4: the MP's own account takes no part as a member; it speaks only through its responses.
            if (MpAccounts.Refusal(await participants.FindAsync(userId, cancellationToken)) is { } mp)
            {
                return mp;
            }

            profile.PublicProfileId = await FreshIdAsync(cancellationToken);
            profile.PublicSince = now;
            db.Consents.Add(new Consent { Id = Ids.New(clock), UserId = userId, Kind = PublicProfileConsent.Kind, TextVersion = PublicProfileConsent.TextVersion, GivenAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await events.RecordAsync(dbTransaction, "profile.public-on", userId, actor, null, null, cancellationToken);
        }
        else if (!change.Public && isPublic)
        {
            await db.SaveChangesAsync(cancellationToken);
            await SwitchOffAsync(profile, now, dbTransaction, cancellationToken);
            await events.RecordAsync(dbTransaction, "profile.public-off", userId, actor, null, null, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await StateAsync(profile, cancellationToken);
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        if (await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) is not { IsPending: false } profile)
        {
            return Error.Unauthorized("auth.required");
        }

        if (profile.PublicProfileId is not null)
        {
            var now = clock.GetUtcNow();
            await SwitchOffAsync(profile, now, transaction.GetDbTransaction(), cancellationToken);
            await events.RecordAsync(transaction.GetDbTransaction(), "profile.public-off", userId, new Actor(userId, Roles.Member, ipHash), null, null, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await StateAsync(profile, cancellationToken);
    }

    /// <summary>
    /// Switching off (design 8.25.1), in the caller's transaction: the consent withdrawn, the id retired for good (410), every
    /// follow of it ended (no follower is told). Also used when the account is deleted.
    /// </summary>
    public async Task SwitchOffAsync(Profile profile, DateTimeOffset now, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
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
        await follows.EndAsync(id, transaction, cancellationToken);
    }

    /// <summary>A random id never issued before: not in use and not retired.</summary>
    private async Task<string> FreshIdAsync(CancellationToken cancellationToken)
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

    private async Task<MyProfileState> StateAsync(Profile profile, CancellationToken cancellationToken) =>
        new(
            profile.About,
            profile.AboutHiddenAt is not null,
            profile.AboutHiddenStatement,
            profile.PublicProfileId,
            profile.PublicSince,
            PublicProfileConsent.TextVersion,
            await settings.GetAsync(SettingKeys.TextsMaxLength, cancellationToken),
            profile.PublicProfileId is { } id ? MemberAddress.Page(id, profile.DisplayName) : null);
}
