using System.Globalization;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Lt2.Migrator;

/// <summary>
/// <c>--demo-activity</c> (design 8.26.5, owner 2026-11-26): invented members' activity on the trial's real Seimas data, so the
/// trial looks used. Invented accounts (<c>demo.bandymas.…@pavyzdys.test</c>, fixed identifiers) take sides on real open
/// bills (<see cref="DemoActivityBills.Open"/>), write arguments, mark them and comment; three invented organisations state
/// positions; about thirty bills the Seimas finished this year get records with sides taken before their real adoption vote,
/// closed as the closing job closes them; the daily aggregates are counted for every day of those records; and the past
/// weeks' summaries are stored as dry runs. No description, no MP response and no Seimas data is written.
/// <para>
/// It refuses unless <c>Demo:Allowed</c> and <c>Email:Caught</c> are both true (no email of the deployment reaches a real
/// mailbox) and the parliament data is real only (<see cref="ParliamentMaintenance.IsDataRealAsync"/>; on the stand-in's data
/// <c>--demo-appeal</c> is the demonstration). A bill a real account took part on is left as it is. Fixed seeds and
/// identifiers: a second run finds everything there and writes nothing twice. While the invented accounts exist every page
/// says that members' activity is invented (<see cref="IInventedMembersNotice"/>); deleting the accounts on
/// <c>pavyzdys.test</c> and the Appeals and summaries rows removes it all (the trial's step 107 does).
/// </para>
/// </summary>
public static class DemoActivity
{
    /// <summary>How many invented members there are: the pool every bill draws its sides from.</summary>
    public const int Members = 3200;

    /// <summary>How many finished bills get invented closed records.</summary>
    public const int ClosedBills = 30;

    /// <summary>How many past weeks get an invented weekly summary (weeks the Seimas sat).</summary>
    public const int PastSummaries = 6;

