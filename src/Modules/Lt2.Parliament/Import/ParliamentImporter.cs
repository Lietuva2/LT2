using System.IO.Compression;
using System.Security.Cryptography;
using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Client;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Infrastructure;
using Lt2.Parliament.Statistics;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lt2.Parliament.Import;

/// <summary>
/// Design 7.2. Walks the chain terms → MPs → sessions → sittings → agenda → proceedings → vote results. For each request:
/// fetch, store the raw response (deduplicated by hash), then parse the stored body and upsert by Seimas identifiers,
/// committing the data and "parsed" together. What was parsed and is old enough not to change is not asked again, so a
/// run that broke off is resumed by the next one, and a second run over unchanged data changes nothing. An administrator's
/// manual job forces parts of the walk (<see cref="ImportParts"/>): their requests are asked again however fresh.
/// </summary>
internal sealed class ParliamentImporter(
    ParliamentDb db,
    ISeimasClient client,
    IOptions<SeimasOptions> options,
    IParliamentStatistics statistics,
    TimeProvider clock,
    ILogger<ParliamentImporter> logger) : IParliamentImport
{
    /// <summary>Raise when a parser or an upsert changes, so stored responses are applied again.</summary>
    internal const int ParserVersion = 1;

    /// <summary>Only one import at a time, across worker instances.</summary>
    internal const long LockKey = 0x4C54_3250_4152_4C;

    private static readonly TimeSpan Daily = TimeSpan.FromHours(24);

    private ImportRun _run = new();

    /// <summary>Where each request stands, read once per run and kept current as requests are applied.</summary>
    private Dictionary<string, FetchState> _states = new(StringComparer.Ordinal);

    /// <summary>The parts this run forces (design 7.2: an administrator's manual job); none for the scheduled run.</summary>
    private ImportParts _force;

    public async Task<ImportDue> DueAsync(CancellationToken cancellationToken)
    {
        var lastSuccess = await db.ImportRuns.Where(r => r.Status == "succeeded").MaxAsync(r => (DateTimeOffset?)r.StartedAt, cancellationToken);
        var lastStart = await db.ImportRuns.MaxAsync(r => (DateTimeOffset?)r.StartedAt, cancellationToken);
        var now = clock.GetUtcNow();
        var today = VilniusTime.DateOf(now);
        var todayStart = VilniusTime.StartOf(today);
        var tomorrowStart = VilniusTime.StartOf(today.AddDays(1));
        var sittingToday = await db.Sittings.AnyAsync(s => s.StartsAt >= todayStart && s.StartsAt < tomorrowStart, cancellationToken);
        return new ImportDue(ImportSchedule.IsDue(now, lastSuccess, lastStart, sittingToday), lastStart);
    }

    public async Task<ImportOutcome> RunAsync(ImportParts force, CancellationToken cancellationToken)
    {
        _force = force;
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryLockAsync(cancellationToken))
            {
                logger.LogInformation("Parliament import skipped: another import is running (forced: {Forced})", ImportSchedule.Describe(force));
                return new ImportOutcome(null, "skipped", 0, 0, false, null);
            }

            try
            {
                return await RunLockedAsync(cancellationToken);
            }
            finally
            {
                await UnlockAsync();
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<ImportOutcome> RunLockedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        // A run still "running" while this one holds the lock was cut off (the process died): it did not succeed.
        await db.ImportRuns.Where(r => r.Status == "running")
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, "failed").SetProperty(r => r.Error, "abandoned").SetProperty(r => r.FinishedAt, clock.GetUtcNow()),
                cancellationToken);
        _run = new ImportRun { Id = Ids.New(clock), StartedAt = clock.GetUtcNow(), Source = ImportSchedule.RunSource(settings.BaseAddress?.ToString() ?? "", _force) };
        db.ImportRuns.Add(_run);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        logger.LogInformation("Parliament import {RunId} started from {Source}", _run.Id, _run.Source);

        try
        {
            await WalkAsync(settings, cancellationToken);
            _run.Status = "succeeded";
        }
        catch (Exception ex) when (ex is SeimasUnavailableException or SeimasFormatException)
        {
            // The site keeps showing what is stored; the next run resumes from here.
            _run.Status = "failed";
            _run.Error = ex.Message;
            logger.LogError(ex, "Parliament import {RunId} failed after {Requests} requests", _run.Id, _run.Requests);
        }
        catch (Exception ex)
        {
            // Anything else (the database, a bug) is not ours to swallow, but the run must not stay "running".
            _run.Status = "failed";
            _run.Error = ex.GetType().Name;
            throw;
        }
        finally
        {
            if (_run.Changes > 0)
            {
                db.ChangeTracker.Clear();
                await statistics.RecomputeAsync(settings.TermId, CancellationToken.None);
            }

            _run.FinishedAt = clock.GetUtcNow();
            db.ChangeTracker.Clear();
            db.ImportRuns.Update(_run);
            await db.SaveChangesAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "Parliament import {RunId} {Status}: {Requests} requests, {Changes} changes, synthetic {Synthetic}",
            _run.Id, _run.Status, _run.Requests, _run.Changes, _run.Synthetic);
        return new ImportOutcome(_run.Id, _run.Status, _run.Requests, _run.Changes, _run.Synthetic, _run.Error);
    }

    private async Task WalkAsync(SeimasOptions settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var recent = now - TimeSpan.FromDays(settings.RefetchDays);
        var termId = settings.TermId;
        _states = await db.FetchStates.AsNoTracking().ToDictionaryAsync(s => s.Request, StringComparer.Ordinal, ct);

        // The order is the chain's, and a forced run keeps it: the MPs come before the votes whose ballots are matched to them.
        await ImportAsync(SeimasRequest.Terms(), ImportParts.Members, s => now - s.FetchedAt >= Daily, ApplyTerms, ct);
        if (!await db.Terms.AnyAsync(t => t.Id == termId, ct))
        {
            throw new SeimasFormatException($"Term {termId} is not in the list of terms.");
        }

        await ImportAsync(SeimasRequest.Members(termId), ImportParts.Members, s => now - s.FetchedAt >= Daily, body => ApplyMembers(termId, body, ct), ct);
        await ImportAsync(SeimasRequest.Sessions(termId), ImportParts.Members, s => now - s.FetchedAt >= Daily, body => ApplySessions(termId, body, ct), ct);

        var sessions = await db.Sessions.AsNoTracking().Where(s => s.TermId == termId).OrderBy(s => s.From).ToListAsync(ct);
        foreach (var session in sessions)
        {
            var open = session.To is null || session.To.Value.ToDateTime(TimeOnly.MaxValue) >= recent.UtcDateTime;
            await ImportAsync(SeimasRequest.Sittings(session.Id), ImportParts.Sittings, _ => open, body => ApplySittings(session.Id, body, ct), ct);
        }

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var sittings = await db.Sittings.AsNoTracking().Where(s => sessionIds.Contains(s.SessionId)).OrderBy(s => s.StartsAt).ToListAsync(ct);
        foreach (var sitting in sittings)
        {
            var changing = sitting.StartsAt >= recent;
            await ImportAsync(SeimasRequest.Agenda(sitting.Id), ImportParts.Sittings, _ => changing, body => ApplyAgenda(sitting.Id, body, ct), ct);
            await ImportAsync(SeimasRequest.Proceedings(settings.ProceedingsParameter, sitting.Id), ImportParts.Sittings, _ => changing, body => ApplyProceedings(sitting.Id, body, ct), ct);

            var votes = await (from v in db.Votes.AsNoTracking()
                               join i in db.AgendaItems on v.AgendaItemId equals i.Id
                               where i.SittingId == sitting.Id && !v.IsConsensus
                               orderby v.HeldAt
                               select new { v.Id, v.HeldAt, v.HasResult }).ToListAsync(ct);
            foreach (var vote in votes)
            {
                // A result still missing is asked for again on a slow back-off, however old the vote: it may come late.
                var fresh = vote.HeldAt >= recent;
                await ImportAsync(
                    SeimasRequest.VoteResults(vote.Id),
                    ImportParts.Votes,
                    s => fresh || (!vote.HasResult && ImportSchedule.IsMissingResultDue(now, vote.HeldAt, s.FetchedAt)),
                    body => ApplyVoteResult(termId, vote.Id, body, ct),
                    ct);
            }
        }
    }

    /// <summary>
    /// One request: skipped when it was parsed, its <paramref name="part"/> is not forced and <paramref name="due"/> says it
    /// cannot have changed (a response parsed by an older parser is then applied again from the stored body, without
    /// asking the service); otherwise fetched, stored raw, and parsed from the stored body unless it is the body already
    /// applied by this parser.
    /// </summary>
    private async Task ImportAsync(SeimasRequest request, ImportParts part, Func<FetchState, bool> due, Func<byte[], Task> apply, CancellationToken ct)
    {
        var key = request.RelativeUri;
        var state = _states.GetValueOrDefault(key);
        var parsed = state is { ParsedAt: not null };
        var applied = parsed && state!.ParserVersion == ParserVersion;
        if (parsed && !ImportSchedule.IsForced(_force, part) && !due(state!))
        {
            if (!applied)
            {
                var stored = await db.RawDocuments.AsNoTracking().Where(r => r.Id == state!.RawDocumentId).Select(r => r.Body).FirstAsync(ct);
                await ApplyStoredAsync(key, Decompress(stored), apply, ct);
            }

            return;
        }

        var response = await client.GetAsync(request, ct);
        _run.Requests++;
        _run.Synthetic |= response.Synthetic;

        var now = clock.GetUtcNow();
        var hash = Convert.ToHexStringLower(SHA256.HashData(response.Body));
        var raw = await db.RawDocuments.FirstOrDefaultAsync(r => r.Request == key && r.Sha256 == hash, ct);
        if (raw is null)
        {
            raw = new RawDocument
            {
                Id = Ids.New(clock),
                Request = key,
                Service = request.Service,
                Url = response.Url.ToString(),
                Sha256 = hash,
                Body = Compress(response.Body),
                Synthetic = response.Synthetic,
                ImportRunId = _run.Id,
                FirstFetchedAt = now,
                LastFetchedAt = now,
            };
            db.RawDocuments.Add(raw);
        }
        else
        {
            raw.LastFetchedAt = now;
        }

        var unchanged = applied && state!.RawDocumentId == raw.Id;
        var tracked = await db.FetchStates.FindAsync([key], ct);
        if (tracked is null)
        {
            tracked = new FetchState { Request = key };
            db.FetchStates.Add(tracked);
        }

        tracked.RawDocumentId = raw.Id;
        tracked.FetchedAt = now;
        if (!unchanged)
        {
            tracked.ParsedAt = null;
        }

        await db.SaveChangesAsync(ct); // the raw response is stored before anything is parsed
        db.ChangeTracker.Clear();
        _states[key] = tracked;
        if (!unchanged)
        {
            await ApplyStoredAsync(key, Decompress(raw.Body), apply, ct);
        }
    }

    /// <summary>Parses a stored body and commits the data together with "parsed by this parser version".</summary>
    private async Task ApplyStoredAsync(string key, byte[] body, Func<byte[], Task> apply, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await apply(body);
        var tracked = await db.FetchStates.FindAsync([key], ct) ?? throw new InvalidOperationException($"No fetch state for {key}.");
        tracked.ParsedAt = clock.GetUtcNow();
        tracked.ParserVersion = ParserVersion;
        _run.Changes += CountDataChanges();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        _states[key] = tracked;
    }

    private int CountDataChanges() => db.ChangeTracker.Entries()
        .Count(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && e.Entity is not (FetchState or RawDocument or ImportRun));

    // ---- Apply: parse a stored body and upsert by Seimas identifiers ------------------------------------------------

    private async Task ApplyTerms(byte[] body)
    {
        var parsed = SeimasParsers.Terms(body);
        var ids = parsed.Select(t => t.Id).ToList();
        var existing = await db.Terms.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        foreach (var t in parsed)
        {
            if (!existing.TryGetValue(t.Id, out var term))
            {
                term = new Term { Id = t.Id };
                db.Terms.Add(term);
            }

            term.Name = t.Name;
            term.From = t.From;
            term.To = t.To;
        }
    }

    private async Task ApplyMembers(int termId, byte[] body, CancellationToken ct)
    {
        var parsed = SeimasParsers.Members(body);
        var seimasIds = parsed.Select(m => m.PersonId).ToList();
        var people = await db.People.Where(p => seimasIds.Contains(p.SeimasPersonId)).ToDictionaryAsync(p => p.SeimasPersonId, ct);
        var slugs = (await db.People.Select(p => p.Slug).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var personIds = people.Values.Select(p => p.Id).ToList();
        var mandates = await db.Mandates.Where(m => m.TermId == termId && personIds.Contains(m.PersonId)).ToDictionaryAsync(m => m.PersonId, ct);
        var bodies = await db.Bodies.Where(b => b.TermId == termId).ToDictionaryAsync(b => b.SeimasId, ct);
        var bodySlugs = bodies.Values.Select(b => b.Slug).ToHashSet(StringComparer.Ordinal);
        var bodyIds = bodies.Values.Select(b => b.Id).ToList();
        var memberships = await db.Memberships.Where(m => bodyIds.Contains(m.BodyId)).ToListAsync(ct);
        var contacts = await db.Contacts.Where(c => personIds.Contains(c.PersonId)).ToListAsync(ct);

        foreach (var m in parsed)
        {
            if (!people.TryGetValue(m.PersonId, out var person))
            {
                var slug = Slugs.Unique($"{m.FirstName} {m.LastName}", m.PersonId.ToString(System.Globalization.CultureInfo.InvariantCulture), slugs.Contains);
                slugs.Add(slug);
                person = new Person { Id = Ids.New(clock), SeimasPersonId = m.PersonId, Slug = slug };
                db.People.Add(person);
                people[m.PersonId] = person;
            }

            person.FirstName = m.FirstName;
            person.LastName = m.LastName;
            person.Gender = m.Gender;

            if (!mandates.TryGetValue(person.Id, out var mandate))
            {
                mandate = new Mandate { Id = Ids.New(clock), PersonId = person.Id, TermId = termId };
                db.Mandates.Add(mandate);
            }

            mandate.From = m.From;
            mandate.To = m.To;
            mandate.NominatingParty = m.NominatingParty;
            mandate.ElectedHow = m.ElectedHow;
            mandate.Constituency = m.Constituency;
            mandate.TermsServed = m.TermsServed;

            // Memberships of this term: the response is the whole truth for this person.
            var wanted = new List<Membership>();
            foreach (var ms in m.Memberships)
            {
                if (!bodies.TryGetValue(ms.BodyId, out var body2))
                {
                    var slug = Slugs.Unique(ms.BodyName, ms.BodyId.ToString(System.Globalization.CultureInfo.InvariantCulture), bodySlugs.Contains);
                    bodySlugs.Add(slug);
                    body2 = new Body { Id = Ids.New(clock), SeimasId = ms.BodyId, TermId = termId, Slug = slug };
                    db.Bodies.Add(body2);
                    bodies[ms.BodyId] = body2;
                }

                body2.Name = ms.BodyName;
                body2.Kind = VoteRules.Body(ms.BodyName);

                var membership = memberships.FirstOrDefault(x => x.PersonId == person.Id && x.BodyId == body2.Id && x.From == ms.From);
                if (membership is null)
                {
                    membership = new Membership { Id = Ids.New(clock), PersonId = person.Id, BodyId = body2.Id, From = ms.From };
                    db.Memberships.Add(membership);
                    memberships.Add(membership);
                }

                membership.Role = ms.Role;
                membership.To = ms.To;
                wanted.Add(membership);
            }

            db.Memberships.RemoveRange(memberships.Where(x => x.PersonId == person.Id && !wanted.Contains(x)).ToList());
            memberships.RemoveAll(x => x.PersonId == person.Id && !wanted.Contains(x));

            var currentContacts = contacts.Where(c => c.PersonId == person.Id).ToList();
            foreach (var c in m.Contacts.Distinct())
            {
                if (!currentContacts.Any(x => x.Kind == c.Kind && x.Value == c.Value))
                {
                    db.Contacts.Add(new Contact { Id = Ids.New(clock), PersonId = person.Id, Kind = c.Kind, Value = c.Value });
                }
            }

            db.Contacts.RemoveRange(currentContacts.Where(x => !m.Contacts.Contains(new ParsedContact(x.Kind, x.Value))));
        }
    }

    private async Task ApplySessions(int termId, byte[] body, CancellationToken ct)
    {
        var parsed = SeimasParsers.Sessions(body);
        var ids = parsed.Select(s => s.Id).ToList();
        var existing = await db.Sessions.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var s in parsed)
        {
            if (!existing.TryGetValue(s.Id, out var session))
            {
                session = new Session { Id = s.Id };
                db.Sessions.Add(session);
            }

            session.TermId = termId;
            session.Name = s.Name;
            session.From = s.From;
            session.To = s.To;
        }
    }

    private async Task ApplySittings(long sessionId, byte[] body, CancellationToken ct)
    {
        var parsed = SeimasParsers.Sittings(body);
        var ids = parsed.Select(s => s.Id).ToList();
        var existing = await db.Sittings.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var s in parsed)
        {
            if (!existing.TryGetValue(s.Id, out var sitting))
            {
                sitting = new Sitting { Id = s.Id };
                db.Sittings.Add(sitting);
            }

            sitting.SessionId = sessionId;
            sitting.Number = s.Number;
            sitting.Kind = s.Kind;
            sitting.StartsAt = s.StartsAt;
            sitting.EndsAt = s.EndsAt;
        }
    }

    private async Task ApplyAgenda(long sittingId, byte[] body, CancellationToken ct)
    {
        var parsed = SeimasParsers.Agenda(body);
        var numbers = parsed.Where(i => i.BillNumber is not null).Select(i => i.BillNumber!).Distinct().ToList();
        var bills = await db.Bills.Where(b => numbers.Contains(b.Number)).ToDictionaryAsync(b => b.Number, ct);
        var ids = parsed.Select(i => i.Id).ToList();
        var items = await db.AgendaItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var i in parsed)
        {
            Bill? bill = null;
            if (i.BillNumber is { } number)
            {
                if (!bills.TryGetValue(number, out bill))
                {
                    bill = new Bill { Id = Ids.New(clock), Number = number };
                    db.Bills.Add(bill);
                    bills[number] = bill;
                }

                bill.Title = i.Title;
                bill.ESeimasUrl = i.BillUrl ?? bill.ESeimasUrl;
            }

            if (!items.TryGetValue(i.Id, out var item))
            {
                item = new AgendaItem { Id = i.Id };
                db.AgendaItems.Add(item);
            }

            item.SittingId = sittingId;
            item.BillId = bill?.Id;
            item.Number = i.Number;
            item.Title = i.Title;
            item.Stage = i.Stage;
        }
    }

    private async Task ApplyProceedings(long sittingId, byte[] body, CancellationToken ct)
    {
        var parsed = SeimasParsers.Proceedings(body);
        var itemIds = parsed.Select(v => v.AgendaItemId).Distinct().ToList();
        var items = await db.AgendaItems.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.SittingId == sittingId).ToDictionaryAsync(i => i.Id, ct);
        var voteIds = parsed.Select(v => v.VoteId).ToList();
        var votes = await db.Votes.Where(v => voteIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var v in parsed)
        {
            if (!items.TryGetValue(v.AgendaItemId, out var item))
            {
                logger.LogWarning("Vote {VoteId} names agenda item {ItemId}, which the agenda of sitting {SittingId} does not list; skipped", v.VoteId, v.AgendaItemId, sittingId);
                continue;
            }

            if (!votes.TryGetValue(v.VoteId, out var vote))
            {
                vote = new Vote { Id = v.VoteId };
                db.Votes.Add(vote);
            }

            var (kind, stage) = VoteRules.Classify(v.Wording, VoteRules.Stage(item.Stage));
            vote.AgendaItemId = item.Id;
            vote.BillId = item.BillId;
            vote.HeldAt = v.HeldAt;
            vote.Wording = v.Wording;
            vote.Type = v.Type;
            vote.IsConsensus = v.IsConsensus;
            vote.Kind = kind;
            vote.Stage = stage;
        }
    }

    private async Task ApplyVoteResult(int termId, long voteId, byte[] body, CancellationToken ct)
    {
        var parsed = SeimasParsers.VoteResult(body);
        var vote = await db.Votes.FirstAsync(v => v.Id == voteId, ct);
        if (parsed is null)
        {
            logger.LogWarning("Vote {VoteId} has no result in open data yet", voteId);
            return;
        }

        if (parsed.VoteId != voteId)
        {
            throw new SeimasFormatException($"Asked for the result of vote {voteId}, got {parsed.VoteId}.");
        }

        vote.For = parsed.Totals.For;
        vote.Against = parsed.Totals.Against;
        vote.Abstain = parsed.Totals.Abstain;
        vote.Voted = parsed.Totals.Voted;
        vote.Seats = parsed.Totals.Seats;
        vote.OfficialNote = parsed.OfficialNote;
        vote.HasResult = true;

        // Who is who: by asmens_id, or by name among this term's MPs when the row has no id (register R10).
        var termPeople = await (from p in db.People.AsNoTracking()
                                join m in db.Mandates on p.Id equals m.PersonId
                                where m.TermId == termId
                                select p).ToListAsync(ct);
        var bySeimasId = termPeople.ToDictionary(p => p.SeimasPersonId);
        var byName = termPeople.GroupBy(p => NameKey(p.FirstName, p.LastName)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var day = VilniusTime.DateOf(vote.HeldAt);
        var factionOf = await FactionMembershipsOnAsync(termId, day, ct);

        var existing = await db.Ballots.Where(b => b.VoteId == voteId).ToDictionaryAsync(b => b.PersonId, ct);
        var seen = new HashSet<Guid>();
        var labelFactions = new List<(string Label, Guid Faction)>();
        foreach (var row in parsed.Ballots)
        {
            var person = row.PersonId is { } id && bySeimasId.TryGetValue(id, out var p) ? p
                : byName.GetValueOrDefault(NameKey(row.FirstName, row.LastName));
            if (person is null || !seen.Add(person.Id))
            {
                logger.LogWarning("A per-MP row of vote {VoteId} matches no MP of term {TermId} (or matches one twice); skipped", voteId, termId);
                continue;
            }

            if (!existing.TryGetValue(person.Id, out var ballot))
            {
                ballot = new Ballot { VoteId = voteId, PersonId = person.Id };
                db.Ballots.Add(ballot);
            }

            ballot.FactionLabel = row.FactionLabel;
            ballot.FactionId = factionOf.GetValueOrDefault(person.Id);
            ballot.Choice = row.Choice;
            if (row.FactionLabel is { } label && ballot.FactionId is { } faction)
            {
                labelFactions.Add((label.Trim(), faction));
            }
        }

        db.Ballots.RemoveRange(existing.Values.Where(b => !seen.Contains(b.PersonId)));

        // A label's faction is the faction body most of its MPs belonged to that day. Only such labels get a position:
        // the mixed group's label (no faction body) and unknown labels get none [decided 2026-11-12].
        var factionByLabel = labelFactions.GroupBy(x => x.Label, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Faction).OrderByDescending(f => f.Count()).First().Key, StringComparer.Ordinal);
        await statistics.ApplyFactionPositionsAsync(voteId, parsed.Ballots.Select(b => (b.FactionLabel, b.Choice)), factionByLabel, ct);
    }

    /// <summary>
    /// Each person's faction body on a day, from the memberships with dates. On the day of a change both the old and the
    /// new membership cover the day: the one that began last counts.
    /// </summary>
    private async Task<Dictionary<Guid, Guid?>> FactionMembershipsOnAsync(int termId, DateOnly day, CancellationToken ct)
    {
        var rows = await (from m in db.Memberships.AsNoTracking()
                          join b in db.Bodies on m.BodyId equals b.Id
                          where b.TermId == termId && b.Kind == BodyKind.Faction && m.From <= day && (m.To == null || m.To >= day)
                          select new { m.PersonId, m.BodyId, m.From }).ToListAsync(ct);
        return rows.GroupBy(r => r.PersonId)
            .ToDictionary(g => g.Key, g => (Guid?)g.OrderByDescending(r => r.From).ThenBy(r => r.BodyId).First().BodyId);
    }

    private static string NameKey(string first, string last) =>
        (first.Trim() + " " + last.Trim()).ToLower(System.Globalization.CultureInfo.GetCultureInfo("lt-LT"));

    // ---- Storage helpers ----------------------------------------------------------------------------------------------

    internal static byte[] Compress(byte[] body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(body);
        }

        return output.ToArray();
    }

    internal static byte[] Decompress(byte[] stored)
    {
        using var input = new MemoryStream(stored);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private async Task<bool> TryLockAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT pg_try_advisory_lock({LockKey})";
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task UnlockAsync()
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT pg_advisory_unlock({LockKey})";
        await command.ExecuteScalarAsync();
    }
}
