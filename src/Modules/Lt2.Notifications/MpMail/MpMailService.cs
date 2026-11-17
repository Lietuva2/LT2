using System.Text.Json;
using Lt2.Notifications.Digests;
using Lt2.Notifications.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Notifications.MpMail;

/// <summary>
/// The MP's choices without an account (design 8.19.4), the introduction letter (8.19.5) and the suppression list (8.19.6).
/// Tokens are looked up by their hash only; an unknown, renewed, used or expired token is the same "not found". Every change of
/// a choice is confirmed by email to the official address (unless it is suppressed). No log line, audit detail or answer other
/// than the MP's own page carries a token, an address or a choice.
/// </summary>
internal sealed class MpMailService(
    IDbContextFactory<NotificationsDb> contexts,
    IMpDirectory mps,
    MpSender sender,
    MailKeys keys,
    MailLocks locks,
    IAuditLog audit,
    TimeProvider clock) : IMpMail, IMpMailAdministration
{
    /// <summary>How long the extra recipient's confirmation link works (design 8.19.4).</summary>
    public const int ConfirmHours = 24;

    private static readonly Error NotFound = Error.NotFound("mp-mail.not-found");

    public async Task<Result<MpMailChoice>> GetAsync(string token, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await FindAsync(db, token, TokenKinds.Preferences, cancellationToken) is { } found
            ? await ChoiceAsync(db, found.SeimasPersonId, cancellationToken)
            : NotFound;
    }

    public Task<Result<MpMailChoice>> SetFrequencyAsync(string token, string frequency, CancellationToken cancellationToken) =>
        ChangeAsync(token, null, frequency, cancellationToken);

    public async Task<Result<Unit>> StopAsync(string token, CancellationToken cancellationToken)
    {
        // The same one-click route stops an office member's copies with the member's own token (design 8.19.3), and nothing else.
        if (await StopOfficeCopiesAsync(token, cancellationToken))
        {
            return Result.Ok();
        }

        var result = await ChangeAsync(token, null, Frequencies.Stopped, cancellationToken);
        return result.IsSuccess ? Result.Ok() : result.Error!;
    }

    public Task<Result<MpMailChoice>> ForPersonAsync(long seimasPersonId, CancellationToken cancellationToken) =>
        WithDbAsync(db => ChoiceAsync(db, seimasPersonId, cancellationToken), cancellationToken);

    public Task<Result<MpMailChoice>> SetForPersonAsync(long seimasPersonId, string frequency, CancellationToken cancellationToken) =>
        ChangeAsync(null, seimasPersonId, frequency, cancellationToken);

    public async Task<Result<Unit>> RenewAsync(string token, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await FindAsync(db, token, TokenKinds.Preferences, cancellationToken, forUpdate: true) is not { } found)
        {
            return NotFound;
        }

        var person = found.SeimasPersonId;
        if (!await locks.MailMayLeaveAsync(cancellationToken))
        {
            return Error.Conflict("mp-mail.not-caught");
        }

        if (await OfficialAsync(db, person, cancellationToken) is not { } official)
        {
            // The new link could reach nobody: the old one keeps working.
            return Error.Conflict("mp-mail.no-address");
        }

        var pref = await PrefAsync(db, person, cancellationToken);
        pref.TokenGeneration++;
        pref.ChangedAt = clock.GetUtcNow();
        await db.MpMailTokens.Where(t => t.SeimasPersonId == person && t.Kind == TokenKinds.Preferences).ExecuteDeleteAsync(cancellationToken);
        var fresh = await sender.PreferencesTokenAsync(db, person, pref.TokenGeneration, cancellationToken);
        await NoticeAsync(db, official, MpMailNotices.NewLink, person, null, MpMailPaths.Preferences(fresh), null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<MpMailChoice>> AskExtraAsync(string token, string address, CancellationToken cancellationToken)
    {
        if (!SendingRules.IsAddress(address))
        {
            return Error.Validation("mp-mail.address-invalid", "address");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await FindAsync(db, token, TokenKinds.Preferences, cancellationToken, forUpdate: true) is not { } found)
        {
            return NotFound;
        }

        var person = found.SeimasPersonId;
        if (!await locks.MailMayLeaveAsync(cancellationToken))
        {
            return Error.Conflict("mp-mail.not-caught");
        }

        if (await OfficialAsync(db, person, cancellationToken) is not { } official)
        {
            return Error.Conflict("mp-mail.no-address");
        }

        // One extra address: a new one replaces the one before, which gets no copies until the new one is confirmed.
        var pref = await PrefAsync(db, person, cancellationToken);
        pref.ExtraAddress = address.Trim();
        pref.ExtraConfirmedAt = null;
        pref.ChangedAt = clock.GetUtcNow();
        await db.MpMailTokens.Where(t => t.SeimasPersonId == person && (t.Kind == TokenKinds.ExtraConfirm || t.Kind == TokenKinds.ExtraStop))
            .ExecuteDeleteAsync(cancellationToken);
        var confirm = MailKeys.RandomToken();
        db.MpMailTokens.Add(new MpMailToken
        {
            Id = Ids.New(clock),
            SeimasPersonId = person,
            Kind = TokenKinds.ExtraConfirm,
            TokenHash = MailKeys.TokenHash(confirm),
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = clock.GetUtcNow().AddHours(ConfirmHours),
        });
        await NoticeAsync(db, official, MpMailNotices.ExtraConfirm, person, null, MpMailPaths.ConfirmExtra(confirm), pref.ExtraAddress, cancellationToken, ConfirmHours);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ChoiceAsync(db, person, cancellationToken);
    }

    public async Task<Result<MpMailChoice>> RemoveExtraAsync(string token, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await FindAsync(db, token, TokenKinds.Preferences, cancellationToken, forUpdate: true) is not { } found)
        {
            return NotFound;
        }

        await RemoveExtraAsync(db, found.SeimasPersonId, Path: MpMailPaths.Preferences(token), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ChoiceAsync(db, found.SeimasPersonId, cancellationToken);
    }

    public async Task<Result<MpMailChoice>> ConfirmExtraAsync(string confirmToken, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await FindAsync(db, confirmToken, TokenKinds.ExtraConfirm, cancellationToken, forUpdate: true) is not { } found)
        {
            return NotFound;
        }

        var person = found.SeimasPersonId;
        var pref = await db.MpEmailPrefs.FirstOrDefaultAsync(p => p.SeimasPersonId == person, cancellationToken);
        if (pref is not { ExtraAddress: { } extra, ExtraConfirmedAt: null })
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        found.UsedAt = now;
        pref.ExtraConfirmedAt = now;
        pref.ExtraGeneration++;
        pref.ChangedAt = now;
        var stop = await sender.ExtraStopTokenAsync(db, person, pref.ExtraGeneration, cancellationToken);
        var preferences = await sender.PreferencesTokenAsync(db, person, pref.TokenGeneration, cancellationToken);
        if (await OfficialAsync(db, person, cancellationToken) is { } official)
        {
            await NoticeAsync(db, official, MpMailNotices.ExtraConfirmed, person, null, MpMailPaths.Preferences(preferences), extra, cancellationToken);
        }

        if (await locks.MailMayLeaveAsync(cancellationToken) && !sender.IsSuppressed(await sender.SuppressedAsync(db, [extra], cancellationToken), extra))
        {
            await NoticeAsync(db, extra, MpMailNotices.ExtraWelcome, person, null, MpMailPaths.StopCopies(stop), null, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ChoiceAsync(db, person, cancellationToken);
    }

    public async Task<Result<Unit>> StopCopiesAsync(string stopToken, CancellationToken cancellationToken)
    {
        // "Nebegauti kopijų" from an office member's copy (design 8.19.3) stops that member's copies.
        if (await StopOfficeCopiesAsync(stopToken, cancellationToken))
        {
            return Result.Ok();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await FindAsync(db, stopToken, TokenKinds.ExtraStop, cancellationToken, forUpdate: true) is not { } found)
        {
            return NotFound;
        }

        var pref = await PrefAsync(db, found.SeimasPersonId, cancellationToken);
        var preferences = await sender.PreferencesTokenAsync(db, found.SeimasPersonId, pref.TokenGeneration, cancellationToken);
        await RemoveExtraAsync(db, found.SeimasPersonId, MpMailPaths.Preferences(preferences), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public Task<Result<OfficeCopyChoice>> OfficeCopiesAsync(Guid userId, long seimasPersonId, CancellationToken cancellationToken) =>
        OfficeChoiceAsync(userId, seimasPersonId, null, cancellationToken);

    public Task<Result<OfficeCopyChoice>> SetOfficeCopiesAsync(Guid userId, long seimasPersonId, string frequency, CancellationToken cancellationToken) =>
        Frequencies.All.Contains(frequency)
            ? OfficeChoiceAsync(userId, seimasPersonId, frequency, cancellationToken)
            : Task.FromResult<Result<OfficeCopyChoice>>(Error.Validation("mp-mail.frequency-invalid", "frequency"));

    /// <summary>An office member's own choice of copies (design 8.19.3), read or, with <paramref name="frequency"/>, changed.</summary>
    private async Task<Result<OfficeCopyChoice>> OfficeChoiceAsync(Guid userId, long person, string? frequency, CancellationToken ct)
    {
        var mp = (await mps.MpsAsync([person], ct)).GetValueOrDefault(person);
        if (mp is null)
        {
            return Error.NotFound("mp.not-found");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        var choice = await db.OfficeCopies.FirstOrDefaultAsync(c => c.UserId == userId && c.SeimasPersonId == person, ct);
        if (frequency is not null && (choice?.Frequency ?? Frequencies.Default) != frequency)
        {
            if (choice is null)
            {
                choice = new OfficeCopy { UserId = userId, SeimasPersonId = person };
                db.OfficeCopies.Add(choice);
            }

            choice.Frequency = frequency;
            choice.ChangedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return new OfficeCopyChoice(mp.Slug, mp.FirstName, mp.LastName, choice?.Frequency ?? Frequencies.Default);
    }

    /// <summary>
    /// An office member's own stop (design 8.19.3), from the copy's one-click or its "Nebegauti kopijų" page: true when the token
    /// is a member's, whose copies are then stopped (their choice "Nesiųsti"); nothing else changes.
    /// </summary>
    private async Task<bool> StopOfficeCopiesAsync(string? token, CancellationToken ct)
    {
        if (!MailKeys.LooksLikeToken(token))
        {
            return false;
        }

        var hash = MailKeys.TokenHash(token!);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var choice = await db.OfficeCopies.FirstOrDefaultAsync(c => c.StopTokenHash == hash, ct);
        if (choice is null)
        {
            return false;
        }

        if (choice.Frequency != Frequencies.Stopped)
        {
            choice.Frequency = Frequencies.Stopped;
            choice.ChangedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<IntroductionState> IntroductionAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var letters = await db.IntroductionLetters.AsNoTracking().OrderByDescending(l => l.Version).ToListAsync(cancellationToken);
        var recipients = (await mps.SittingMpsAsync(cancellationToken)).Where(m => m.HasOfficialAddress).ToList();
        var plan = await IntroductionPlanAsync(db, recipients, cancellationToken);
        return new IntroductionState(
            letters.Count > 0 ? Version(letters[0]) : null,
            letters.Where(l => l.SentAt != null).Select(Version).ToList(),
            plan.Count(p => p.Status == DeliveryStatuses.Queued),
            await locks.MailMayLeaveAsync(cancellationToken));
    }

    public async Task<Result<IntroductionVersion>> SaveIntroductionAsync(IntroductionDraft draft, StaffActor administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(administrator);
        if (IntroductionRules.Check(draft) is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(DigestService.LockSql, cancellationToken);
        var latest = await db.IntroductionLetters.OrderByDescending(l => l.Version).FirstOrDefaultAsync(cancellationToken);
        if (latest is { SentAt: not null } && IntroductionRules.Same(latest, draft))
        {
            // Saved again unchanged: nothing new to send, so no new version.
            return Version(latest);
        }

        // A version that was sent is never changed: a changed text is a new version (design 8.19.5).
        var letter = latest is { SentAt: null } ? latest : new IntroductionLetter { Version = (latest?.Version ?? 0) + 1 };
        if (letter != latest)
        {
            db.IntroductionLetters.Add(letter);
        }

        letter.Subject = draft.Subject.Trim();
        letter.Text = draft.Text.Trim();
        letter.SignerName = draft.SignerName.Trim();
        letter.SignerRole = draft.SignerRole.Trim();
        letter.WrittenBy = administrator.UserId;
        letter.WrittenAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("introduction.saved", administrator.UserId, administrator.Role, "introduction:" + letter.Version, null, administrator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Version(letter);
    }

    public async Task<Result<Unit>> SendIntroductionTestAsync(string ownAddress, StaffActor administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (!await locks.MailMayLeaveAsync(cancellationToken))
        {
            return Error.Conflict("mp-mail.not-caught");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var letter = await db.IntroductionLetters.AsNoTracking().OrderByDescending(l => l.Version).FirstOrDefaultAsync(cancellationToken);
        if (letter is null)
        {
            return Error.Conflict("introduction.none");
        }

        await sender.EnqueueAsync(
            db,
            MpMailKinds.Introduction,
            new MpIntroductionEmail(ownAddress, letter.Version, letter.Subject, letter.Text, letter.SignerName, letter.SignerRole, null, null, Test: true, null),
            cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("introduction.test", administrator.UserId, administrator.Role, "introduction:" + letter.Version, null, administrator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<int>> SendIntroductionAsync(StaffActor administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (!await locks.MailMayLeaveAsync(cancellationToken))
        {
            return Error.Conflict("mp-mail.not-caught");
        }

        var recipients = (await mps.SittingMpsAsync(cancellationToken)).Where(m => m.HasOfficialAddress).ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Under the summaries' lock: one letter per MP, also when two administrators press at once (design 8.19.5).
        await db.Database.ExecuteSqlRawAsync(DigestService.LockSql, cancellationToken);
        var letter = await db.IntroductionLetters.OrderByDescending(l => l.Version).FirstOrDefaultAsync(cancellationToken);
        if (letter is null)
        {
            return Error.Conflict("introduction.none");
        }

        var plan = await IntroductionPlanAsync(db, recipients, cancellationToken);
        var queued = plan.Count(p => p.Status == DeliveryStatuses.Queued);
        if (queued == 0 && letter.SentAt is not null)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var run = new DigestRun
        {
            Id = Ids.New(clock),
            Kind = SendingRules.Introduction,
            RunDate = VilniusTime.DateOf(now),
            CreatedAt = now,
            Status = RunStatuses.Sent,
            LetterVersion = letter.Version,
            DueAt = now,
            SentAt = now,
            MpCount = queued,
            SentCount = queued,
        };
        db.DigestRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var p in plan)
        {
            db.DigestDeliveries.Add(new DigestDelivery
            {
                RunId = run.Id, MpSlug = p.Mp.Slug, SeimasPersonId = p.Mp.SeimasPersonId, Recipient = Recipients.Official, Status = p.Status,
            });
            if (p.Status == DeliveryStatuses.Queued)
            {
                await sender.EnqueueIntroductionAsync(db, letter, p.Mp, p.Address, p.Pref, run.Id, cancellationToken);
            }
        }

        letter.SentAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("introduction.sent", administrator.UserId, administrator.Role, "introduction:" + letter.Version,
                JsonSerializer.SerializeToElement(new { mps = queued }, JsonSerializerOptions.Web), administrator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return queued;
    }

    public async Task<IReadOnlyList<IntroductionVersion>> SentIntroductionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.IntroductionLetters.AsNoTracking().Where(l => l.SentAt != null).OrderByDescending(l => l.Version).ToListAsync(cancellationToken))
            .Select(Version).ToList();
    }

    public async Task<SuppressionView> SuppressionAsync(CancellationToken cancellationToken)
    {
        var recipients = (await mps.SittingMpsAsync(cancellationToken)).Where(m => m.HasOfficialAddress).ToList();
        var addresses = await sender.AddressesAsync(recipients.Select(m => m.SeimasPersonId), cancellationToken);
        var byHash = addresses.ToDictionary(a => keys.AddressHash(a.Value), a => a.Key);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entries = await db.Suppressions.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
        var people = recipients.ToDictionary(m => m.SeimasPersonId);
        var listed = entries
            .Where(e => byHash.ContainsKey(e.AddressHash))
            .Select(e => people[byHash[e.AddressHash]] is var mp ? new SuppressedMp(e.Id, mp.Slug, mp.FirstName, mp.LastName, e.Reason, e.CreatedAt) : null)
            .OfType<SuppressedMp>()
            .ToList();
        return new SuppressionView(listed, entries.Count - listed.Count);
    }

    public async Task<Result<Unit>> SuppressAsync(string address, string statement, StaffActor administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (!SendingRules.IsAddress(address))
        {
            return Error.Validation("mp-mail.address-invalid", "address");
        }

        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        var hash = keys.AddressHash(address);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Suppressions.AnyAsync(s => s.AddressHash == hash, cancellationToken))
        {
            return Error.Conflict("suppression.exists");
        }

        var entry = new Suppression
        {
            Id = Ids.New(clock),
            AddressHash = hash,
            Reason = "staff",
            CreatedAt = clock.GetUtcNow(),
            AddedBy = administrator.UserId,
            Statement = statement.Trim(),
        };
        db.Suppressions.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("suppression.added", administrator.UserId, administrator.Role, "suppression:" + entry.Id,
                JsonSerializer.SerializeToElement(new { reason = entry.Reason, statement = entry.Statement }, JsonSerializerOptions.Web), administrator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> UnsuppressAsync(Guid? entryId, string? address, string statement, StaffActor administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (StaffStatements.Check(statement) is { } invalid)
        {
            return invalid;
        }

        var hash = !string.IsNullOrWhiteSpace(address) ? keys.AddressHash(address) : null;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entry = entryId is { } id
            ? await db.Suppressions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            : hash is not null ? await db.Suppressions.FirstOrDefaultAsync(s => s.AddressHash == hash, cancellationToken) : null;
        if (entry is null)
        {
            return Error.NotFound("suppression.not-found");
        }

        db.Suppressions.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("suppression.removed", administrator.UserId, administrator.Role, "suppression:" + entry.Id,
                JsonSerializer.SerializeToElement(new { reason = entry.Reason, statement = statement.Trim() }, JsonSerializerOptions.Web), administrator.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Changes the frequency by the MP's link (<paramref name="token"/>) or for a linked account (<paramref name="person"/>), and
    /// confirms it by email to the official address when it changed.
    /// </summary>
    private async Task<Result<MpMailChoice>> ChangeAsync(string? token, long? person, string frequency, CancellationToken ct)
    {
        if (!Frequencies.All.Contains(frequency))
        {
            return Error.Validation("mp-mail.frequency-invalid", "frequency");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        long seimasPersonId;
        if (token is not null)
        {
            if (await FindAsync(db, token, TokenKinds.Preferences, ct, forUpdate: true) is not { } found)
            {
                return NotFound;
            }

            seimasPersonId = found.SeimasPersonId;
        }
        else
        {
            seimasPersonId = person!.Value;
            if (await mps.MpsAsync([seimasPersonId], ct) is var known && !known.ContainsKey(seimasPersonId))
            {
                return Error.NotFound("mp.not-found");
            }
        }

        var pref = await PrefAsync(db, seimasPersonId, ct);
        if (pref.Frequency != frequency)
        {
            pref.Frequency = frequency;
            pref.ChangedAt = clock.GetUtcNow();
            if (await OfficialAsync(db, seimasPersonId, ct) is { } official)
            {
                var link = await sender.PreferencesTokenAsync(db, seimasPersonId, pref.TokenGeneration, ct);
                await NoticeAsync(db, official, MpMailNotices.Changed, seimasPersonId, frequency, MpMailPaths.Preferences(link), null, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ChoiceAsync(db, seimasPersonId, ct);
    }

    private async Task RemoveExtraAsync(NotificationsDb db, long person, string Path, CancellationToken ct)
    {
        var pref = await PrefAsync(db, person, ct);
        if (pref.ExtraAddress is not { } removed)
        {
            return;
        }

        pref.ExtraAddress = null;
        pref.ExtraConfirmedAt = null;
        pref.ChangedAt = clock.GetUtcNow();
        await db.MpMailTokens.Where(t => t.SeimasPersonId == person && (t.Kind == TokenKinds.ExtraConfirm || t.Kind == TokenKinds.ExtraStop))
            .ExecuteDeleteAsync(ct);
        if (await OfficialAsync(db, person, ct) is { } official)
        {
            await NoticeAsync(db, official, MpMailNotices.ExtraRemoved, person, null, Path, removed, ct);
        }
    }

    /// <summary>
    /// The token's row when it is of <paramref name="kind"/>, still good and not used; null otherwise, whatever the reason. With
    /// <paramref name="forUpdate"/> the row is locked for the transaction, so two presses on one link change things once.
    /// </summary>
    private async Task<MpMailToken?> FindAsync(NotificationsDb db, string? token, string kind, CancellationToken ct, bool forUpdate = false)
    {
        if (!MailKeys.LooksLikeToken(token))
        {
            return null;
        }

        var hash = MailKeys.TokenHash(token!);
        var now = clock.GetUtcNow();
        var found = forUpdate
            ? await db.MpMailTokens.FromSqlInterpolated($"SELECT * FROM notify.mp_mail_token WHERE token_hash = {hash} FOR UPDATE").FirstOrDefaultAsync(ct)
            : await db.MpMailTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        return found is not null && found.Kind == kind && found.UsedAt is null && (found.ExpiresAt is null || found.ExpiresAt > now) ? found : null;
    }

    private async Task<MpEmailPref> PrefAsync(NotificationsDb db, long person, CancellationToken ct)
    {
        var pref = await db.MpEmailPrefs.FirstOrDefaultAsync(p => p.SeimasPersonId == person, ct);
        if (pref is null)
        {
            pref = new MpEmailPref { SeimasPersonId = person, Frequency = Frequencies.Default, ChangedAt = clock.GetUtcNow() };
            db.MpEmailPrefs.Add(pref);
        }

        return pref;
    }

    private async Task<Result<MpMailChoice>> ChoiceAsync(NotificationsDb db, long person, CancellationToken ct)
    {
        var mp = (await mps.MpsAsync([person], ct)).GetValueOrDefault(person);
        if (mp is null)
        {
            return NotFound;
        }

        var pref = await db.MpEmailPrefs.AsNoTracking().FirstOrDefaultAsync(p => p.SeimasPersonId == person, ct);
        return new MpMailChoice(mp.Slug, mp.FirstName, mp.LastName, pref?.Frequency ?? Frequencies.Default, pref?.ExtraAddress, pref?.ExtraConfirmedAt is not null);
    }

    /// <summary>The MP's official address for a preference email, unless there is none or it is suppressed.</summary>
    private async Task<string?> OfficialAsync(NotificationsDb db, long person, CancellationToken ct)
    {
        // The invented-data lock (design 8.19.1) holds for preference emails too.
        if (!await locks.MailMayLeaveAsync(ct))
        {
            return null;
        }

        var address = (await sender.AddressesAsync([person], ct)).GetValueOrDefault(person);
        return address is not null && !sender.IsSuppressed(await sender.SuppressedAsync(db, [address], ct), address) ? address : null;
    }

    private async Task NoticeAsync(
        NotificationsDb db, string to, string notice, long person, string? frequency, string? path, string? extraAddress, CancellationToken ct, int? validHours = null)
    {
        var mp = (await mps.MpsAsync([person], ct)).GetValueOrDefault(person);
        await sender.EnqueueAsync(
            db, MpMailKinds.Notice, new MpMailNoticeEmail(to, notice, mp is null ? "" : mp.FirstName + " " + mp.LastName, frequency, path, extraAddress, validHours), ct);
    }

    /// <summary>Who would get the introduction letter now: sitting MPs with an official address who have had none (design 8.19.5).</summary>
    private async Task<List<(MpRecipient Mp, string Address, MpEmailPref? Pref, string Status)>> IntroductionPlanAsync(
        NotificationsDb db, IReadOnlyList<MpRecipient> recipients, CancellationToken ct)
    {
        var addresses = await sender.AddressesAsync(recipients.Select(m => m.SeimasPersonId), ct);
        var prefs = await MpSender.PrefsAsync(db, recipients.Select(m => m.SeimasPersonId).ToList(), ct);
        var introduced = await MpSender.IntroducedAsync(db, ct);
        var suppressed = await sender.SuppressedAsync(db, addresses.Values, ct);
        var plan = new List<(MpRecipient, string, MpEmailPref?, string)>();
        foreach (var mp in recipients.Where(m => !introduced.Contains(m.SeimasPersonId)))
        {
            if (!addresses.TryGetValue(mp.SeimasPersonId, out var address))
            {
                continue;
            }

            var pref = prefs.GetValueOrDefault(mp.SeimasPersonId);
            var status = sender.IsSuppressed(suppressed, address) ? DeliveryStatuses.Suppressed
                : !SendingRules.PreferenceAllows(pref?.Frequency ?? Frequencies.Default, SendingRules.Introduction) ? DeliveryStatuses.SkippedPreference
                : DeliveryStatuses.Queued;
            plan.Add((mp, address, pref, status));
        }

        return plan;
    }

    private async Task<Result<MpMailChoice>> WithDbAsync(Func<NotificationsDb, Task<Result<MpMailChoice>>> read, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await read(db);
    }

    private static IntroductionVersion Version(IntroductionLetter l) => new(l.Version, l.Subject, l.Text, l.SignerName, l.SignerRole, l.WrittenAt, l.SentAt);
}

/// <summary>What the introduction letter must hold (design 8.19.5): a subject, the text, the signer's name and role, each within its length.</summary>
internal static class IntroductionRules
{
    public const int SubjectMax = 200;
    public const int TextMax = 10000;
    public const int SignerMax = 200;

    public static Error? Check(IntroductionDraft draft) =>
        Field(draft.Subject, SubjectMax) ? Error.Validation("introduction.invalid", "subject")
        : Field(draft.Text, TextMax) ? Error.Validation("introduction.invalid", "text")
        : Field(draft.SignerName, SignerMax) ? Error.Validation("introduction.invalid", "signerName")
        : Field(draft.SignerRole, SignerMax) ? Error.Validation("introduction.invalid", "signerRole")
        : null;

    private static bool Field(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max;

    /// <summary>Whether a draft says exactly what a stored version says (then saving it again changes nothing).</summary>
    public static bool Same(IntroductionLetter letter, IntroductionDraft draft) =>
        letter.Subject == draft.Subject.Trim() && letter.Text == draft.Text.Trim()
        && letter.SignerName == draft.SignerName.Trim() && letter.SignerRole == draft.SignerRole.Trim();
}