    /// <summary>The first day whose finished bills may get invented closed records.</summary>
    public static readonly DateTimeOffset ClosedFrom = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    public sealed record Outcome(
        int AccountsCreated,
        int OpenWritten,
        int OpenAlreadyThere,
        int OpenLeft,
        int OpenNotFound,
        int ClosedWritten,
        int ClosedAlreadyThere,
        int Organisations,
        int Summaries,
        int DailyRows)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"invented accounts created: {AccountsCreated}; open bills written: {OpenWritten}, already there: {OpenAlreadyThere}, left as they are (a real account took part, or not open): {OpenLeft}, not in the data or finished: {OpenNotFound}; closed bills written and closed: {ClosedWritten}, already there: {ClosedAlreadyThere}; invented organisations created: {Organisations}; past summaries stored as dry runs: {Summaries}; daily aggregate rows: {DailyRows}");
    }

    /// <summary>What a run writes: the open bills, and how many closed bills and past summaries at most (tests write fewer).</summary>
    public sealed record Scope(IReadOnlyList<DemoActivityBills.Bill> Open, int ClosedBills, int PastSummaries)
    {
        public static Scope Trial { get; } = new(DemoActivityBills.Open, DemoActivity.ClosedBills, DemoActivity.PastSummaries);
    }

    public static async Task<Outcome> RunAsync(IConfiguration configuration, string connectionString, TimeProvider clock, CancellationToken cancellationToken, Scope? scope = null)
    {
        scope ??= Scope.Trial;
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        if (!configuration.GetValue<bool>("Demo:Allowed") || !configuration.GetValue<bool>("Email:Caught"))
        {
            throw new InvalidOperationException("Refused: --demo-activity runs only where Demo:Allowed and Email:Caught are both true (the trial, whose every email Mailpit catches).");
        }

        if (!await ParliamentMaintenance.IsDataRealAsync(connectionString, cancellationToken))
        {
            throw new InvalidOperationException("Refused: --demo-activity needs the real Seimas data only, and this database holds none or the stand-in's; there --demo-appeal is the demonstration.");
        }

        var now = clock.GetUtcNow();

        // The members: created once, then read back, so every later run uses the registration days stored the first time.
        var members = Pool(now, scope.Open);
        var representatives = Representatives(now);
        var everyone = members.Concat(representatives).ToList();
        var created = await InventedMembers.CreateAsync(connectionString, everyone, cancellationToken);
        await InventedMembers.AcceptTermsAsync(connectionString, everyone, cancellationToken);
        await InventedMembers.GivePositionConsentAsync(connectionString, everyone, cancellationToken);
        var since = await InventedMembers.SinceAsync(connectionString, members.Select(m => m.Id).ToList(), cancellationToken);
        var pool = members.Where(m => since.ContainsKey(m.Id)).Select(m => new KeyValuePair<Guid, DateTimeOffset>(m.Id, since[m.Id])).ToList();
        var named = members.Where(m => m.DisplayName is not null).ToDictionary(m => m.DisplayName!, m => m.Id, StringComparer.Ordinal);

        // Open bills.
        var open = scope.Open.Where(b => !DemoActivityBills.Excluded.Contains(b.Number)).ToList();
        var active = await ParliamentMaintenance.ActiveBillsAsync(connectionString, open.Select(b => b.Number).ToList(), cancellationToken);
        var records = new List<(Guid Record, string Bill)>();
        int written = 0, already = 0, left = 0, missing = 0;
        foreach (var bill in open)
        {
            if (!active.ContainsKey(bill.Number))
            {
                missing++;
                continue;
            }

            var (outcome, record) = await InventedActivity.AddToBillAsync(connectionString, BuildOpen(bill, pool, named, now), newRecordOnly: false, cancellationToken);
            switch (outcome)
            {
                case InventedRecordOutcome.Created: written++; break;
                case InventedRecordOutcome.AlreadyThere: already++; break;
                default: left++; break;
            }

            if (record is { } id)
            {
                records.Add((id, bill.Number));
            }
        }

        // The invented organisations' statements, only on bills whose activity is the demonstration's.
        var ours = records.Select(r => r.Bill).ToHashSet(StringComparer.Ordinal);
        var organisations = await InventedOrganisations.SeedAsync(
            connectionString,
            DemoActivityBills.Organisations(n => representatives[n - 1].Id).Where(o => ours.Contains(o.Statement.BillNumber)).ToList(),
            now.AddDays(-9),
            cancellationToken);

        // Bills the Seimas finished: sides before the real adoption vote, frozen as the closing job freezes them.
        var votes = await ParliamentMaintenance.AdoptionVotesAsync(connectionString, ClosedFrom, now.AddDays(-1), cancellationToken);
        var closedRecords = new List<Guid>();
        int closedWritten = 0, closedAlready = 0;
        foreach (var vote in PickClosed(votes, scope.ClosedBills))
        {
            var seimasFor = AnalyticsRules.SeimasMajorityFor(vote.Numbers) ?? true;
            var (outcome, record) = await InventedActivity.AddToBillAsync(connectionString, BuildClosed(vote, seimasFor, pool), newRecordOnly: true, cancellationToken);
            if (record is not { } id)
            {
                continue;
            }

            var closed = await InventedRecord.CloseAsync(connectionString, vote.BillNumber, vote.VoteId, vote.HeldAt.AddHours(1), cancellationToken);
            if (outcome == InventedRecordOutcome.Created || closed)
            {
                closedWritten++;
            }
            else
            {
                closedAlready++;
            }

            closedRecords.Add(id);
        }

        var daily = await InventedActivity.AggregateDaysAsync(connectionString, records.Select(r => r.Record).Concat(closedRecords).ToList(), now, cancellationToken);
        var summaries = await WriteSummariesAsync(connectionString, records, active, now, scope.PastSummaries, cancellationToken);
        return new Outcome(created, written, already, left, missing, closedWritten, closedAlready, organisations, summaries, daily);
    }

    /// <summary>The invented members, the same on every run: fixed identifiers and addresses, the named authors first.</summary>
    public static IReadOnlyList<InventedMember> Pool(DateTimeOffset now, IReadOnlyList<DemoActivityBills.Bill>? open = null)
    {
        var names = (open ?? DemoActivityBills.Open).Concat(DemoActivityBills.Open).SelectMany(b => b.Arguments.Select(a => a.Author).Concat((b.Comments ?? []).Select(c => c.Author)))
            .Distinct(StringComparer.Ordinal).ToList();
        var random = new Random(2026_11_26);
        var members = new List<InventedMember>(Members);
        for (var n = 1; n <= Members; n++)
        {
            // About one in eight registered in the last 30 days; the rest over the year and a half before.
            var days = random.NextDouble() < 0.12 ? random.Next(1, 30) : random.Next(31, 540);
            var registered = now.AddDays(-days).AddMinutes(-random.Next(0, 1440));
            members.Add(new InventedMember(MemberId(n), $"{InventedMembers.Prefix}bandymas.{n:D5}@{InventedMembers.Domain}", n <= names.Count ? names[n - 1] : null,
                n <= names.Count ? now.AddDays(-random.Next(60, 540)) : registered));
        }

        return members;
    }

    /// <summary>The invented organisations' representatives: accounts of their own, never given a side.</summary>
    public static IReadOnlyList<InventedMember> Representatives(DateTimeOffset now) =>
    [
        new(RepresentativeId(1), $"{InventedMembers.Prefix}bandymas.org1@{InventedMembers.Domain}", "Vilma K.", now.AddDays(-210)),
        new(RepresentativeId(2), $"{InventedMembers.Prefix}bandymas.org2@{InventedMembers.Domain}", "Algis R.", now.AddDays(-160)),
        new(RepresentativeId(3), $"{InventedMembers.Prefix}bandymas.org3@{InventedMembers.Domain}", "Daiva N.", now.AddDays(-130)),
    ];

    public static Guid MemberId(int n) => new($"{InventedActivity.IdPrefix}7a1a-7000-8000-{n:D12}");

    private static Guid RepresentativeId(int n) => new($"{InventedActivity.IdPrefix}7a1a-7000-9000-{n:D12}");

    private static Guid CommentId(string bill, int n) =>
        new($"{InventedActivity.IdPrefix}7a1a-7000-a000-{(uint)StableHash(bill):x8}{n:D4}");

    /// <summary>
    /// One open bill's invented activity: its record opened 18 to 75 days ago, the sides taken since, more of them lately; each
    /// argument's author on its side, written in the record's first half, marked by people of its side after their own side.
    /// </summary>
    public static InventedActivitySpec BuildOpen(
        DemoActivityBills.Bill bill, IReadOnlyList<KeyValuePair<Guid, DateTimeOffset>> pool, IReadOnlyDictionary<string, Guid> named, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(named);
        var random = new Random(StableHash(bill.Number));
        var opened = now.AddDays(-random.Next(18, 76)).AddMinutes(-random.Next(0, 1440));
        var authors = bill.Arguments.Select(a => (named[a.Author], a.Side)).DistinctBy(a => a.Item1).ToList();
        var authorIds = authors.Select(a => a.Item1).ToHashSet();
        var others = Shuffle(pool.Where(p => !authorIds.Contains(p.Key) && p.Value < now.AddHours(-1)).ToList(), random);
        var forCount = (int)Math.Round(bill.Positions * bill.ForShare);
        var positions = new List<InventedPosition>();
        var since = pool.ToDictionary(p => p.Key, p => p.Value);

        // The authors first, early in the record's life, each on their argument's side.
        foreach (var (id, side) in authors)
        {
            positions.Add(new InventedPosition(id, side, since[id], Later(opened, since[id]).AddHours(random.Next(1, 48))));
        }

        foreach (var (id, registered) in others)
        {
            if (positions.Count >= bill.Positions)
            {
                break;
            }

            var side = positions.Count(p => p.Side == Sides.For) < forCount && (positions.Count(p => p.Side == Sides.Against) >= bill.Positions - forCount || random.NextDouble() < bill.ForShare)
                ? Sides.For
                : Sides.Against;
            var earliest = Later(opened, registered);
            var taken = earliest + TimeSpan.FromMinutes(Math.Pow(random.NextDouble(), 0.7) * Math.Max(1, (now - earliest).TotalMinutes - 5));
            positions.Add(new InventedPosition(id, side, registered, taken));
        }

        var takenAt = positions.ToDictionary(p => p.UserId, p => p.TakenAt ?? opened);
        var arguments = new List<InventedArgument>();
        foreach (var a in bill.Arguments)
        {
            var author = named[a.Author];
            var at = Later(takenAt[author], opened.AddMinutes((now - opened).TotalMinutes * (0.05 + random.NextDouble() * 0.4))).AddMinutes(10);
            var side = positions.Where(p => p.Side == a.Side && p.UserId != author).ToList();
            var marks = Shuffle(side, random).Take((int)(side.Count * a.Marks))
                .Select(p =>
                {
                    var from = Later(at, takenAt[p.UserId]);
                    return new InventedMark(p.UserId, from + TimeSpan.FromMinutes(random.NextDouble() * Math.Max(1, (now - from).TotalMinutes - 1)));
                })
                .ToList();
            arguments.Add(new InventedArgument(author, a.Side, a.Text, at, marks));
        }

        var comments = new List<InventedComment>();
        var c = 0;
        foreach (var comment in bill.Comments ?? [])
        {
            var under = arguments[comment.Under];
            var at = under.At.AddDays(1 + random.Next(0, 6)).AddMinutes(random.Next(0, 600));
            at = at < now ? at : now.AddMinutes(-30);
            var agreements = Shuffle(positions.Select(p => p.UserId).ToList(), random).Take(comment.Agreements).ToList();
            comments.Add(new InventedComment(CommentId(bill.Number, ++c), comment.Under, named[comment.Author], comment.Text, at, agreements));
        }

        return new InventedActivitySpec(bill.Number, opened, positions, arguments, comments);
    }

    /// <summary>
    /// The finished bills given invented closed records: up to <see cref="ClosedBills"/>, a third of them from the votes where
    /// some MPs voted against, picked by a fixed rule from the bill numbers (not by date), so the same data picks the same bills.
    /// </summary>
    public static IReadOnlyList<AdoptionVote> PickClosed(IReadOnlyList<AdoptionVote> votes, int closedBills = ClosedBills)
    {
        ArgumentNullException.ThrowIfNull(votes);
        var usable = votes.Where(v => !DemoActivityBills.Excluded.Contains(v.BillNumber) && AnalyticsRules.SeimasMajorityFor(v.Numbers) is not null)
            .OrderBy(v => (uint)StableHash(v.BillNumber)).ThenBy(v => v.BillNumber, StringComparer.Ordinal).ToList();
        var contested = usable.Where(v => v.Numbers.Against >= 5).Take(closedBills / 3).ToList();
        return contested.Concat(usable.Except(contested).Take(closedBills - contested.Count)).OrderBy(v => v.HeldAt).ToList();
    }

    /// <summary>
    /// A finished bill's invented closed record: from 7 to about 2 200 sides from the members registered before the vote, taken
    /// between the record's opening (30 to 90 days before) and two hours before the vote; on most bills the members' majority
    /// is the Seimas's, on about three in ten it is not.
    /// </summary>
    public static InventedActivitySpec BuildClosed(AdoptionVote vote, bool seimasFor, IReadOnlyList<KeyValuePair<Guid, DateTimeOffset>> pool)
    {
        ArgumentNullException.ThrowIfNull(vote);
        ArgumentNullException.ThrowIfNull(pool);
        var random = new Random(StableHash(vote.BillNumber) ^ 0x5eed);
        var opened = vote.HeldAt.AddDays(-random.Next(30, 91));
        var latest = vote.HeldAt.AddHours(-2);
        var roll = random.NextDouble();
        var count = roll < 0.15 ? random.Next(7, 20) : roll < 0.7 ? random.Next(40, 900) : random.Next(900, 2200);
        var agrees = random.NextDouble() < 0.7;
        var larger = 0.52 + random.NextDouble() * 0.33;
        var membersFor = agrees == seimasFor;
        var chosen = Shuffle(pool.Where(p => p.Value < latest.AddHours(-1)).ToList(), random).Take(count).ToList();
        var forCount = (int)Math.Ceiling(chosen.Count * (membersFor ? larger : 1 - larger));
        if (forCount * 2 == chosen.Count)
        {
            forCount += membersFor ? 1 : -1; // no tie: a tie has no majority and the bill would not be compared
        }

        var positions = chosen.Select((p, i) =>
            {
                var earliest = Later(opened, p.Value);
                var taken = earliest + TimeSpan.FromMinutes(random.NextDouble() * Math.Max(1, (latest - earliest).TotalMinutes));
                return new InventedPosition(p.Key, i < forCount ? Sides.For : Sides.Against, p.Value, taken);
            })
            .ToList();
        return new InventedActivitySpec(vote.BillNumber, opened, Shuffle(positions, random), [], []);
    }

    /// <summary>
    /// The past weeks' weekly summaries (design 8.4), as dry runs: each Monday at 07:00 Vilnius of the last
    /// <see cref="PastSummaries"/> weeks the Seimas sat, the invented open bills that had reached the default threshold then
    /// and were new to the summaries or changed by at least the notable share since the last one that held them.
    /// </summary>
    private static async Task<int> WriteSummariesAsync(
        string connectionString, List<(Guid Record, string Bill)> records, IReadOnlyDictionary<string, string> titles, DateTimeOffset now, int weeks, CancellationToken cancellationToken)
    {
        if (records.Count == 0 || weeks == 0)
        {
            return 0;
        }

        var today = VilniusTime.DateOf(now);
        var sitting = await ParliamentMaintenance.SittingDaysAsync(connectionString, today.AddDays(-7 * 12), today, cancellationToken);
        var mondays = sitting.Select(d => d.AddDays(-(((int)d.DayOfWeek + 6) % 7))).Distinct()
            .Where(m => VilniusTime.StartOf(m).AddHours(7) < now.AddHours(-1)).OrderBy(m => m).TakeLast(weeks).ToList();
        var threshold = SettingKeys.DigestThresholdDefault.Default;
        var notable = SettingKeys.DigestNotableChange.Default;
        var last = new Dictionary<Guid, int>();
        var runs = new List<InventedDigestRun>();
        foreach (var monday in mondays)
        {
            var at = VilniusTime.StartOf(monday).AddHours(7);
            var snapshot = await InventedActivity.SnapshotAsync(connectionString, records.Select(r => r.Record).ToList(), at, cancellationToken);
            var items = snapshot
                .Where(s => s.For + s.Against >= threshold)
                .Where(s => !last.TryGetValue(s.RecordId, out var before) || Math.Abs(s.For + s.Against - before) >= before * notable)
                .OrderByDescending(s => s.For + s.Against)
                .Select(s => new InventedDigestItem(s.RecordId, s.BillNumber, titles[s.BillNumber], s.For, s.ForOlderThan30Days, s.Against, s.AgainstOlderThan30Days,
                    s.ForReasons, s.AgainstReasons, s.Reasons))
                .ToList();
            foreach (var item in items)
            {
                last[item.RecordId] = item.For + item.Against;
            }

            runs.Add(new InventedDigestRun(new Guid($"{InventedActivity.IdPrefix}7a1a-7000-b000-0000{monday:yyyyMMdd}"), monday, at, items));
        }

        var mps = await ParliamentMaintenance.SittingMpsWithAddressAsync(connectionString, today, cancellationToken);
        return await InventedDigests.WriteDryRunsAsync(connectionString, runs, mps, cancellationToken);
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static List<T> Shuffle<T>(List<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    /// <summary>A hash of a bill number that is the same in every process (string.GetHashCode is not).</summary>
    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var ch in text)
            {
                hash = (hash ^ ch) * 16777619;
            }

            return hash;
        }
    }
}
