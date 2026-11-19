using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Profiles;

/// <summary>
/// Reports of a public profile and what moderators do with "Apie mane" (design 8.25.6): hide with a statement emailed to the
/// person, "Palikti", restore while not replaced. Audited as <c>moderation.profile.*</c>, never with the text; reporters are
/// never shown. A context of its own per operation.
/// </summary>
internal sealed class ProfileModerationHandlers(IDbContextFactory<IdentityDb> contexts, IParticipants participants, IOutbox outbox, IAuditLog audit, TimeProvider clock)
{
    private const string Open = "open";

    public async Task<Result<Unit>> ReportAsync(Guid reporterUserId, string profileId, string ground, string? text, CancellationToken cancellationToken)
    {
        if (!ProfileReportGrounds.All.Contains(ground))
        {
            return Error.Validation("report.ground", "ground");
        }

        var note = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (note is { Length: > ProfileReportGrounds.TextMaxLength })
        {
            return Error.Validation("report.too-long", "text");
        }

        if (!ProfileRules.IsWellFormedId(profileId))
        {
            return Error.NotFound("member.not-found");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await db.Profiles.AsNoTracking().Where(p => p.PublicProfileId == profileId).Select(p => (Guid?)p.UserId).FirstOrDefaultAsync(cancellationToken);
        if (target is not { } targetUserId)
        {
            return Error.NotFound("member.not-found");
        }

        if (await participants.FindAsync(reporterUserId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        // Owner, 2026-11-17: nobody reports their own text, their own profile included.
        if (targetUserId == reporterUserId)
        {
            return Error.Forbidden("report.own");
        }

        if (await db.ProfileReports.AnyAsync(r => r.TargetUserId == targetUserId && r.ReporterUserId == reporterUserId && r.Status == Open, cancellationToken))
        {
            return Result.Ok();
        }

        db.ProfileReports.Add(new ProfileReport
        {
            Id = Ids.New(clock),
            TargetUserId = targetUserId,
            ReporterUserId = reporterUserId,
            Ground = ground,
            Text = note,
            CreatedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same person reported the same profile twice at once: one report stands.
        }

        return Result.Ok();
    }

    public async Task<IReadOnlyList<ProfileModerationItem>> QueueAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var reports = await db.ProfileReports.AsNoTracking().Where(r => r.Status == Open).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var reported = reports.Select(r => r.TargetUserId).Distinct().ToList();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => reported.Contains(p.UserId) || p.AboutHiddenAt != null)
            .ToListAsync(cancellationToken);
        return profiles
            .OrderBy(p => reported.Contains(p.UserId) ? 0 : 1)
            .ThenByDescending(p => p.AboutHiddenAt)
            .Select(p => new ProfileModerationItem(
                p.UserId,
                p.PublicProfileId,
                p.DisplayName,
                p.About,
                p.AboutHiddenAt,
                p.AboutHiddenStatement,
                ProfileRules.MayRestore(p.AboutHiddenAt, p.AboutChangedAt),
                reports.Where(r => r.TargetUserId == p.UserId).Select(r => new ProfileReportInfo(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList()))
            .ToList();
    }

    public Task<Result<Unit>> HideAboutAsync(Guid id, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        ActAsync(id, statement, required: true, moderator, "hidden", ProfileNotices.AboutHidden, contact, (profile, now) =>
        {
            if (profile.AboutHiddenAt is not null || ProfileRules.About(profile.About) is null)
            {
                return Error.Conflict("moderation.wrong-state");
            }

            profile.AboutHiddenAt = now;
            profile.AboutHiddenStatement = statement.Trim();
            return null;
        }, cancellationToken);

    public Task<Result<Unit>> KeepAsync(Guid id, string? statement, StaffActor moderator, CancellationToken cancellationToken) =>
        ActAsync(id, statement, required: false, moderator, "kept", null, null, (_, _) => null, cancellationToken);

    public Task<Result<Unit>> RestoreAboutAsync(Guid id, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        ActAsync(id, statement, required: true, moderator, "restored", ProfileNotices.AboutRestored, contact, (profile, _) =>
        {
            if (!ProfileRules.MayRestore(profile.AboutHiddenAt, profile.AboutChangedAt))
            {
                return Error.Conflict("moderation.wrong-state");
            }

            profile.AboutHiddenAt = null;
            profile.AboutHiddenStatement = null;
            return null;
        }, cancellationToken);

    /// <summary>One moderator's action on a profile: the statement checked, the change, the open reports closed, audited, told.</summary>
    private async Task<Result<Unit>> ActAsync(
        Guid id,
        string? statement,
        bool required,
        StaffActor moderator,
        string action,
        string? notice,
        string? contact,
        Func<Profile, DateTimeOffset, Error?> change,
        CancellationToken cancellationToken)
    {
        if (StaffStatements.Check(statement, required) is { } refused)
        {
            return refused;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, id, cancellationToken);
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == id, cancellationToken);
        if (profile is null)
        {
            return Error.NotFound("member.not-found");
        }

        var now = clock.GetUtcNow();
        if (change(profile, now) is { } wrong)
        {
            return wrong;
        }

        await db.SaveChangesAsync(cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        // Owner, 2026-11-17 (DSA art. 16(5)): each member whose report was open is told the decision, with the explanation.
        var reporters = await (from r in db.ProfileReports.AsNoTracking()
                               join u in db.Users.AsNoTracking() on r.ReporterUserId equals u.Id
                               where r.TargetUserId == id && r.Status == Open
                               select u.Email).Distinct().ToListAsync(cancellationToken);
        var outcome = action == "hidden" ? ReportOutcomes.Hidden : ReportOutcomes.Kept;
        var path = ReportOutcomes.StillShown(outcome) && profile.PublicProfileId is { } profileId ? MemberAddress.Page(profileId, profile.DisplayName) : null;
        foreach (var reporter in reporters.OfType<string>())
        {
            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.ReportDecidedEmail, new ReportDecidedEmail(
                    reporter, "profile", outcome, null, null, profile.DisplayName, path, string.IsNullOrWhiteSpace(statement) ? null : statement.Trim())),
                dbTransaction,
                cancellationToken);
        }

        await db.ProfileReports.Where(r => r.TargetUserId == id && r.Status == Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "closed").SetProperty(r => r.ClosedAt, now), cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(
                "moderation.profile." + action,
                moderator.UserId,
                moderator.Role,
                id.ToString(),
                System.Text.Json.JsonSerializer.SerializeToElement(new { statement = statement?.Trim() }, System.Text.Json.JsonSerializerOptions.Web),
                moderator.IpHash),
            dbTransaction,
            cancellationToken);
        if (notice is not null && await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken) is { } address)
        {
            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.ProfileNoticeEmail, new ProfileNoticeEmail(address, notice, statement?.Trim(), contact, AccountScreensPath)),
                dbTransaction,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Where the notice sends the person: their "Profilis" screen, where "Apie mane" is written.</summary>
    private const string AccountScreensPath = AccountScreens.Profile;
}
