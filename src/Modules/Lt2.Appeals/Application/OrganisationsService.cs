using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// Organisations and their statements (design 8.14.14, stage A): registering, the profile and its history, confirmation by
/// email domain, representatives through one-time links, leaving and closing, and statements on bills. Member actions are
/// recorded in the module's own tables and not in the audit log, so no audit row ties a person to an organisation; the
/// moderators' actions are audited (<see cref="OrganisationModerationService"/>).
/// </summary>
internal sealed class OrganisationsService(
    IDbContextFactory<AppealsDb> contexts,
    OrganisationReads reads,
    IParticipants participants,
    IPreScreening screening,
    ISettings settings,
    IOutbox outbox,
    IOrganisationCodeSwitch codes,
    TimeProvider clock) : IOrganisations
{
    private const int ListLimit = 500;
    private const string FallbackSlug = "organizacija";

    public async Task<OrganisationListPage> ListAsync(string? search, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var active = await db.Organisations.AsNoTracking().Where(o => o.Status == OrganisationStatuses.Active).ToListAsync(cancellationToken);
        var words = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matching = active
            .Where(o => words.All(w => o.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(o => o.Name, OrganisationReads.NameOrder)
            .ThenBy(o => o.Slug, StringComparer.Ordinal)
            .ToList();
        return new OrganisationListPage(
            matching.Take(ListLimit).Select(o => new OrganisationSummary(o.Slug, o.Name, OrganisationReads.Label(o), o.Status)).ToList(),
            matching.Count);
    }

    public async Task<Result<OrganisationProfile>> ProfileAsync(string slug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await OrganisationReads.BySlugAsync(db, slug, tracking: false, cancellationToken) is not { } o)
        {
            return Error.NotFound("organisation.not-found");
        }

        var history = await db.Revisions.AsNoTracking()
            .Where(r => r.EntityKind == "organisation" && r.EntityId == o.Id && (r.Field == "name" || r.Field == "website"))
            .OrderBy(r => r.ChangedAt)
            .Select(r => new ProfileChange(r.ChangedAt, r.Field, r.OldValue, r.NewValue))
            .ToListAsync(cancellationToken);

        // A blocked or closed organisation's statements are shown nowhere, its profile included (design 8.14.14).
        IReadOnlyList<ProfileStatement> statements = [];
        if (o.Status == OrganisationStatuses.Active)
        {
            var shown = await db.OrganisationStatements.AsNoTracking()
                .Where(s => s.OrganisationId == o.Id && s.Status == StatementStatuses.Published)
                .OrderByDescending(s => s.PublishedAt)
                .ToListAsync(cancellationToken);
            statements = await ProfileStatementsAsync(db, shown, cancellationToken);
        }

        return new OrganisationProfile(
            o.Id, o.Slug, o.Name, o.Description, o.Website, o.LegalCode, o.DeclaredLobbyist, OrganisationReads.Label(o), o.Status,
            o.Status == OrganisationStatuses.Blocked ? o.BlockStatement : null, o.RegisteredAt, o.ClosedAt, history, statements);
    }

    private async Task<IReadOnlyList<ProfileStatement>> ProfileStatementsAsync(AppealsDb db, List<OrganisationStatement> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var numbers = rows.Select(s => s.BillNumber).Distinct().ToList();
        var billsOf = await reads.BillsAsync(db, numbers, ct);
        var counts = await reads.CountsAsync(db, numbers, ct);
        var firstPublic = rows.ToDictionary(s => s.Id, s => s.FirstPublicAt);
        var ids = rows.Select(s => s.Id).ToList();
        var versions = (await db.Revisions.AsNoTracking()
                .Where(r => r.EntityKind == "statement" && ids.Contains(r.EntityId) && r.Field == "version")
                .OrderBy(r => r.ChangedAt)
                .Select(r => new { r.EntityId, r.ChangedAt, r.OldValue })
                .ToListAsync(ct))
            .GroupBy(r => r.EntityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<StatementVersion>)g.Select(r => OrganisationReads.Version(r.ChangedAt, r.OldValue, firstPublic[g.Key])).OfType<StatementVersion>().ToList());
        return rows.Select(s => new ProfileStatement(
            billsOf.GetValueOrDefault(s.BillNumber)?.Bill ?? new BillFacts(s.BillNumber, s.BillNumber),
            OrganisationReads.Info(s, counts.GetValueOrDefault(s.BillNumber)),
            billsOf.GetValueOrDefault(s.BillNumber)?.Open == true,
            versions.GetValueOrDefault(s.Id) ?? [])).ToList();
    }

    public async Task<Result<BillStatements>> OnBillAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await reads.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        if (bill.Hidden)
        {
            // Taking part is closed by the moderators: the page shows the bill only (design 8.12.3).
            return new BillStatements(bill.Bill.Number, false, []);
        }

        var rows = await (from s in db.OrganisationStatements.AsNoTracking().Shown(db.Organisations)
                          join o in db.Organisations.AsNoTracking() on s.OrganisationId equals o.Id
                          where s.BillNumber == bill.Bill.Number
                          orderby s.PublishedAt, s.Id
                          select new { s, o })
            .ToListAsync(cancellationToken);
        var counts = (await reads.CountsAsync(db, [bill.Bill.Number], cancellationToken))[bill.Bill.Number];
        return new BillStatements(
            bill.Bill.Number,
            bill.Open,
            rows.Select(r => new BillStatement(r.o.Id, r.o.Slug, r.o.Name, OrganisationReads.Label(r.o), OrganisationReads.Info(r.s, counts))).ToList());
    }

    public async Task<Result<OrganisationCreated>> RegisterAsync(Guid userId, NewOrganisation request, CancellationToken cancellationToken)
    {
        if (CheckProfile(request) is { } broken)
        {
            return OrganisationErrors.Of(broken);
        }

        var name = OrganisationRules.NormaliseName(request.Name);
        var description = AppealRules.NormaliseText(request.Description);
        var flags = await ScreenAsync(name, description, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The person's lock: the limit per account holds under parallel requests.
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);
        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        var now = clock.GetUtcNow();
        if (await WritingRestrictionReads.IsRestrictedAsync(db, userId, now, cancellationToken))
        {
            return Error.Forbidden("writing.restricted");
        }

        var registered = await db.Organisations.CountAsync(o => o.RegisteredByUserId == userId && o.Status != OrganisationStatuses.Closed, cancellationToken);
        if (OrganisationRules.CheckRegistering(registered, await settings.GetAsync(SettingKeys.OrganisationsPerAccount, cancellationToken)) is { } limited)
        {
            return OrganisationErrors.Of(limited);
        }

        var key = OrganisationRules.NameKey(name);
        if (await db.Organisations.AnyAsync(o => o.NameKey == key && o.Status == OrganisationStatuses.Active, cancellationToken))
        {
            return Error.Conflict("organisation.name-taken");
        }

        var organisation = new Organisation
        {
            Id = Ids.New(clock),
            Slug = await FreeSlugAsync(db, name, cancellationToken),
            Name = name,
            NameKey = key,
            Description = description,
            Website = OrganisationRules.Optional(request.Website),
            LegalCode = OrganisationRules.Optional(request.LegalCode),
            DeclaredLobbyist = request.DeclaredLobbyist,
            RegisteredByUserId = userId,
            RegisteredAt = now,
            Status = OrganisationStatuses.Active,
            ScreeningFlags = [.. flags],
            ChangedAt = now,
        };
        db.Organisations.Add(organisation);
        db.OrganisationRepresentatives.Add(new OrganisationRepresentative
        {
            Id = Ids.New(clock), OrganisationId = organisation.Id, UserId = userId, GrantedAt = now,
        });
        if (!await SaveAsync(db, cancellationToken))
        {
            return Error.Conflict("organisation.name-taken");
        }

        await transaction.CommitAsync(cancellationToken);
        return new OrganisationCreated(organisation.Id, organisation.Slug);
    }

    public async Task<Result<Unit>> ChangeAsync(string slug, Guid userId, NewOrganisation request, CancellationToken cancellationToken)
    {
        if (CheckProfile(request) is { } broken)
        {
            return OrganisationErrors.Of(broken);
        }

        var name = OrganisationRules.NormaliseName(request.Name);
        var description = AppealRules.NormaliseText(request.Description);
        var flags = await ScreenAsync(name, description, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: true, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        var now = clock.GetUtcNow();
        var key = OrganisationRules.NameKey(name);
        if (key != o.NameKey && await db.Organisations.AnyAsync(x => x.Id != o.Id && x.NameKey == key && x.Status == OrganisationStatuses.Active, cancellationToken))
        {
            return Error.Conflict("organisation.name-taken");
        }

        var website = OrganisationRules.Optional(request.Website);

        // Earlier names and websites stay in the public history; a new name or website, or a new description that
        // pre-screening flags, puts the organisation back in the moderators' queue (design 8.14.14, 6.8). The history
        // carries no person: appeals.revision is readable for analysis.
        var nameChanged = name != o.Name;
        var websiteChanged = website != o.Website;
        var descriptionChanged = description != o.Description;
        var descriptionFlagged = descriptionChanged && description.Length > 0 && (await screening.ScreenAsync(description, cancellationToken)).Count > 0;
        if (nameChanged)
        {
            db.Revisions.Add(AppealsService.Revise("organisation", o.Id, "name", o.Name, name, null, now));
        }

        if (websiteChanged)
        {
            db.Revisions.Add(AppealsService.Revise("organisation", o.Id, "website", o.Website, website, null, now));
            if (!OrganisationRules.KeepsConfirmation(o.ConfirmedBy, o.ConfirmedDomain, website))
            {
                o.ConfirmedBy = null;
                o.ConfirmedDomain = null;
                o.ConfirmedAt = null;
            }

            // A code sent for the old website confirms nothing any more.
            await db.OrganisationCodes.Where(c => c.OrganisationId == o.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        }

        o.Name = name;
        o.NameKey = key;
        o.Description = description;
        o.Website = website;
        o.LegalCode = OrganisationRules.Optional(request.LegalCode);
        o.DeclaredLobbyist = request.DeclaredLobbyist;
        o.ScreeningFlags = [.. flags];
        o.ChangedAt = now;
        if (OrganisationRules.ReturnsToQueue(nameChanged, websiteChanged, descriptionChanged, descriptionFlagged))
        {
            o.SeenAt = null;
        }

        if (!await SaveAsync(db, cancellationToken))
        {
            return Error.Conflict("organisation.name-taken");
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<MyOrganisation>> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from r in db.OrganisationRepresentatives.AsNoTracking()
                          join o in db.Organisations.AsNoTracking() on r.OrganisationId equals o.Id
                          where r.UserId == userId && r.EndedAt == null
                          select o)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(o => o.Name, OrganisationReads.NameOrder).Select(o => new MyOrganisation(o.Slug, o.Name, o.Status, OrganisationReads.Label(o))).ToList();
    }

    public async Task<Result<OrganisationManagement>> ManagementAsync(string slug, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await OrganisationReads.BySlugAsync(db, slug, tracking: false, cancellationToken) is not { } o)
        {
            return Error.NotFound("organisation.not-found");
        }

        var representatives = await db.OrganisationRepresentatives.AsNoTracking()
            .Where(r => r.OrganisationId == o.Id && r.EndedAt == null)
            .OrderBy(r => r.GrantedAt)
            .ToListAsync(cancellationToken);
        if (representatives.All(r => r.UserId != userId))
        {
            // Nobody else learns who the representatives are, staff included (design 8.14.14).
            return new OrganisationManagement(false, [], null, null, [], null);
        }

        var names = await participants.DisplayNamesAsync(representatives.Select(r => r.UserId).ToList(), cancellationToken);
        var host = OrganisationRules.WebsiteHost(o.Website);
        var domain = OrganisationRules.CheckDomainConfirmation(o.Status, o.ConfirmedBy, o.DomainBarred)
            ?? (host is null ? "confirm.no-website" : OrganisationRules.IsSharedHost(host) ? "confirm.shared-website" : null);
        var statements = await db.OrganisationStatements.AsNoTracking()
            .Where(s => s.OrganisationId == o.Id && s.Status != StatementStatuses.Withdrawn)
            .OrderByDescending(s => s.PublishedAt)
            .ToListAsync(cancellationToken);
        var numbers = statements.Select(s => s.BillNumber).Distinct().ToList();
        var facts = await reads.BillFactsAsync(numbers, cancellationToken);
        var counts = await reads.CountsAsync(db, numbers, cancellationToken);
        var refusal = OrganisationRules.CheckActive(o.Status)
            ?? (await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), cancellationToken) ? "writing.restricted" : null);
        return new OrganisationManagement(
            true,
            representatives.Select(r => new RepresentativeInfo(r.Id, names.GetValueOrDefault(r.UserId), r.UserId == userId, r.GrantedAt)).ToList(),
            domain,
            host,
            statements.Select(s => new RepresentedStatement(facts.GetValueOrDefault(s.BillNumber) ?? new BillFacts(s.BillNumber, s.BillNumber), OrganisationReads.Info(s, counts.GetValueOrDefault(s.BillNumber)))).ToList(),
            refusal);
    }

    public async Task<Result<Unit>> SendCodeAsync(string slug, Guid userId, string address, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if (OrganisationRules.CheckDomainConfirmation(o.Status, o.ConfirmedBy, o.DomainBarred) is { } refused)
        {
            return OrganisationErrors.Of(refused);
        }

        var (wrong, domain) = OrganisationRules.CheckConfirmationAddress(o.Website, address);
        if (wrong is not null)
        {
            return OrganisationErrors.Of(wrong);
        }

        // The sign-in codes' limits (design 6.2), per organisation; a person who asks too often is told so.
        var now = clock.GetUtcNow();
        var lastHour = await db.OrganisationCodes.CountAsync(c => c.OrganisationId == o.Id && c.CreatedAt > now.AddHours(-1), cancellationToken);
        var lastDay = await db.OrganisationCodes.CountAsync(c => c.OrganisationId == o.Id && c.CreatedAt > now.AddDays(-1), cancellationToken);
        if (!OrganisationCodeRules.MayIssue(
                lastHour, lastDay, await settings.GetAsync(SettingKeys.SignInCodesPerHour, cancellationToken), await settings.GetAsync(SettingKeys.SignInCodesPerDay, cancellationToken)))
        {
            return new Error(ErrorKind.TooManyRequests, "confirm.too-many");
        }

        // A new code replaces the earlier one.
        await db.OrganisationCodes.Where(c => c.OrganisationId == o.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        var id = Ids.New(clock);
        var code = OrganisationCodeRules.Generate();
        db.OrganisationCodes.Add(new OrganisationCode
        {
            Id = id,
            OrganisationId = o.Id,
            Domain = domain!,
            CodeHash = OrganisationCodeRules.Hash(id, code),
            CreatedAt = now,
            ExpiresAt = now + OrganisationCodeRules.Lifetime,
            RequestedByUserId = userId,
        });
        await db.SaveChangesAsync(cancellationToken);

        // The mailbox exists only in this outbox row until the email is sent; the module keeps the domain.
        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.OrganisationCodeEmail, new OrganisationCodeEmail(address.Trim(), code, (int)OrganisationCodeRules.Lifetime.TotalMinutes, o.Name, domain!)),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ConfirmAsync(string slug, Guid userId, string code, CancellationToken cancellationToken)
    {
        var invalid = Error.Validation("confirm.code-invalid", "code");
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        var now = clock.GetUtcNow();
        var current = await db.OrganisationCodes.AsNoTracking()
            .Where(c => c.OrganisationId == o.Id && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return invalid;
        }

        if (!OrganisationCodeRules.Matches(current, code, now))
        {
            await db.OrganisationCodes.Where(c => c.Id == current.Id && c.FailedAttempts < OrganisationCodeRules.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return invalid;
        }

        // After a moderator withdrew a confirmation a code confirms nothing; nor does one sent for an earlier website.
        if (OrganisationRules.CheckDomainConfirmation(o.Status, o.ConfirmedBy, o.DomainBarred) is not null || current.Domain != OrganisationRules.WebsiteHost(o.Website))
        {
            return invalid;
        }

        var consumed = await db.OrganisationCodes
            .Where(c => c.Id == current.Id && c.ConsumedAt == null && c.FailedAttempts < OrganisationCodeRules.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return invalid;
        }

        o.ConfirmedBy = ConfirmationKinds.Domain;
        o.ConfirmedDomain = current.Domain;
        o.ConfirmedAt = now;
        o.ConfirmedByUserId = null;
        o.ConfirmationNote = null;
        o.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<InviteCreated>> InviteAsync(string slug, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if ((OrganisationRules.CheckActive(o.Status) ?? OrganisationRules.CheckNewRepresentative(await OrganisationReads.ActiveRepresentativesAsync(db, o.Id, cancellationToken))) is { } refused)
        {
            return OrganisationErrors.Of(refused);
        }

        var now = clock.GetUtcNow();
        var token = InviteTokens.New();
        var invite = new OrganisationInvite
        {
            Id = Ids.New(clock), OrganisationId = o.Id, TokenHash = InviteTokens.Hash(token), CreatedByUserId = userId, CreatedAt = now,
            ExpiresAt = now + OrganisationRules.InviteLifetime,
        };
        db.OrganisationInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InviteCreated(token, invite.ExpiresAt);
    }

    public async Task<Result<InviteView>> InviteViewAsync(string token, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var hash = InviteTokens.Hash(token);
        var invite = await db.OrganisationInvites.AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        var o = invite is null ? null : await db.Organisations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invite.OrganisationId, cancellationToken);
        if (invite is null || o is null)
        {
            return Error.NotFound("invite.not-found");
        }

        var usable = InviteTokens.IsUsable(invite.ExpiresAt, invite.UsedAt, clock.GetUtcNow()) && o.Status == OrganisationStatuses.Active;
        return new InviteView(o.Slug, o.Name, usable, await OrganisationReads.IsRepresentativeAsync(db, o.Id, userId, cancellationToken));
    }

    public async Task<Result<string>> AcceptInviteAsync(string token, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hash = InviteTokens.Hash(token);
        var invite = await db.OrganisationInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invite is null)
        {
            return Error.NotFound("invite.not-found");
        }

        await AdvisoryLocks.OrganisationAsync(db, invite.OrganisationId, cancellationToken);
        await db.Entry(invite).ReloadAsync(cancellationToken);
        var o = await db.Organisations.AsNoTracking().FirstAsync(x => x.Id == invite.OrganisationId, cancellationToken);
        var now = clock.GetUtcNow();
        if (!InviteTokens.IsUsable(invite.ExpiresAt, invite.UsedAt, now))
        {
            return Error.Conflict("invite.used");
        }

        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        if (OrganisationRules.CheckActive(o.Status) is { } inactive)
        {
            return OrganisationErrors.Of(inactive);
        }

        if (await OrganisationReads.IsRepresentativeAsync(db, o.Id, userId, cancellationToken))
        {
            return Error.Conflict("organisation.already-representative");
        }

        if (OrganisationRules.CheckNewRepresentative(await OrganisationReads.ActiveRepresentativesAsync(db, o.Id, cancellationToken)) is { } full)
        {
            return OrganisationErrors.Of(full);
        }

        invite.UsedAt = now;
        db.OrganisationRepresentatives.Add(new OrganisationRepresentative
        {
            Id = Ids.New(clock), OrganisationId = o.Id, UserId = userId, GrantedByUserId = invite.CreatedByUserId, GrantedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return o.Slug;
    }

    public async Task<Result<Unit>> RemoveRepresentativeAsync(string slug, Guid representativeId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if (o.Status == OrganisationStatuses.Closed)
        {
            return OrganisationErrors.Of("organisation.closed");
        }

        var row = await db.OrganisationRepresentatives.FirstOrDefaultAsync(r => r.Id == representativeId && r.OrganisationId == o.Id && r.EndedAt == null, cancellationToken);
        if (row is null)
        {
            return Error.NotFound("representative.not-found");
        }

        if (row.UserId == userId)
        {
            return Error.Conflict("representative.self");
        }

        row.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> LeaveAsync(string slug, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if (o.Status != OrganisationStatuses.Closed
            && OrganisationRules.CheckLeaving(await OrganisationReads.ActiveRepresentativesAsync(db, o.Id, cancellationToken)) is { } last)
        {
            return OrganisationErrors.Of(last);
        }

        await db.OrganisationRepresentatives.Where(r => r.OrganisationId == o.Id && r.UserId == userId && r.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.EndedAt, clock.GetUtcNow()), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> CloseAsync(string slug, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if (OrganisationRules.CheckClosing(o.Status, await OrganisationReads.ActiveRepresentativesAsync(db, o.Id, cancellationToken)) is { } refused)
        {
            return OrganisationErrors.Of(refused);
        }

        await CloseOrganisationAsync(db, reads, codes, transaction.GetDbTransaction(), o, clock.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Closes an organisation (design 8.14.14): its statements on open bills are withdrawn, those on closed bills stay as
    /// they were (shown nowhere, as the organisation is closed), codes and links stop working. Used by the last
    /// representative and by deleting that account.
    /// </summary>
    internal static async Task CloseOrganisationAsync(
        AppealsDb db, OrganisationReads reads, IOrganisationCodeSwitch codes, System.Data.Common.DbTransaction transaction, Organisation o, DateTimeOffset now, CancellationToken ct)
    {
        var statements = await db.OrganisationStatements
            .Where(s => s.OrganisationId == o.Id && StatementStatuses.Holding.Contains(s.Status))
            .ToListAsync(ct);
        var billsOf = await reads.BillsAsync(db, statements.Select(s => s.BillNumber).ToList(), ct);
        foreach (var s in statements.Where(s => billsOf.GetValueOrDefault(AppealReads.NormaliseBillNumber(s.BillNumber))?.Open == true))
        {
            s.Status = StatementStatuses.Withdrawn;
            s.WithdrawnAt = now;
        }

        await db.OrganisationCodes.Where(c => c.OrganisationId == o.Id && c.ConsumedAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);
        await db.OrganisationInvites.Where(i => i.OrganisationId == o.Id && i.UsedAt == null && i.ExpiresAt > now).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, now), ct);

        // Its invitation code stops for good (design 8.14.14, stage B); the count it brought stays.
        await codes.SetAsync(o.Id, OrganisationCodeStates.Closed, transaction, ct);
        o.Status = OrganisationStatuses.Closed;
        o.ClosedAt = now;
        o.ChangedAt = now;
    }

    public async Task<Result<IReadOnlyList<MyBillOrganisation>>> MineOnBillAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await reads.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        var organisations = await (from r in db.OrganisationRepresentatives.AsNoTracking()
                                   join o in db.Organisations.AsNoTracking() on r.OrganisationId equals o.Id
                                   where r.UserId == userId && r.EndedAt == null && o.Status != OrganisationStatuses.Closed
                                   select o)
            .ToListAsync(cancellationToken);
        organisations = organisations.OrderBy(o => o.Name, OrganisationReads.NameOrder).ToList();
        if (organisations.Count == 0)
        {
            return Result<IReadOnlyList<MyBillOrganisation>>.Success([]);
        }

        var ids = organisations.Select(o => o.Id).ToList();
        var statements = await db.OrganisationStatements.AsNoTracking()
            .Where(s => ids.Contains(s.OrganisationId) && s.BillNumber == bill.Bill.Number && s.Status != StatementStatuses.Withdrawn)
            .ToListAsync(cancellationToken);
        var counts = (await reads.CountsAsync(db, [bill.Bill.Number], cancellationToken))[bill.Bill.Number];
        var restricted = await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), cancellationToken);
        return organisations.Select(o =>
        {
            var statement = statements.FirstOrDefault(s => s.OrganisationId == o.Id);
            var refusal = OrganisationRules.CheckActive(o.Status) ?? (restricted ? "writing.restricted" : null) ?? OrganisationRules.CheckBillOpen(bill.Record?.Status, bill.Finished);
            return new MyBillOrganisation(o.Id, o.Slug, o.Name, o.Status, OrganisationReads.Label(o), statement is null ? null : OrganisationReads.Info(statement, counts), refusal);
        }).ToList();
    }

    public async Task<Result<StatementSaved>> SaveStatementAsync(string billNumber, string slug, Guid userId, NewStatement request, CancellationToken cancellationToken)
    {
        if (OrganisationRules.CheckStatement(request.Stance, request.Text, request.Link, request.Goal) is { } broken)
        {
            return OrganisationErrors.Of(broken);
        }

        var text = AppealRules.NormaliseText(request.Text);
        var link = OrganisationRules.Optional(request.Link);
        var flags = await screening.ScreenAsync(text, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.PersonAsync(db, userId, cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: true, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if (await reads.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        if (OrganisationRules.CheckBillOpen(bill.Record?.Status, bill.Finished) is { } closed)
        {
            return OrganisationErrors.Of(closed);
        }

        var now = clock.GetUtcNow();
        var status = flags.Count > 0 ? StatementStatuses.Pending : StatementStatuses.Published;
        var existing = await db.OrganisationStatements.FirstOrDefaultAsync(
            s => s.OrganisationId == o.Id && s.BillNumber == bill.Bill.Number && StatementStatuses.Holding.Contains(s.Status),
            cancellationToken);
        if (existing is { Status: StatementStatuses.Hidden })
        {
            // A hidden statement is a moderator's to restore; it is not written over.
            return Error.Conflict("statement.hidden");
        }

        if (existing is not null)
        {
            if (existing.Stance == request.Stance && existing.Text == text && existing.Link == link && existing.Goal == request.Goal)
            {
                return new StatementSaved(existing.Id, existing.Status);
            }

            // Every earlier version that was public stays public, under the statement on the profile (design 8.14.14); a
            // version that waited for a moderator was never public and is kept nowhere (design 6.8).
            if (OrganisationRules.KeepsVersion(existing.Status))
            {
                db.Revisions.Add(AppealsService.Revise("statement", existing.Id, "version", OrganisationReads.VersionJson(existing), null, null, now));
            }

            existing.Stance = request.Stance;
            existing.Text = text;
            existing.Link = link;
            existing.Goal = request.Goal;
            existing.Status = status;

            // A change never makes a statement "new" for its followers; one that waited and now passes is public for the first time.
            if (status == StatementStatuses.Published)
            {
                existing.FirstPublicAt ??= now;
            }

            existing.ScreeningFlags = [.. flags];
            existing.SeenAt = null;
            existing.EditedAt = now;
            existing.WrittenByUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StatementSaved(existing.Id, existing.Status);
        }

        var statement = new OrganisationStatement
        {
            Id = Ids.New(clock),
            OrganisationId = o.Id,
            BillNumber = bill.Bill.Number,
            Stance = request.Stance,
            Text = text,
            Link = link,
            Goal = request.Goal,
            Status = status,
            ScreeningFlags = [.. flags],
            PublishedAt = now,
            FirstPublicAt = status == StatementStatuses.Published ? now : null,
            WrittenByUserId = userId,
        };
        db.OrganisationStatements.Add(statement);
        if (!await SaveAsync(db, cancellationToken))
        {
            return Error.Conflict("statement.exists");
        }

        await transaction.CommitAsync(cancellationToken);
        return new StatementSaved(statement.Id, statement.Status);
    }

    public async Task<Result<Unit>> WithdrawStatementAsync(string billNumber, string slug, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acting = await ActingAsync(db, slug, userId, write: false, cancellationToken);
        if (!acting.IsSuccess)
        {
            return acting.Error!;
        }

        var o = acting.Value;
        if (OrganisationRules.CheckActive(o.Status) is { } inactive)
        {
            return OrganisationErrors.Of(inactive);
        }

        if (await reads.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        var statement = await db.OrganisationStatements.FirstOrDefaultAsync(
            s => s.OrganisationId == o.Id && s.BillNumber == bill.Bill.Number && (s.Status == StatementStatuses.Pending || s.Status == StatementStatuses.Published),
            cancellationToken);
        if (statement is null)
        {
            return Error.NotFound("statement.not-found");
        }

        // Frozen once the bill closes (design 8.14.3).
        if (OrganisationRules.CheckBillOpen(bill.Record?.Status, bill.Finished) is { } closed)
        {
            return OrganisationErrors.Of(closed);
        }

        var now = clock.GetUtcNow();
        statement.Status = StatementStatuses.Withdrawn;
        statement.WithdrawnAt = now;
        await ModerationRecorder.CloseReportsAsync(db, statement.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<OrganisationCodeAccess>> CodeAccessAsync(string slug, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await OrganisationReads.BySlugAsync(db, slug, tracking: false, cancellationToken) is not { } o)
        {
            return Error.NotFound("organisation.not-found");
        }

        if (!await OrganisationReads.IsRepresentativeAsync(db, o.Id, userId, cancellationToken))
        {
            return Error.Forbidden("organisation.representatives-only");
        }

        var active = o.Status == OrganisationStatuses.Active;
        var numbers = active
            ? await db.OrganisationStatements.AsNoTracking()
                .Where(s => s.OrganisationId == o.Id && s.Status == StatementStatuses.Published)
                .OrderByDescending(s => s.PublishedAt)
                .Select(s => s.BillNumber)
                .ToListAsync(cancellationToken)
            : [];
        var facts = await reads.BillFactsAsync(numbers, cancellationToken);
        return new OrganisationCodeAccess(o.Id, active, numbers.Select(n => facts.GetValueOrDefault(n) ?? new BillFacts(n, n)).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Organisations.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
    }

    /// <summary>
    /// The organisation for a representative's action, under its lock: not found, or forbidden for someone who is not its
    /// representative. With <paramref name="write"/> the organisation must be active and the person not restricted from
    /// writing (design 8.14.14: a person under a writing restriction cannot publish for one).
    /// </summary>
    private async Task<Result<Organisation>> ActingAsync(AppealsDb db, string slug, Guid userId, bool write, CancellationToken ct)
    {
        if (await OrganisationReads.BySlugAsync(db, slug, tracking: false, ct) is not { } found)
        {
            return Error.NotFound("organisation.not-found");
        }

        await AdvisoryLocks.OrganisationAsync(db, found.Id, ct);
        var o = await db.Organisations.FirstAsync(x => x.Id == found.Id, ct);
        if (!await OrganisationReads.IsRepresentativeAsync(db, o.Id, userId, ct))
        {
            return Error.Forbidden("organisation.representatives-only");
        }

        if (!write)
        {
            return o;
        }

        if (OrganisationRules.CheckActive(o.Status) is { } inactive)
        {
            return OrganisationErrors.Of(inactive);
        }

        return await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), ct) ? Error.Forbidden("writing.restricted") : o;
    }

    private static string? CheckProfile(NewOrganisation request) =>
        OrganisationRules.CheckName(request.Name)
        ?? OrganisationRules.CheckDescription(request.Description)
        ?? OrganisationRules.CheckWebsite(request.Website)
        ?? OrganisationRules.CheckLegalCode(request.LegalCode);

    /// <summary>Pre-screening reads the name and the description as it reads arguments (design 8.14.14); a flag decides nothing.</summary>
    private async Task<IReadOnlyList<string>> ScreenAsync(string name, string description, CancellationToken ct) =>
        await screening.ScreenAsync(description.Length == 0 ? name : name + "\n" + description, ct);

    private static async Task<string> FreeSlugAsync(AppealsDb db, string name, CancellationToken ct)
    {
        var slug = OrganisationRules.Slug(name);
        if (slug.Length == 0)
        {
            slug = FallbackSlug;
        }

        for (var n = 1; ; n++)
        {
            var candidate = OrganisationRules.SlugCandidate(slug, n);
            if (!await db.Organisations.AnyAsync(o => o.Slug == candidate, ct))
            {
                return candidate;
            }
        }
    }

    /// <summary>Saves; false when a unique index refused it (a name or a statement taken at the same moment).</summary>
    private static async Task<bool> SaveAsync(AppealsDb db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }
}

/// <summary>Turns an organisation rule's code into the module's error.</summary>
internal static class OrganisationErrors
{
    public static Error Of(string code) => code switch
    {
        "organisation.name-empty" or "organisation.name-length" => Error.Validation(code, "name"),
        "organisation.description-too-long" => Error.Validation(code, "description"),
        "organisation.website-invalid" => Error.Validation(code, "website"),
        "organisation.legal-code-invalid" => Error.Validation(code, "legalCode"),
        "statement.stance-invalid" => Error.Validation(code, "stance"),
        "statement.empty" or "statement.too-long" => Error.Validation(code, "text"),
        "statement.link-invalid" => Error.Validation(code, "link"),
        "statement.goal-invalid" or "statement.goal-with-remarks" => Error.Validation(code, "goal"),
        "confirm.address-invalid" or "confirm.public-mail" or "confirm.wrong-domain" => Error.Validation(code, "address"),
        "confirm.too-many" => new Error(ErrorKind.TooManyRequests, code),
        _ => Error.Conflict(code),
    };
}
