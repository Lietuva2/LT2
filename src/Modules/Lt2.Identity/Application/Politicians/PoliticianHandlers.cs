using System.Data.Common;
using System.Security.Claims;
using Lt2.Identity.Application.Passkeys;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application.Politicians;

/// <summary>
/// MPs on the platform (design 8.5.1 to 8.5.4). The official address is read through <see cref="IMpDirectory"/> only; the
/// notices go to the MP's account's own address, which was the official address when the profile was taken over, so no
/// address leaves the Parliament module. Nothing here logs an address, a token or who is linked to whom. The reads a page's
/// components make (the office, the MPs taken over, who speaks for an MP) take a context of their own per call; the changes
/// use the request's context, which the account's passkeys (<see cref="UserManager{TUser}"/>) and the account's deletion
/// write in the same transaction.
/// </summary>
internal sealed class PoliticianHandlers(
    IdentityDb db,
    IDbContextFactory<IdentityDb> contexts,
    UserManager<User> users,
    SignInManager<User> signIn,
    PoliticianAccess access,
    StaffAccess staff,
    AccountEvents events,
    IMpDirectory mps,
    IPositionErasure positions,
    Profiles.ProfileHandlers profiles,
    IOutbox outbox,
    IOfficeCopiesEnd officeCopies,
    TimeProvider clock,
    ILogger<PoliticianHandlers> logger) : IPoliticians
{
    /// <summary>The page the take-over link opens; the token follows the <c>#</c> (design 6.8).</summary>
    public const string TakeoverPage = "/seimo-narys/perimti";

    /// <summary>The page an office invitation opens; the code follows the <c>#</c> (design 6.8), or is typed there (design 8.5.3).</summary>
    public const string OfficeInvitationPage = "/seimo-narys/biuras";

    internal static readonly Error NotOfficial = Error.Forbidden("mp.not-official-address");
    internal static readonly Error LinkInvalid = Error.Validation("mp.link-invalid", "token");
    internal static readonly Error AlreadyLinked = Error.Conflict("mp.already-linked");
    internal static readonly Error Taken = Error.Conflict("mp.taken");
    internal static readonly Error LinkLimit = new(ErrorKind.TooManyRequests, "mp.link-limit");
    internal static readonly Error NotLinked = Error.NotFound("mp.not-linked");
    internal static readonly Error NotMp = Error.Forbidden("mp.not-mp");
    internal static readonly Error OfficeFull = Error.Conflict("mp.office-full");
    internal static readonly Error MemberNotFound = Error.NotFound("mp.office-member-not-found");
    internal static readonly Error OfficeInvitationLimit = new(ErrorKind.TooManyRequests, "mp.office-invitation-limit");
    private static readonly Error MemberRequired = Error.Forbidden("member.required");
    private const string PasskeyName = "Seimo nario prieigos raktas";

    /// <summary>The name of a passkey made while accepting an office invitation (owner, 2026-11-17).</summary>
    private const string OfficePasskeyName = "Prieigos raktas";

    /// <summary>How an office invitation's acceptance shows a passkey (design 8.5.3, owner 2026-11-17).</summary>
    private enum OfficeProof
    {
        /// <summary>The session is a passkey session already.</summary>
        Session,

        /// <summary>A passkey made now for the account, stored with the acceptance.</summary>
        NewPasskey,

        /// <summary>An assertion with one of the account's passkeys.</summary>
        Passkey,
    }

    public async Task<Result<Unit>> ClaimAsync(ClaimsPrincipal principal, string slug, string? ipHash, CancellationToken cancellationToken)
    {
        if (await MemberAsync(principal, cancellationToken) is not { } user)
        {
            return MemberRequired;
        }

        // The link goes only to the official address, which must be the account's own (design 8.5.2, step 1).
        var mp = await mps.OfficialAddressOwnerAsync(user.Email!, cancellationToken);
        if (mp is null || !string.Equals(mp.Slug, slug, StringComparison.Ordinal))
        {
            return NotOfficial;
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.MpAsync(db, mp.SeimasPersonId, cancellationToken);
        if (await LinkConflictAsync(user.Id, mp.SeimasPersonId, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var since = now - TimeSpan.FromDays(1);
        var sent = await db.PoliticianTokens.CountAsync(
            t => t.Kind == PoliticianTokenKinds.Takeover && t.SeimasPersonId == mp.SeimasPersonId && t.CreatedAt > since, cancellationToken);
        if (!PoliticianRules.MaySendTakeoverLink(sent))
        {
            return LinkLimit;
        }

        // A new link replaces the old one.
        await db.PoliticianTokens
            .Where(t => t.Kind == PoliticianTokenKinds.Takeover && t.SeimasPersonId == mp.SeimasPersonId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);
        // Owner, 2026-11-17: the link's secret is a typeable one-time code, also typed on the take-over page.
        var token = OneTimeCode.New();
        db.PoliticianTokens.Add(new PoliticianToken
        {
            Id = Ids.New(clock),
            Kind = PoliticianTokenKinds.Takeover,
            SeimasPersonId = mp.SeimasPersonId,
            UserId = user.Id,
            TokenHash = OneTimeCode.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + PoliticianRules.TakeoverLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        await events.RecordAsync(transaction.GetDbTransaction(), "mp.takeover-link-sent", user.Id, new Actor(user.Id, Roles.Member, ipHash), new { mp = mp.SeimasPersonId }, null, cancellationToken);
        await outbox.EnqueueAsync(
            OutboxMessage.Create(
                MessageKinds.MpTakeoverLinkEmail,
                new MpTakeoverLinkEmail(user.Email!, Name(mp), TakeoverPage + "#" + token, (int)PoliticianRules.TakeoverLifetime.TotalHours, TakeoverPage, token)),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<SignInOutcome>> SignInWithRegistrationLinkAsync(string token, CancellationToken cancellationToken)
    {
        var hash = OneTimeCode.Hash(token);
        var found = await db.PoliticianTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (PoliticianRules.CheckRegistrationSignIn(found, clock.GetUtcNow()) != PoliticianTokenCheck.Valid)
        {
            return LinkInvalid;
        }

        // Only while the registration is not completed: a member signs in with a code or a passkey (design 8.5.2). A locked account
        // is refused as any wrong link is (design 8.3).
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == found!.UserId, cancellationToken);
        var user = await users.FindByIdAsync(found!.UserId.ToString());
        if (profile is not { IsPending: true, LockedAt: null } || user is null || !await signIn.CanSignInAsync(user))
        {
            return LinkInvalid;
        }

        // The link proves the address, as the first correct code does (design 6.2); the account stays pending until completed.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            profile.VerificationLevel = VerificationLevels.Email;
            await users.UpdateAsync(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SignInOutcome(await SessionSignIn.SignInAsync(signIn, user, AuthenticationMethods.EmailCode));
    }

    public async Task<Result<MpLinkState>> TakeOverAsync(ClaimsPrincipal principal, string token, string credentialJson, string? ipHash, CancellationToken cancellationToken)
    {
        if (await MemberAsync(principal, cancellationToken) is not { } user)
        {
            return MemberRequired;
        }

        var now = clock.GetUtcNow();
        var hash = OneTimeCode.Hash(token);
        var found = await db.PoliticianTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (PoliticianRules.Check(found, PoliticianTokenKinds.Takeover, user.Id, now) != PoliticianTokenCheck.Valid)
        {
            return LinkInvalid;
        }

        // The address must still be that MP's official address (design 8.5.2, step 3).
        var mp = await mps.OfficialAddressOwnerAsync(user.Email!, cancellationToken);
        if (mp is null || mp.SeimasPersonId != found!.SeimasPersonId)
        {
            return NotOfficial;
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "Take-over passkey attestation without a pending challenge");
            return PasskeyHandlers.NotAdded;
        }

        if (!attestation.Succeeded || attestation.UserEntity.Id != user.Id.ToString())
        {
            logger.LogInformation("Take-over passkey attestation failed: {Reason}", attestation.Failure?.Message);
            return PasskeyHandlers.NotAdded;
        }

        var passkeyId = attestation.Passkey.CredentialId;
        int removed;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await IdentityLocks.MpAsync(db, mp.SeimasPersonId, cancellationToken);
            var used = await db.PoliticianTokens
                .Where(t => t.Id == found.Id && t.UsedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);
            if (used == 0)
            {
                return LinkInvalid;
            }

            if (await LinkConflictAsync(user.Id, mp.SeimasPersonId, cancellationToken) is { } conflict)
            {
                return conflict;
            }

            // The new passkey becomes the account's only one; the sessions signed in with the others end with them.
            attestation.Passkey.Name = PasskeyName;
            if (!(await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey)).Succeeded)
            {
                return PasskeyHandlers.NotAdded;
            }

            removed = await db.Set<IdentityUserPasskey<Guid>>()
                .Where(p => p.UserId == user.Id && p.CredentialId != passkeyId)
                .ExecuteDeleteAsync(cancellationToken);

            // This session is left to the sign-in below, which ends it and starts the new passkey's session under a new key.
            var current = Guid.TryParse(principal.FindFirstValue(SessionTicketStore.SessionIdClaim), out var sessionId) ? sessionId : Guid.Empty;
            var sessionsEnded = await db.Sessions
                .Where(s => s.UserId == user.Id && s.Id != current && s.PasskeyId != null && s.PasskeyId != passkeyId)
                .ExecuteDeleteAsync(cancellationToken);

            // A lost passkey is replaced the same way (design 8.5.2, step 4): the link stays.
            var existing = await PoliticianAccess.OpenLinks(db).AnyAsync(l => l.UserId == user.Id, cancellationToken);
            if (!existing)
            {
                db.PoliticianLinks.Add(new PoliticianLink
                {
                    Id = Ids.New(clock),
                    UserId = user.Id,
                    SeimasPersonId = mp.SeimasPersonId,
                    Part = PoliticianParts.Mp,
                    VerifiedBy = "takeover-link",
                    CreatedAt = now,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            // The MP's own account takes no part (design 8.5.4): its sides go, and the counts with them.
            var sides = await positions.WithdrawOpenSidesAsync(user.Id, transaction.GetDbTransaction(), cancellationToken);

            // Nor a public profile (design 8.25.1, 8.5.4: the MP's own account cannot switch one on): one already on goes off as
            // switching it off does, the consent withdrawn, the id retired, every follow of it ended.
            var profileOff = false;
            if (await db.Profiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken) is { PublicProfileId: not null } publicProfile)
            {
                await profiles.SwitchOffAsync(db, publicProfile, now, transaction.GetDbTransaction(), cancellationToken);
                profileOff = true;
            }

            await events.RecordAsync(
                transaction.GetDbTransaction(),
                existing ? "mp.passkey-replaced" : "mp.linked",
                user.Id,
                new Actor(user.Id, Roles.Member, ipHash),
                new { mp = mp.SeimasPersonId, credential = WebEncoders.Base64UrlEncode(passkeyId)[..12], passkeysRemoved = removed, sessionsEnded, sidesWithdrawn = sides, profileSwitchedOff = profileOff },
                new AccountNoticeEmail(user.Email!, AccountNotices.MpLinked, PasskeysRemoved: removed, MpName: Name(mp)),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // The session becomes a passkey session under a new key (the old one ends with the new sign-in).
        await SessionSignIn.SignInAsync(signIn, user, AuthenticationMethods.Passkey, passkeyId);
        return new MpLinkState(mp.Slug, mp.FirstName, mp.LastName, PoliticianParts.Mp, true);
    }

    public async Task<Result<Unit>> EndOwnAsync(ClaimsPrincipal principal, string? ipHash, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state?.Link is not { } link)
        {
            return NotLinked;
        }

        if (!state.MayWrite)
        {
            return PoliticianAccess.PasskeyRequired;
        }

        var now = clock.GetUtcNow();
        var facts = await FactsAsync(link.SeimasPersonId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.MpAsync(db, link.SeimasPersonId, cancellationToken);
        var actor = new Actor(state.UserId, Roles.Member, ipHash);
        if (link.Part == PoliticianParts.Mp)
        {
            if (await EndAsync(link.Id, state.UserId, "given-up", now, cancellationToken) == 0)
            {
                return NotLinked;
            }

            var office = await EndOfficeAsync(link.SeimasPersonId, state.UserId, now, cancellationToken);
            await events.RecordAsync(
                transaction.GetDbTransaction(), "mp.given-up", state.UserId, actor, new { mp = link.SeimasPersonId, officeEnded = office },
                new AccountNoticeEmail(await EmailOfAsync(state.UserId, cancellationToken), AccountNotices.MpGivenUp, MpName: facts), cancellationToken);
        }
        else
        {
            if (await EndAsync(link.Id, state.UserId, "left", now, cancellationToken) == 0)
            {
                return NotLinked;
            }

            await events.RecordAsync(
                transaction.GetDbTransaction(), "mp.office-left", state.UserId, actor, new { mp = link.SeimasPersonId },
                await MpNoticeAsync(link.SeimasPersonId, AccountNotices.MpOfficeLeft, facts, null, cancellationToken), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<MpOffice>> OfficeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state?.Link is null)
        {
            return NotLinked;
        }

        if (!state.IsMp)
        {
            return NotMp;
        }

        // Reading is open to a code session too (design 8.5.4: it can read everything of its own).
        await using var reads = await contexts.CreateDbContextAsync(cancellationToken);
        var members = await (from l in PoliticianAccess.OpenLinks(reads).AsNoTracking()
                             join p in reads.Profiles.AsNoTracking() on l.UserId equals p.UserId
                             where l.SeimasPersonId == state.Link.SeimasPersonId && l.Part == PoliticianParts.Office
                             orderby l.CreatedAt
                             select new OfficeMember(l.Id, p.DisplayName, l.CreatedAt))
            .ToListAsync(cancellationToken);
        return new MpOffice(members, PoliticianRules.OfficeMembersPerMp);
    }

    public async Task<Result<OfficeInvitation>> InviteOfficeMemberAsync(ClaimsPrincipal principal, string? email, string? ipHash, CancellationToken cancellationToken)
    {
        if (await MpWriterAsync(principal, cancellationToken) is not { } state)
        {
            return await RefusalAsync(principal, cancellationToken);
        }

        // Owner, 2026-11-17: the site may send the invitation. The address is used only to send it: it goes into the outbox row
        // and nowhere else (not on the invitation, not in the audit).
        var to = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (to is not null && EmailAddress.Normalize(to) is null)
        {
            return Error.Validation("email.invalid", "email");
        }

        var now = clock.GetUtcNow();
        var person = state.Link!.SeimasPersonId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.MpAsync(db, person, cancellationToken);
        if (!PoliticianRules.MayAddOfficeMember(await OfficeCountAsync(person, cancellationToken)))
        {
            return OfficeFull;
        }

        var since = now - TimeSpan.FromDays(1);
        var made = await db.PoliticianTokens.CountAsync(
            t => t.Kind == PoliticianTokenKinds.Office && t.SeimasPersonId == person && t.CreatedAt > since, cancellationToken);
        if (!PoliticianRules.MayMakeOfficeInvitation(made))
        {
            return OfficeInvitationLimit;
        }

        // Design 8.5.3 (owner, 2026-11-17): the invitation's one secret is a code that can be typed as well as followed.
        var code = OneTimeCode.New();
        var invitation = new PoliticianToken
        {
            Id = Ids.New(clock),
            Kind = PoliticianTokenKinds.Office,
            SeimasPersonId = person,
            UserId = state.UserId,
            TokenHash = OneTimeCode.Hash(code),
            CreatedAt = now,
            ExpiresAt = now + PoliticianRules.OfficeInvitationLifetime,
        };
        db.PoliticianTokens.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        await events.RecordAsync(
            transaction.GetDbTransaction(), "mp.office-invited", state.UserId, new Actor(state.UserId, Roles.Member, ipHash), new { mp = person, emailed = to is not null }, null, cancellationToken);
        if (to is not null)
        {
            await outbox.EnqueueAsync(
                OutboxMessage.Create(
                    MessageKinds.MpOfficeInvitationEmail,
                    new MpOfficeInvitationEmail(to, await FactsAsync(person, cancellationToken) ?? "", OfficeInvitationPage + "#" + code, OfficeInvitationPage, code, invitation.ExpiresAt)),
                transaction.GetDbTransaction(),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new OfficeInvitation(code, invitation.ExpiresAt, to is not null);
    }

    public async Task<Result<MpLinkState>> AcceptOfficeInvitationAsync(
        ClaimsPrincipal principal, string token, string? newPasskey, string? passkey, string? ipHash, CancellationToken cancellationToken)
    {
        if (await MemberAsync(principal, cancellationToken) is not { } user)
        {
            return MemberRequired;
        }

        // A member accepts in a passkey session (design 8.5.3): their passkeys then change only in one. Owner, 2026-11-17: a
        // code session brings the passkey in this request, a new one or one the account has, and becomes a passkey session.
        var state = await access.ForAsync(principal, cancellationToken);
        if (state is null)
        {
            return MemberRequired;
        }

        var proof = !string.IsNullOrEmpty(newPasskey) ? OfficeProof.NewPasskey : !string.IsNullOrEmpty(passkey) ? OfficeProof.Passkey : OfficeProof.Session;
        if (proof == OfficeProof.Session && !state.PasskeySession)
        {
            return PoliticianAccess.PasskeyRequired;
        }

        var now = clock.GetUtcNow();
        var hash = OneTimeCode.Hash(token);
        var found = await db.PoliticianTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (PoliticianRules.Check(found, PoliticianTokenKinds.Office, state.UserId, now) != PoliticianTokenCheck.Valid)
        {
            return LinkInvalid;
        }

        // Nothing is attested or stored for an account that cannot become an office member anyway.
        if (state.Link is not null)
        {
            return AlreadyLinked;
        }

        PasskeyAttestationResult? attestation = null;
        byte[]? sessionPasskey = null;
        if (proof == OfficeProof.NewPasskey)
        {
            // The staff rule of 6.2: an account with an active staff role adds a passkey only in its staff session.
            if ((await staff.ForAsync(principal, cancellationToken))?.AccountHasActiveStaffRole == true)
            {
                return StaffHandlers.StaffPasskeyRequired;
            }

            try
            {
                attestation = await signIn.PerformPasskeyAttestationAsync(newPasskey!);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogInformation(ex, "Office passkey attestation without a pending challenge");
                return PasskeyHandlers.NotAdded;
            }

            if (!attestation.Succeeded || attestation.UserEntity.Id != user.Id.ToString())
            {
                logger.LogInformation("Office passkey attestation failed: {Reason}", attestation.Failure?.Message);
                return PasskeyHandlers.NotAdded;
            }

            sessionPasskey = attestation.Passkey.CredentialId;
        }
        else if (proof == OfficeProof.Passkey)
        {
            PasskeyAssertionResult<User> assertion;
            try
            {
                assertion = await signIn.PerformPasskeyAssertionAsync(passkey!);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogInformation(ex, "Office passkey assertion without a pending challenge");
                return PasskeyHandlers.InvalidPasskey;
            }

            if (!assertion.Succeeded || assertion.User.Id != user.Id || !await signIn.CanSignInAsync(assertion.User))
            {
                logger.LogInformation("Office passkey assertion refused: {Reason}", assertion.Failure?.Message ?? "not this account's passkey");
                return PasskeyHandlers.InvalidPasskey;
            }

            // The new signature counter must be stored, or the assertion does not count (as at sign-in).
            if (!(await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey)).Succeeded)
            {
                return PasskeyHandlers.InvalidPasskey;
            }

            sessionPasskey = assertion.Passkey.CredentialId;
        }

        var person = found!.SeimasPersonId;
        var facts = await mps.MpsAsync([person], cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.MpAsync(db, person, cancellationToken);

        // The invitation works only while the account that made it is still the MP's.
        var mpAccount = await PoliticianAccess.OpenLinks(db).AsNoTracking()
            .Where(l => l.SeimasPersonId == person && l.Part == PoliticianParts.Mp)
            .Select(l => (Guid?)l.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (mpAccount != found.UserId)
        {
            return LinkInvalid;
        }

        // At most one MP per account, and the MP's own account is of none (design 8.5.3).
        if (await PoliticianAccess.OpenLinks(db).AnyAsync(l => l.UserId == state.UserId, cancellationToken))
        {
            return AlreadyLinked;
        }

        if (!PoliticianRules.MayAddOfficeMember(await OfficeCountAsync(person, cancellationToken)))
        {
            return OfficeFull;
        }

        if (await db.PoliticianTokens.Where(t => t.Id == found.Id && t.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken) == 0)
        {
            return LinkInvalid;
        }

        // A passkey made now is stored in the same transaction, before the link: on an account not yet linked a code session
        // may add one (8.5.3), and the account is told as for any added passkey.
        if (attestation?.Passkey is { } added)
        {
            added.Name = OfficePasskeyName;
            if (!(await users.AddOrUpdatePasskeyAsync(user, added)).Succeeded)
            {
                return PasskeyHandlers.NotAdded;
            }

            await events.RecordAsync(
                transaction.GetDbTransaction(), "passkey.added", user.Id, new Actor(user.Id, Roles.Member, ipHash),
                new { credential = WebEncoders.Base64UrlEncode(added.CredentialId)[..12], officeInvitation = true },
                new AccountNoticeEmail(user.Email!, AccountNotices.PasskeyAdded), cancellationToken);
        }

        db.PoliticianLinks.Add(new PoliticianLink
        {
            Id = Ids.New(clock),
            UserId = state.UserId,
            SeimasPersonId = person,
            Part = PoliticianParts.Office,
            VerifiedBy = "office-invitation",
            AddedByUserId = found.UserId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        var name = facts.TryGetValue(person, out var mp) ? Name(mp) : null;
        await events.RecordAsync(
            transaction.GetDbTransaction(), "mp.office-added", state.UserId, new Actor(state.UserId, Roles.Member, ipHash), new { mp = person },
            await MpNoticeAsync(person, AccountNotices.MpOfficeAdded, name, null, cancellationToken), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // A code session that brought its passkey becomes a passkey session under a new key (as a staff session does, 6.2):
        // the code session's row is deleted and a passkey session stored.
        if (sessionPasskey is not null)
        {
            await SessionSignIn.SignInAsync(signIn, user, AuthenticationMethods.Passkey, sessionPasskey);
        }

        return mp is null
            ? new MpLinkState("", "", "", PoliticianParts.Office, true)
            : new MpLinkState(mp.Slug, mp.FirstName, mp.LastName, PoliticianParts.Office, true);
    }

    public async Task<Result<Unit>> RemoveOfficeMemberAsync(ClaimsPrincipal principal, Guid memberId, string? ipHash, CancellationToken cancellationToken)
    {
        if (await MpWriterAsync(principal, cancellationToken) is not { } state)
        {
            return await RefusalAsync(principal, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var person = state.Link!.SeimasPersonId;
        var facts = await FactsAsync(person, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.MpAsync(db, person, cancellationToken);
        var member = await PoliticianAccess.OpenLinks(db).AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == memberId && l.SeimasPersonId == person && l.Part == PoliticianParts.Office, cancellationToken);
        if (member is null || await EndAsync(member.Id, state.UserId, "removed", now, cancellationToken) == 0)
        {
            return MemberNotFound;
        }

        await events.RecordAsync(
            transaction.GetDbTransaction(), "mp.office-removed", member.UserId, new Actor(state.UserId, Roles.Member, ipHash), new { mp = person },
            await MpNoticeAsync(person, AccountNotices.MpOfficeRemoved, facts, null, cancellationToken), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> EndByAdministratorAsync(string slug, string statement, StaffActor administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (StaffStatements.Check(statement) is { } broken)
        {
            return broken;
        }

        if (await mps.FindMpAsync(slug, cancellationToken) is not { } mp)
        {
            return Error.NotFound("mp.not-found");
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.MpAsync(db, mp.SeimasPersonId, cancellationToken);
        var link = await PoliticianAccess.OpenLinks(db).AsNoTracking()
            .FirstOrDefaultAsync(l => l.SeimasPersonId == mp.SeimasPersonId && l.Part == PoliticianParts.Mp, cancellationToken);
        if (link is null)
        {
            return NotLinked;
        }

        // The notice goes before the link ends: it is addressed to the MP's account.
        var notice = await MpNoticeAsync(mp.SeimasPersonId, AccountNotices.MpLinkEnded, Name(mp), statement.Trim(), cancellationToken);
        await EndAsync(link.Id, administrator.UserId, "administrator", now, cancellationToken);
        var office = await EndOfficeAsync(mp.SeimasPersonId, administrator.UserId, now, cancellationToken);
        await events.RecordAsync(
            transaction.GetDbTransaction(), "mp.link-ended", link.UserId, new Actor(administrator.UserId, administrator.Role, administrator.IpHash),
            new { mp = mp.SeimasPersonId, officeEnded = office, statement = statement.Trim() }, notice, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<IReadOnlyDictionary<long, DateTimeOffset>> TakenOverAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken)
    {
        var ids = seimasPersonIds.Distinct().ToList();
        await using var reads = await contexts.CreateDbContextAsync(cancellationToken);
        return await PoliticianAccess.OpenLinks(reads).AsNoTracking()
            .Where(l => l.Part == PoliticianParts.Mp && ids.Contains(l.SeimasPersonId))
            .ToDictionaryAsync(l => l.SeimasPersonId, l => l.CreatedAt, cancellationToken);
    }

    public async Task<IReadOnlyList<LinkedMp>> LinkedAsync(CancellationToken cancellationToken)
    {
        await using var reads = await contexts.CreateDbContextAsync(cancellationToken);
        return (await PoliticianAccess.OpenLinks(reads).AsNoTracking()
                .Where(l => l.Part == PoliticianParts.Mp)
                .Select(l => new { l.SeimasPersonId, l.CreatedAt })
                .ToListAsync(cancellationToken))
            .OrderBy(l => l.CreatedAt)
            .Select(l => new LinkedMp(l.SeimasPersonId, l.CreatedAt))
            .ToList();
    }

    public async Task<MpSpeaker?> SpeakerAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        return state?.Link is { } link ? new MpSpeaker(state.UserId, link.SeimasPersonId, link.Part, state.MayWrite) : null;
    }

    /// <summary>
    /// Design 8.5.2: deleting the account ends its link the same way, in the deletion's transaction (after the outbox forgot
    /// the account's unsent mail, so the notice is the last email to it). The rows go with the account.
    /// </summary>
    public async Task OnAccountDeletedAsync(Guid userId, string? address, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var link = await PoliticianAccess.OpenLinks(db).AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var facts = await FactsAsync(link.SeimasPersonId, cancellationToken);
        await IdentityLocks.MpAsync(db, link.SeimasPersonId, cancellationToken);
        var actor = new Actor(userId, Roles.Member);
        if (link.Part == PoliticianParts.Mp)
        {
            var office = await EndOfficeAsync(link.SeimasPersonId, userId, now, cancellationToken);
            await events.RecordAsync(
                transaction, "mp.given-up", userId, actor, new { mp = link.SeimasPersonId, officeEnded = office, reason = "account-deleted" },
                address is null ? null : new AccountNoticeEmail(address, AccountNotices.MpGivenUp, MpName: facts), cancellationToken);
        }
        else
        {
            await events.RecordAsync(
                transaction, "mp.office-left", userId, actor, new { mp = link.SeimasPersonId, reason = "account-deleted" },
                await MpNoticeAsync(link.SeimasPersonId, AccountNotices.MpOfficeLeft, facts, null, cancellationToken), cancellationToken);
        }
    }

    /// <summary>
    /// The account's offer or link for <c>GET /me</c> (design 8.5.4): only for a member whose registration is complete.
    /// </summary>
    public async Task<(MpOffer? Offer, MpLinkState? Link)> MineAsync(ClaimsPrincipal principal, User user, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state?.Link is { } link)
        {
            var facts = await mps.MpsAsync([link.SeimasPersonId], cancellationToken);
            return facts.TryGetValue(link.SeimasPersonId, out var mp)
                ? (null, new MpLinkState(mp.Slug, mp.FirstName, mp.LastName, link.Part, state.MayWrite))
                : (null, null);
        }

        var owner = await mps.OfficialAddressOwnerAsync(user.Email!, cancellationToken);
        return (owner is null ? null : new MpOffer(owner.Slug, owner.FirstName, owner.LastName), null);
    }

    /// <summary>
    /// The passkey rule of a linked account (design 8.5.2, 8.5.3): adding or removing a passkey needs a passkey session; null
    /// when allowed.
    /// </summary>
    public async Task<Error?> PasskeyChangeRefusalAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        return state is null || PoliticianRules.MayChangePasskeys(state.Link is not null, state.PasskeySession) ? null : PoliticianAccess.PasskeyRequired;
    }

    private async Task<User?> MemberAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return null;
        }

        var complete = await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.RegisteredAt != null, cancellationToken);
        return complete ? await users.FindByIdAsync(userId.ToString()) : null;
    }

    /// <summary>The MP's account in a passkey session, or null.</summary>
    private async Task<PoliticianState?> MpWriterAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        return state is { IsMp: true, MayWrite: true } ? state : null;
    }

    private async Task<Error> RefusalAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        return state?.Link is null ? NotLinked : !state.IsMp ? NotMp : PoliticianAccess.PasskeyRequired;
    }

    /// <summary>One account as the MP, one MP per account (design 8.5.2): null when this account may take the person over.</summary>
    private async Task<Error?> LinkConflictAsync(Guid userId, long person, CancellationToken cancellationToken)
    {
        var own = await PoliticianAccess.OpenLinks(db).AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (own is not null && (own.Part != PoliticianParts.Mp || own.SeimasPersonId != person))
        {
            return AlreadyLinked;
        }

        var other = await PoliticianAccess.OpenLinks(db).AnyAsync(
            l => l.SeimasPersonId == person && l.Part == PoliticianParts.Mp && l.UserId != userId, cancellationToken);
        return other ? Taken : null;
    }

    private Task<int> OfficeCountAsync(long person, CancellationToken cancellationToken) =>
        PoliticianAccess.OpenLinks(db).CountAsync(l => l.SeimasPersonId == person && l.Part == PoliticianParts.Office, cancellationToken);

    private async Task<int> EndAsync(Guid linkId, Guid by, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var link = await db.PoliticianLinks.AsNoTracking().Where(l => l.Id == linkId && l.EndedAt == null)
            .Select(l => new { l.UserId, l.SeimasPersonId, l.Part }).FirstOrDefaultAsync(cancellationToken);
        var ended = await db.PoliticianLinks.Where(l => l.Id == linkId && l.EndedAt == null).ExecuteUpdateAsync(
            s => s.SetProperty(l => l.EndedAt, now).SetProperty(l => l.EndedByUserId, by).SetProperty(l => l.EndReason, reason),
            cancellationToken);
        if (ended > 0 && link is { Part: PoliticianParts.Office })
        {
            // Design 8.19.3 (owner, 2026-11-17): a member who leaves or is removed gets no more copies; their choice goes.
            await officeCopies.EndAsync(link.UserId, link.SeimasPersonId, Transaction(), cancellationToken);
        }

        return ended;
    }

    /// <summary>When the MP's link ends, the office it added ends with it; its unused invitations stop working too.</summary>
    private async Task<int> EndOfficeAsync(long person, Guid by, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.PoliticianTokens.Where(t => t.SeimasPersonId == person && t.Kind == PoliticianTokenKinds.Office && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);
        var ended = await db.PoliticianLinks.Where(l => l.SeimasPersonId == person && l.Part == PoliticianParts.Office && l.EndedAt == null).ExecuteUpdateAsync(
            s => s.SetProperty(l => l.EndedAt, now).SetProperty(l => l.EndedByUserId, by).SetProperty(l => l.EndReason, "mp-ended"),
            cancellationToken);
        await officeCopies.EndAsync(null, person, Transaction(), cancellationToken);
        return ended;
    }

    /// <summary>The transaction a link ends in; the office's copies end in it too (design 8.19.3).</summary>
    private DbTransaction Transaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction() ?? throw new InvalidOperationException("A link ends inside the change's transaction.");

    /// <summary>A notice to the MP's account (the official address when it took the profile over); null when there is none.</summary>
    private async Task<AccountNoticeEmail?> MpNoticeAsync(long person, string notice, string? mpName, string? statement, CancellationToken cancellationToken)
    {
        var address = await (from l in PoliticianAccess.OpenLinks(db).AsNoTracking()
                             join u in db.Users.AsNoTracking() on l.UserId equals u.Id
                             where l.SeimasPersonId == person && l.Part == PoliticianParts.Mp
                             select u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        return address is null ? null : new AccountNoticeEmail(address, notice, MpName: mpName, Statement: statement);
    }

    private async Task<string> EmailOfAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstAsync(cancellationToken))!;

    private async Task<string?> FactsAsync(long person, CancellationToken cancellationToken) =>
        (await mps.MpsAsync([person], cancellationToken)).TryGetValue(person, out var mp) ? Name(mp) : null;

    private static string Name(MpFacts mp) => mp.FirstName + " " + mp.LastName;
}
