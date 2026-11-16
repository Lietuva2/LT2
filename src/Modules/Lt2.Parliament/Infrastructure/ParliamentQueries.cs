using System.Globalization;
using System.Text;
using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Statistics;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Parliament.Infrastructure;

/// <summary>Stable lower-case codes for the API (see Queries.cs).</summary>
internal static class Codes
{
    public static string Of(BodyKind kind) => kind switch
    {
        BodyKind.Faction => "faction",
        BodyKind.Committee => "committee",
        BodyKind.Commission => "commission",
        BodyKind.Group => "group",
        _ => "other",
    };

    public static string Of(VoteKind kind) => kind switch
    {
        VoteKind.StageResult => "stage-result",
        VoteKind.Amendment => "amendment",
        VoteKind.Procedural => "procedural",
        _ => "other",
    };

    public static string? Of(BillStage? stage) => stage switch
    {
        BillStage.Presentation => "presentation",
        BillStage.Consideration => "consideration",
        BillStage.Adoption => "adoption",
        _ => null,
    };

    public static string Of(BallotChoice choice) => choice switch
    {
        BallotChoice.For => "for",
        BallotChoice.Against => "against",
        BallotChoice.Abstain => "abstain",
        _ => "did-not-vote",
    };

    public static string Of(FactionPosition position) => position switch
    {
        FactionPosition.For => "for",
        FactionPosition.Against => "against",
        FactionPosition.Abstain => "abstain",
        FactionPosition.Free => "free",
        _ => "nobody-voted",
    };

    public static BodyKind? Kind(string kind) => kind switch
    {
        "faction" => BodyKind.Faction,
        "committee" => BodyKind.Committee,
        _ => null,
    };
}

internal sealed partial class ParliamentQueries(IDbContextFactory<ParliamentDb> contexts, IParliamentDataNotice notice, IMemoryCache cache, TimeProvider clock) : IParliamentQueries
{
    private static readonly CultureInfo Lithuanian = CultureInfo.GetCultureInfo("lt-LT");
    private static readonly StringComparer ByLithuanian = StringComparer.Create(Lithuanian, ignoreCase: true);

    private DateOnly Today => VilniusTime.Today(clock);

