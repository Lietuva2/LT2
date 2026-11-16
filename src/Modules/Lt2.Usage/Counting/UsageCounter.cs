using Lt2.SharedKernel;
using Lt2.Usage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lt2.Usage.Counting;

/// <summary>
/// Design 8.27: the counter in memory. A page view adds the visitor's (or the account's) keyed hash to the sketches of its
/// day, month and year, one view to the day and to the page, a referring site once per visitor and day, and marks the
/// visitor as seen for "now on the site" (under a salt of this process, never stored). The address, the user agent and
/// the account are not kept.
/// <para>
/// Every minute <see cref="FlushAsync"/> merges the sketches into <c>usage.sketch</c>, writes the counts into
/// <c>usage.total</c>, <c>usage.page</c> and <c>usage.referrer</c>, and deletes the salt and the sketch of every period
/// that has ended (its count written first). Visitors and members go into the sketches only once the stored salts have been
/// read (the first flush that reaches the database): before that, page views are counted and distinct visitors are not,
/// so a start with the database away counts nobody twice. One web host counts; should two run at once, an advisory lock
/// keeps their writes apart and each adopts the stored salts.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability", "CA1001", Justification = "The semaphore never makes a wait handle, so it holds nothing to free; disposing it would race the last flush at shutdown.")]
internal sealed class UsageCounter(IServiceScopeFactory scopes, TimeProvider clock, ILogger<UsageCounter> logger) : IUsageCounter
{
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended('lt2.usage', 0))";

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly Dictionary<Key, byte[]> _salts = [];

    /// <summary>"Now on the site" and the referring sites already counted today, both under <see cref="_processSalt"/>.</summary>
    private readonly Dictionary<(string Audience, ulong Hash), DateTimeOffset> _online = [];
    private readonly Dictionary<ulong, HashSet<string>> _referred = [];
    private readonly byte[] _processSalt = Sketch.NewSalt();
    private DateOnly _referredDay;
    private bool _ready;
    private Pending _pending = new();

    private readonly record struct Key(string Period, DateOnly Starts, string Audience);

    private sealed class Views
    {
        public int Visitor;
        public int Member;
        public int Crawler;
    }

    private sealed class Pending
    {
        public Dictionary<Key, Sketch> Sketches { get; } = [];

        public Dictionary<DateOnly, Views> Views { get; } = [];

        public Dictionary<DateOnly, Dictionary<string, int>> Pages { get; } = [];

        public Dictionary<DateOnly, Dictionary<string, int>> Hosts { get; } = [];
    }

    /// <summary>Whether the stored salts have been read, so distinct visitors and members are counted.</summary>
    internal bool Ready
    {
        get
        {
            lock (_gate)
            {
                return _ready;
            }
        }
    }

    public void Count(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!UsageRules.IsPageView(view.Method, view.Path, view.Status, view.ContentType, view.Prefetch))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var day = VilniusTime.DateOf(now);
        var member = view.AccountId is not null;
        if (!member && UsageRules.IsCrawler(view.UserAgent))
        {
            lock (_gate)
            {
                ViewsOf(day).Crawler++;
            }

            return;
        }

        var network = member ? null : UsageRules.Network(view.Address);
        byte[]? data = member ? Sketch.MemberData(view.AccountId!.Value) : network is null ? null : Sketch.VisitorData(network, view.UserAgent!);
        var audience = member ? UsageRules.Member : UsageRules.Visitor;
        var path = UsageRules.CountedPath(view.Template, view.RouteValues);
        var host = UsageRules.ReferrerHost(view.Referrer, view.OwnHost);

        lock (_gate)
        {
            if (_referredDay != day)
            {
                _referred.Clear();
                _referredDay = day;
            }

            ulong? local = data is null ? null : Sketch.Hash(_processSalt, data);
            if (local is { } seen)
            {
                _online[(audience, seen)] = now;
            }

            if (data is not null && _ready)
            {
                foreach (var (period, starts) in UsageRules.PeriodsOf(day))
                {
                    var key = new Key(period, starts, audience);
                    if (!_salts.TryGetValue(key, out var salt))
                    {
                        salt = _salts[key] = Sketch.NewSalt();
                    }

                    if (!_pending.Sketches.TryGetValue(key, out var sketch))
                    {
                        sketch = _pending.Sketches[key] = new Sketch();
                    }

                    sketch.Add(Sketch.Hash(salt, data));
                }
            }

            var views = ViewsOf(day);
            if (member)
            {
                views.Member++;
            }
            else
            {
                views.Visitor++;
            }

            if (UsageRules.IsListed(path))
            {
                Add(_pending.Pages, day, path, UsageRules.PathsPerDay, UsageRules.OtherPaths, 1);
            }

            if (host is not null && local is { } referredBy && FirstReferral(referredBy, host))
            {
                Add(_pending.Hosts, day, host, UsageRules.HostsPerDay, UsageRules.OtherHosts, 1);
            }
        }
    }

    /// <summary>A visitor's first referral from <paramref name="host"/> today, and at most <see cref="UsageRules.HostsPerVisitor"/> hosts.</summary>
    private bool FirstReferral(ulong visitor, string host)
    {
        if (!_referred.TryGetValue(visitor, out var hosts))
        {
            hosts = _referred[visitor] = new HashSet<string>(StringComparer.Ordinal);
        }

        return hosts.Count < UsageRules.HostsPerVisitor && hosts.Add(host);
    }

    /// <summary>Adds to a day's counts by name, at most <paramref name="cap"/> names in memory; the rest go under <paramref name="other"/>.</summary>
    private static void Add(Dictionary<DateOnly, Dictionary<string, int>> counts, DateOnly day, string name, int cap, string other, int views)
    {
        if (!counts.TryGetValue(day, out var names))
        {
            names = counts[day] = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var key = names.ContainsKey(name) || names.Count < cap - 1 ? name : other;
        names[key] = names.GetValueOrDefault(key) + views;
    }

    public UsageOnline Online()
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            PruneOnline(now);
            return new UsageOnline(
                _online.Keys.Count(k => k.Audience == UsageRules.Visitor),
                _online.Keys.Count(k => k.Audience == UsageRules.Member));
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _flushing.WaitAsync(cancellationToken);
        try
        {
            Pending taken;
            lock (_gate)
            {
                taken = _pending;
                _pending = new Pending();
                PruneOnline(clock.GetUtcNow());
            }

            try
            {
                await WriteAsync(taken, cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    Restore(taken);
                }

                if (ex is OperationCanceledException)
                {
                    throw;
                }

                logger.LogWarning(ex, "Usage counts could not be written; they are kept for the next attempt");
            }
        }
        finally
        {
            _flushing.Release();
        }
    }

    private async Task WriteAsync(Pending taken, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UsageDb>();
        var today = VilniusTime.Today(clock);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);

        var rows = await db.Sketches.ToListAsync(cancellationToken);
        Dictionary<Key, byte[]> salts;
        lock (_gate)
        {
            salts = new Dictionary<Key, byte[]>(_salts);
        }

        var late = new List<(Key Key, int Estimate)>();
        foreach (var (key, sketch) in taken.Sketches)
        {
            var row = rows.FirstOrDefault(r => r.Period == key.Period && r.Starts == key.Starts && r.Audience == key.Audience);
            if (row is null)
            {
                if (UsageRules.HasEnded(key.Period, key.Starts, today))
                {
                    // An ended period with no stored sketch: either all of it came in this minute (its first visitor just
                    // before midnight), or its count is final and its salt gone. Its count becomes the larger of the two;
                    // no salt is stored again.
                    late.Add((key, sketch.Estimate()));
                    continue;
                }

                row = new SketchRow { Period = key.Period, Starts = key.Starts, Audience = key.Audience, Salt = salts[key], Registers = sketch.ToBytes() };
                db.Sketches.Add(row);
                rows.Add(row);
                continue;
            }

            // Counted with this host's own salt while another instance had stored one first (two web hosts at a period's
            // start): merged all the same, so such a visitor may count twice that period, never not at all.
            var merged = Sketch.From(row.Registers);
            merged.Merge(sketch);
            row.Registers = merged.ToBytes();
        }

        var totals = new Dictionary<(string Period, DateOnly Starts), TotalRow>();
        async Task<TotalRow> TotalAsync(string period, DateOnly starts)
        {
            if (!totals.TryGetValue((period, starts), out var total))
            {
                total = await db.Totals.FindAsync([period, starts], cancellationToken);
                if (total is null)
                {
                    total = new TotalRow { Period = period, Starts = starts };
                    db.Totals.Add(total);
                }

                totals[(period, starts)] = total;
            }

            return total;
        }

        foreach (var row in rows)
        {
            var total = await TotalAsync(row.Period, row.Starts);
            var estimate = Sketch.From(row.Registers).Estimate();
            if (row.Audience == UsageRules.Member)
            {
                total.Members = estimate;
            }
            else
            {
                total.Visitors = estimate;
            }
        }

        foreach (var (key, estimate) in late)
        {
            var total = await TotalAsync(key.Period, key.Starts);
            if (key.Audience == UsageRules.Member)
            {
                total.Members = Math.Max(total.Members, estimate);
            }
            else
            {
                total.Visitors = Math.Max(total.Visitors, estimate);
            }
        }

        foreach (var (day, views) in taken.Views)
        {
            foreach (var (period, starts) in UsageRules.PeriodsOf(day))
            {
                var total = await TotalAsync(period, starts);
                total.VisitorViews += views.Visitor;
                total.MemberViews += views.Member;
                total.CrawlerViews += views.Crawler;
            }
        }

        foreach (var (day, names) in taken.Pages)
        {
            var keys = names.Keys.Append(UsageRules.OtherPaths).ToList();
            var stored = await db.Pages.Where(p => p.Day == day && keys.Contains(p.Path)).ToDictionaryAsync(p => p.Path, cancellationToken);
            var count = await db.Pages.CountAsync(p => p.Day == day, cancellationToken);
            foreach (var (name, views, added) in Capped(names, stored.Keys, count, UsageRules.PathsPerDay, UsageRules.OtherPaths))
            {
                if (added)
                {
                    var row = new PageRow { Day = day, Path = name, Views = views };
                    db.Pages.Add(row);
                    stored[name] = row;
                }
                else
                {
                    stored[name].Views += views;
                }
            }
        }

        foreach (var (day, names) in taken.Hosts)
        {
            var keys = names.Keys.Append(UsageRules.OtherHosts).ToList();
            var stored = await db.Referrers.Where(r => r.Day == day && keys.Contains(r.Host)).ToDictionaryAsync(r => r.Host, cancellationToken);
            var count = await db.Referrers.CountAsync(r => r.Day == day, cancellationToken);
            foreach (var (name, views, added) in Capped(names, stored.Keys, count, UsageRules.HostsPerDay, UsageRules.OtherHosts))
            {
                if (added)
                {
                    var row = new ReferrerRow { Day = day, Host = name, Views = views };
                    db.Referrers.Add(row);
                    stored[name] = row;
                }
                else
                {
                    stored[name].Views += views;
                }
            }
        }

        // A period that is over: its count is written above, so its salt and sketch go. Nothing links anyone after this.
        var ended = rows.Where(r => UsageRules.HasEnded(r.Period, r.Starts, today)).ToList();
        db.Sketches.RemoveRange(ended);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        lock (_gate)
        {
            // Counting goes on with the stored salts (another instance's, or this one's before a restart); an ended period's
            // salt is forgotten here too, also one that never reached the database.
            foreach (var row in rows.Where(r => !UsageRules.HasEnded(r.Period, r.Starts, today)))
            {
                _salts[new Key(row.Period, row.Starts, row.Audience)] = row.Salt;
            }

            foreach (var key in _salts.Keys.Where(k => UsageRules.HasEnded(k.Period, k.Starts, today)).ToList())
            {
                _salts.Remove(key);
            }

            _ready = true;
        }
    }

    /// <summary>
    /// Where each name's views go: a name already stored adds to its row; a new one gets a row while the day has fewer than
    /// <paramref name="cap"/> - 1, the most viewed first; the rest add to <paramref name="other"/> (one place kept for it).
    /// </summary>
    internal static IEnumerable<(string Name, int Views, bool Added)> Capped(
        IReadOnlyDictionary<string, int> names, IEnumerable<string> stored, int storedCount, int cap, string other)
    {
        var known = new HashSet<string>(stored, StringComparer.Ordinal);
        var rest = 0;
        foreach (var (name, views) in names.OrderByDescending(n => n.Value).ThenBy(n => n.Key, StringComparer.Ordinal))
        {
            if (name == other)
            {
                rest += views;
            }
            else if (known.Contains(name))
            {
                yield return (name, views, false);
            }
            else if (storedCount < cap - 1)
            {
                known.Add(name);
                storedCount++;
                yield return (name, views, true);
            }
            else
            {
                rest += views;
            }
        }

        if (rest > 0)
        {
            var added = !known.Contains(other);
            yield return (other, rest, added);
        }
    }

    private Views ViewsOf(DateOnly day)
    {
        if (!_pending.Views.TryGetValue(day, out var views))
        {
            views = _pending.Views[day] = new Views();
        }

        return views;
    }

    private void PruneOnline(DateTimeOffset now)
    {
        foreach (var (key, seen) in _online.ToList())
        {
            if (!UsageRules.IsOnline(seen, now))
            {
                _online.Remove(key);
            }
        }
    }

    private void Restore(Pending taken)
    {
        foreach (var (key, sketch) in taken.Sketches)
        {
            if (_pending.Sketches.TryGetValue(key, out var pending))
            {
                pending.Merge(sketch);
            }
            else
            {
                _pending.Sketches[key] = sketch;
            }
        }

        foreach (var (day, views) in taken.Views)
        {
            var into = ViewsOf(day);
            into.Visitor += views.Visitor;
            into.Member += views.Member;
            into.Crawler += views.Crawler;
        }

        foreach (var (day, names) in taken.Pages)
        {
            foreach (var (name, views) in names)
            {
                Add(_pending.Pages, day, name, UsageRules.PathsPerDay, UsageRules.OtherPaths, views);
            }
        }

        foreach (var (day, names) in taken.Hosts)
        {
            foreach (var (name, views) in names)
            {
                Add(_pending.Hosts, day, name, UsageRules.HostsPerDay, UsageRules.OtherHosts, views);
            }
        }
    }
}

/// <summary>Writes the counts at start (finishing periods that ended while the site was down), every minute, and at shutdown.</summary>
internal sealed class UsageFlushService(UsageCounter counter, ILogger<UsageFlushService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Before the server takes requests, so counting starts with the stored salts of the periods under way. Should the
        // database be away, page views are counted and distinct visitors wait until a flush reaches it (UsageCounter).
        try
        {
            await counter.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Usage counts could not be read at start");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await counter.FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await counter.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Usage counts could not be written at shutdown");
        }
    }
}
