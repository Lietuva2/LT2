using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// Registration by email and invitation code with the 18+ confirmation (design 6.2). The response is the same whether
/// the address is new or already registered: in both cases a sign-in code is emailed, and an existing account is not
/// changed and does not use up the invitation.
/// </summary>
internal sealed class RegisterHandler(
    IdentityDb db,
    UserManager<User> users,
    SignInCodeIssuer codes,
    ISettings settings,
    TimeProvider clock)
{
    public async Task<Result<Unit>> HandleAsync(Registration registration, CancellationToken cancellationToken)
    {
        if (!registration.ConfirmsAdult)
        {
            return Error.Validation("adult.required", "confirmsAdult");
        }

        var normalized = EmailAddress.Normalize(registration.Email);
        if (normalized is null)
        {
            return Error.Validation("email.invalid", "email");
        }

        if (DisposableDomains.Contains(normalized))
        {
            return Error.Validation("email.disposable", "email");
        }

        var now = clock.GetUtcNow();
        Invitation? invitation = null;
        if (!string.IsNullOrWhiteSpace(registration.InvitationCode))
        {
            var code = Invitation.NormalizeCode(registration.InvitationCode);
            invitation = await db.Invitations.AsNoTracking().FirstOrDefaultAsync(
                i => i.Code == code && i.UsesLeft > 0 && (i.ExpiresAt == null || i.ExpiresAt > now),
                cancellationToken);
            if (invitation is null)
            {
                return Error.Validation("invitation.invalid", "invitationCode");
            }
        }
        else if (await settings.GetAsync(SettingKeys.RegistrationRequiresInvitation, cancellationToken))
        {
            return Error.Validation("invitation.required", "invitationCode");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Profiles.AsNoTracking()
            .Where(p => p.NormalizedEmail == normalized)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing != Guid.Empty)
        {
            var existingUser = await users.FindByIdAsync(existing.ToString());
            if (existingUser is not null)
            {
                await codes.IssueAsync(existingUser, transaction, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result.Ok();
        }

        var user = new User { Id = Ids.New(clock), Email = registration.Email.Trim(), EmailConfirmed = false };
        user.UserName = user.Id.ToString("N");
        try
        {
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Creating the account failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
            }

            db.Profiles.Add(new Profile
            {
                UserId = user.Id,
                DisplayName = string.IsNullOrWhiteSpace(registration.DisplayName) ? null : registration.DisplayName.Trim(),
                NormalizedEmail = normalized,
                VerificationLevel = VerificationLevels.None,
                InvitationId = invitation?.Id,
                CreatedAt = now,
            });
            db.Consents.Add(new Consent
            {
                Id = Ids.New(clock),
                UserId = user.Id,
                Kind = ConsentKinds.Adult,
                TextVersion = ConsentKinds.AdultTextVersion,
                GivenAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);

            if (invitation is not null)
            {
                var used = await db.Invitations
                    .Where(i => i.Id == invitation.Id && i.UsesLeft > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsesLeft, i => i.UsesLeft - 1), cancellationToken);
                if (used == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Error.Validation("invitation.invalid", "invitationCode");
                }
            }

            await codes.IssueAsync(user, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent registration of the same address won; answer as for an existing account.
            await transaction.RollbackAsync(cancellationToken);
            return Result.Ok();
        }
    }
}