    public async Task<TermInfo?> CurrentTermAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        return term is null ? null : ToInfo(term);
    }

    public async Task<MpPage> MpsAsync(MpFilter filter, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(filter);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        if (term is null)
        {
            return new MpPage([], 0, null);
        }

        var summaries = await SummariesAsync(db, term.Id, cancellationToken);
        IEnumerable<MpSummary> list = summaries.Values;
        if (!string.IsNullOrWhiteSpace(filter.Faction))
        {
            list = list.Where(m => m.Faction?.Slug == filter.Faction);
        }

        if (!string.IsNullOrWhiteSpace(filter.Committee))
        {
            var today = Today;
            var members = await (from m in db.Memberships
                                 join b in db.Bodies on m.BodyId equals b.Id
                                 join p in db.People on m.PersonId equals p.Id
                                 where b.TermId == term.Id && b.Kind == BodyKind.Committee && b.Slug == filter.Committee
                                     && (m.To == null || m.To >= today)
                                 select p.Slug).ToListAsync(cancellationToken);
            list = list.Where(m => members.Contains(m.Slug));
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var query = Fold(filter.Query);
            list = list.Where(m => Fold(m.FirstName + " " + m.LastName).Contains(query, StringComparison.Ordinal));
        }

        var ordered = list.OrderBy(m => m.LastName, ByLithuanian).ThenBy(m => m.FirstName, ByLithuanian).ToList();
        return Page(ordered, filter.Cursor, filter.Limit, (items, total, next) => new MpPage(items, total, next));
    }

    public async Task<MpProfile?> MpAsync(string slug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var context = await MpContextAsync(db, slug, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (person, mandate, term) = context.Value;
        var summaries = await SummariesAsync(db, term.Id, cancellationToken);
        var today = Today;
        var memberships = (await (from m in db.Memberships.AsNoTracking()
                                  join b in db.Bodies on m.BodyId equals b.Id
                                  where m.PersonId == person.Id && b.TermId == term.Id
                                  select new { m, b }).ToListAsync(cancellationToken))
            .Select(x => new MembershipInfo(Ref(x.b), x.m.Role, x.m.From, x.m.To, x.m.To is null || x.m.To >= today))
            .OrderByDescending(x => x.Current).ThenBy(x => x.Body.Kind == "faction" ? 0 : 1).ThenBy(x => x.Body.Name, ByLithuanian).ThenByDescending(x => x.From)
            .ToList();
        var contacts = await db.Contacts.AsNoTracking().Where(c => c.PersonId == person.Id).OrderBy(c => c.Kind).ThenBy(c => c.Value)
            .Select(c => new ContactInfo(c.Kind, c.Value)).ToListAsync(cancellationToken);
        var stat = await db.MpStats.AsNoTracking().FirstOrDefaultAsync(s => s.PersonId == person.Id && s.TermId == term.Id, cancellationToken);
        var votes = await VotesOfAsync(db, person, mandate, term.Id, 0, 10, cancellationToken);

        return new MpProfile(
            summaries[person.Id],
            ToInfo(term),
            new MandateInfo(mandate.From, mandate.To, mandate.NominatingParty, mandate.ElectedHow, mandate.Constituency, mandate.TermsServed),
            memberships,
            contacts,
            stat is null ? null : ToInfo(stat),
            votes.Items);
    }

    public async Task<MpVotePage?> MpVotesAsync(string slug, string? cursor, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var context = await MpContextAsync(db, slug, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (person, mandate, term) = context.Value;
        return await VotesOfAsync(db, person, mandate, term.Id, Offset(cursor), Math.Clamp(limit, 1, 100), cancellationToken);
    }

    public async Task<IReadOnlyList<BodySummary>> BodiesAsync(string kind, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        if (term is null || Codes.Kind(kind) is not { } bodyKind)
        {
            return [];
        }

        var today = Today;
        var bodies = await db.Bodies.AsNoTracking().Where(b => b.TermId == term.Id && b.Kind == bodyKind).ToListAsync(cancellationToken);
        var counts = await db.Memberships.Where(m => (m.To == null || m.To >= today) && bodies.Select(b => b.Id).Contains(m.BodyId))
            .GroupBy(m => m.BodyId).Select(g => new { g.Key, Count = g.Select(m => m.PersonId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return bodies.Select(b => new BodySummary(b.Slug, b.Name, Codes.Of(b.Kind), counts.GetValueOrDefault(b.Id)))
            .OrderBy(b => b.Name, ByLithuanian).ToList();
    }

    public async Task<BodyDetail?> BodyAsync(string kind, string slug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        if (term is null || Codes.Kind(kind) is not { } bodyKind)
        {
            return null;
        }

        var body = await db.Bodies.AsNoTracking().FirstOrDefaultAsync(b => b.TermId == term.Id && b.Kind == bodyKind && b.Slug == slug, cancellationToken);
        if (body is null)
        {
            return null;
        }

        var today = Today;
        var members = (await (from m in db.Memberships.AsNoTracking()
                              join p in db.People on m.PersonId equals p.Id
                              where m.BodyId == body.Id
                              select new { m, p }).ToListAsync(cancellationToken))
            .Select(x => new BodyMember(new MpRef(x.p.Slug, x.p.FirstName, x.p.LastName), x.m.Role, x.m.From, x.m.To, x.m.To is null || x.m.To >= today))
            .OrderByDescending(x => x.Current).ThenBy(x => x.Mp.LastName, ByLithuanian).ThenBy(x => x.Mp.FirstName, ByLithuanian)
            .ToList();

        var recent = new List<FactionVote>();
        if (bodyKind == BodyKind.Faction)
        {
            var rows = await (from fp in db.FactionPositions.AsNoTracking()
                              join v in db.Votes on fp.VoteId equals v.Id
                              where fp.FactionId == body.Id
                              orderby v.HeldAt descending
                              select new { fp, v }).Take(10).ToListAsync(cancellationToken);
            var bills = await BillsByIdAsync(db, rows.Select(r => r.v.BillId), cancellationToken);
            recent = rows.Select(r => new FactionVote(Ref(r.v, bills), Split(r.fp, Ref(body)))).ToList();
        }

        return new BodyDetail(new BodySummary(body.Slug, body.Name, Codes.Of(body.Kind), members.Count(m => m.Current)), ToInfo(term), members, recent);
    }

    public async Task<BillPage> BillsAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var bills = await db.Bills.AsNoTracking()
            .Select(b => new { b.Number, b.Title, Last = db.Votes.Where(v => v.BillId == b.Id).Max(v => (DateTimeOffset?)v.HeldAt) })
            .ToListAsync(cancellationToken);
        IEnumerable<BillSummary> list = bills.Select(b => new BillSummary(b.Number, b.Title, b.Last));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = Fold(query);
            list = list.Where(b => Fold(b.Number + " " + b.Title).Contains(q, StringComparison.Ordinal));
        }

        var ordered = list.OrderByDescending(b => b.LastVoteAt).ThenBy(b => b.Number, StringComparer.Ordinal).ToList();
        return Page(ordered, cursor, Math.Clamp(limit, 1, 100), (items, total, next) => new BillPage(items, total, next));
    }

    public async Task<BillDetail?> BillAsync(string number, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Number == number, cancellationToken);
        if (bill is null)
        {
            return null;
        }

        var refs = new Dictionary<Guid, BillRef> { [bill.Id] = new(bill.Number, bill.Title) };
        var votes = await db.Votes.AsNoTracking().Where(v => v.BillId == bill.Id).OrderBy(v => v.HeldAt).ToListAsync(cancellationToken);
        return new BillDetail(bill.Number, bill.Title, bill.ESeimasUrl, votes.Select(v => Summary(v, refs)).ToList());
    }

    public async Task<VoteDetail?> VoteAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await (from v in db.Votes.AsNoTracking()
                         join i in db.AgendaItems on v.AgendaItemId equals i.Id
                         join s in db.Sittings on i.SittingId equals s.Id
                         where v.Id == id
                         select new { v, i.Title, s.StartsAt }).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var bills = await BillsByIdAsync(db, [row.v.BillId], cancellationToken);
        var positions = await (from fp in db.FactionPositions.AsNoTracking()
                               join b in db.Bodies on fp.FactionId equals b.Id into bs
                               from b in bs.DefaultIfEmpty()
                               where fp.VoteId == id
                               select new { fp, b }).ToListAsync(cancellationToken);
        var factions = positions
            .Select(x => Split(x.fp, x.b is null ? null : Ref(x.b)))
            .OrderByDescending(f => f.For + f.Against + f.Abstain + f.DidNotVote).ThenBy(f => f.Label, StringComparer.Ordinal)
            .ToList();
        var ballots = (await (from b in db.Ballots.AsNoTracking()
                              join p in db.People on b.PersonId equals p.Id
                              where b.VoteId == id
                              select new { b, p }).ToListAsync(cancellationToken))
            .Select(x => new BallotInfo(new MpRef(x.p.Slug, x.p.FirstName, x.p.LastName), x.b.FactionLabel, Codes.Of(x.b.Choice)))
            .OrderBy(x => x.Mp.LastName, ByLithuanian).ThenBy(x => x.Mp.FirstName, ByLithuanian)
            .ToList();
        return new VoteDetail(Summary(row.v, bills), row.StartsAt, row.Title, factions, ballots);
    }

    public async Task<VoteHistoryPage> VoteHistoryAsync(VoteHistoryFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var votes = filter.Kind switch
        {
            VoteHistoryKind.All => db.Votes.AsNoTracking(),
            VoteHistoryKind.Adoptions => db.Votes.AsNoTracking().Where(v => v.Kind == VoteKind.StageResult && v.BillId != null && v.Stage == BillStage.Adoption),
            _ => db.Votes.AsNoTracking().Where(v => v.Kind == VoteKind.StageResult && v.BillId != null),
        };

        // "Held on that day or earlier": before the next Vilnius midnight. The last day there is has no next one.
        if (filter.Until is { } until && until < DateOnly.MaxValue)
        {
            var stop = VilniusTime.StartOf(until.AddDays(1));
            votes = votes.Where(v => v.HeldAt < stop);
        }

        // The search folds the bills in memory, as the bill list does (a few thousand at most), then filters the votes
        // by them in the database.
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var q = Fold(filter.Query);
            var bills = await db.Bills.AsNoTracking().Select(b => new { b.Id, b.Number, b.Title }).ToListAsync(cancellationToken);
            var found = bills.Where(b => Fold(b.Number + " " + b.Title).Contains(q, StringComparison.Ordinal)).Select(b => (Guid?)b.Id).ToList();
            votes = votes.Where(v => found.Contains(v.BillId));
        }

        var total = await votes.CountAsync(cancellationToken);
        var offset = Offset(filter.Cursor);
        var limit = Math.Clamp(filter.Limit, 1, 300);
        var rows = await (from v in votes
                          join i in db.AgendaItems on v.AgendaItemId equals i.Id
                          orderby v.HeldAt descending, v.Id descending
                          select new { v, i.Title }).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        var refs = await BillsByIdAsync(db, rows.Select(r => r.v.BillId), cancellationToken);
        var next = offset + limit < total ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null;
        return new VoteHistoryPage(rows.Select(r => new VoteHistoryItem(Summary(r.v, refs), r.Title)).ToList(), total, next);
    }

    public async Task<ImportsInfo> ImportsAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync("parliament:imports", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            var success = await db.ImportRuns.AsNoTracking().Where(r => r.Status == "succeeded").MaxAsync(r => r.FinishedAt, cancellationToken);
            var last = await db.ImportRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);
            return new ImportsInfo(success, last?.StartedAt, last?.Status, await notice.IsParliamentDataInventedAsync(cancellationToken));
        }))!;

    public async Task<IReadOnlyList<VoteSummary>> StageResultsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var votes = await db.Votes.AsNoTracking()
            .Where(v => v.Kind == VoteKind.StageResult && v.BillId != null && v.HeldAt >= since)
            .OrderByDescending(v => v.HeldAt).ThenByDescending(v => v.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        var bills = await BillsByIdAsync(db, votes.Select(v => v.BillId), cancellationToken);
        return votes.Select(v => Summary(v, bills)).ToList();
    }

    public async Task<AgendaReadInfo> AgendaReadAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var last = await db.AgendaSnapshots.AsNoTracking()
            .Where(s => s.Failure == null)
            .OrderByDescending(s => s.FetchedAt)
            .Select(s => new { s.FetchedAt, s.Url })
            .FirstOrDefaultAsync(cancellationToken);
        return new AgendaReadInfo(last?.FetchedAt, last?.Url);
    }

    // ---- Shared pieces ------------------------------------------------------------------------------------------------

    private static Task<Term?> CurrentTermRowAsync(ParliamentDb db, CancellationToken cancellationToken) => CurrentTerm.FindAsync(db, cancellationToken);

    /// <summary>Every MP of the term with the faction of today (none in the mixed group; the last one, for a former MP).</summary>
    private async Task<Dictionary<Guid, MpSummary>> SummariesAsync(ParliamentDb db, int termId, CancellationToken cancellationToken)
    {
        var today = Today;
        var people = await (from m in db.Mandates.AsNoTracking()
                            join p in db.People on m.PersonId equals p.Id
                            where m.TermId == termId
                            select new { p, m }).ToListAsync(cancellationToken);
        var factions = await (from m in db.Memberships.AsNoTracking()
                              join b in db.Bodies on m.BodyId equals b.Id
                              where b.TermId == termId && b.Kind == BodyKind.Faction
                              select new { m.PersonId, m.From, m.To, b }).ToListAsync(cancellationToken);
        return people.ToDictionary(x => x.p.Id, x =>
        {
            // A sitting MP's faction is today's, or none (the mixed group is not a faction); a former MP's, the last one.
            var sitting = CurrentTerm.IsSitting(x.m, today);
            var faction = factions.Where(f => f.PersonId == x.p.Id && (!sitting || f.To is null || f.To >= today))
                .OrderByDescending(f => f.From).FirstOrDefault();
            return new MpSummary(x.p.Slug, x.p.FirstName, x.p.LastName, x.p.Gender, sitting, faction is null ? null : Ref(faction.b), x.m.NominatingParty);
        });
    }

    private static async Task<(Person Person, Mandate Mandate, Term Term)?> MpContextAsync(ParliamentDb db, string slug, CancellationToken cancellationToken)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);
        if (person is null)
        {
            return null;
        }

        var current = await CurrentTermRowAsync(db, cancellationToken);
        var mandate = await db.Mandates.AsNoTracking().Where(m => m.PersonId == person.Id)
            .OrderByDescending(m => current != null && m.TermId == current.Id).ThenByDescending(m => m.TermId).FirstOrDefaultAsync(cancellationToken);
        if (mandate is null)
        {
            return null;
        }

        var term = await db.Terms.AsNoTracking().FirstAsync(t => t.Id == mandate.TermId, cancellationToken);
        return (person, mandate, term);
    }

    /// <summary>The votes of the term held during the mandate, newest first, with the MP's choice and faction split.</summary>
    private static async Task<MpVotePage> VotesOfAsync(ParliamentDb db, Person person, Mandate mandate, int termId, int offset, int limit, CancellationToken cancellationToken)
    {
        var start = VilniusTime.StartOf(mandate.From);
        var stop = mandate.To is { } last ? VilniusTime.StartOf(last.AddDays(1)) : new DateTimeOffset(3000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var votes = await (from v in db.Votes.AsNoTracking()
                           join i in db.AgendaItems on v.AgendaItemId equals i.Id
                           join s in db.Sittings on i.SittingId equals s.Id
                           join ses in db.Sessions on s.SessionId equals ses.Id
                           where ses.TermId == termId && v.HeldAt >= start && v.HeldAt < stop
                           orderby v.HeldAt descending, v.Id descending
                           select v).Skip(offset).Take(limit + 1).ToListAsync(cancellationToken);
        var more = votes.Count > limit;
        votes = votes.Take(limit).ToList();
        var ids = votes.Select(v => v.Id).ToList();
        var ballots = await db.Ballots.AsNoTracking().Where(b => b.PersonId == person.Id && ids.Contains(b.VoteId)).ToDictionaryAsync(b => b.VoteId, cancellationToken);
        var positions = await (from fp in db.FactionPositions.AsNoTracking()
                               join b in db.Bodies on fp.FactionId equals b.Id into bs
                               from b in bs.DefaultIfEmpty()
                               where ids.Contains(fp.VoteId)
                               select new { fp, b }).ToListAsync(cancellationToken);
        var bills = await BillsByIdAsync(db, votes.Select(v => v.BillId), cancellationToken);

        var items = votes.Select(v =>
        {
            if (v.IsConsensus || !v.HasResult)
            {
                return new MpVote(Ref(v, bills), null, null);
            }

            var ballot = ballots.GetValueOrDefault(v.Id);
            var split = ballot?.FactionLabel is { } label
                ? positions.Where(p => p.fp.VoteId == v.Id && p.fp.FactionLabel == label.Trim()).Select(p => Split(p.fp, p.b is null ? null : Ref(p.b))).FirstOrDefault()
                : null;
            return new MpVote(Ref(v, bills), Codes.Of(ballot?.Choice ?? BallotChoice.DidNotVote), split);
        }).ToList();
        return new MpVotePage(items, more ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null);
    }

    private static async Task<Dictionary<Guid, BillRef>> BillsByIdAsync(ParliamentDb db, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return await db.Bills.AsNoTracking().Where(b => wanted.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => new BillRef(b.Number, b.Title), cancellationToken);
    }

    private static VoteSummary Summary(Vote v, IReadOnlyDictionary<Guid, BillRef> bills) =>
        new(Ref(v, bills), new VoteTotalsInfo(v.For, v.Against, v.Abstain, v.Voted, v.Seats), v.OfficialNote);

    private static VoteRef Ref(Vote v, IReadOnlyDictionary<Guid, BillRef> bills) =>
        new(v.Id, v.HeldAt, v.Wording, Codes.Of(v.Kind), Codes.Of(v.Stage), v.IsConsensus, v.HasResult, v.BillId is { } b ? bills.GetValueOrDefault(b) : null);

    private static FactionSplit Split(FactionPositionRow p, BodyRef? faction) =>
        new(p.FactionLabel, faction, Codes.Of(p.Position), p.For, p.Against, p.Abstain, p.DidNotVote);

    private static BodyRef Ref(Body b) => new(b.Slug, b.Name, Codes.Of(b.Kind));

    private static TermInfo ToInfo(Term t) => new(t.Id, t.Name, t.From, t.To);

    private static MpStatisticsInfo ToInfo(MpStat s)
    {
        var stats = new MpStatistics(s.VotesHeld, s.VotesTakenPart, s.VotesWithFactionPosition, s.VotedWithFaction, s.FreeVotes);
        return new MpStatisticsInfo(s.VotesHeld, s.VotesTakenPart, stats.Participation, s.VotesWithFactionPosition, s.VotedWithFaction, stats.WithFaction, s.FreeVotes);
    }

    private static int Offset(string? cursor) =>
        int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ? offset : 0;

    private static TPage Page<TItem, TPage>(List<TItem> all, string? cursor, int limit, Func<IReadOnlyList<TItem>, int, string?, TPage> make)
    {
        var offset = Offset(cursor);
        limit = Math.Clamp(limit, 1, 200);
        var items = all.Skip(offset).Take(limit).ToList();
        var next = offset + limit < all.Count ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null;
        return make(items, all.Count, next);
    }

    /// <summary>Lower case without diacritics, for search: "Ąžuolaitis" matches "azuolaitis".</summary>
    internal static string Fold(string text)
    {
        var decomposed = text.Trim().ToLower(Lithuanian).Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(c);
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
