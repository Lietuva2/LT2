using Lt2.SharedKernel;
using Lt2.Usage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lt2.Usage.Counting;

/// <summary>
/// Design 8.27: the counter in memory. A page view adds the visitor's (or the account's) keyed hash to the sketches of its
/// day, month and year, one view to the day, the path and the referring site, and marks the hash as seen for "now on the
/// site". Nothing else is kept: not the address, not the user agent, not the account, not which pages one person saw.
/// Every minute <see cref="FlushAsync"/> merges the sketches into <c>usage.sketch</c>, writes the counts into
/// <c>usage.total</c>, <c>usage.page</c> and <c>usage.referrer</c>, and deletes the salt and the sketch of every period
/// that has ended (its count written first). One web host counts; should two run at once, an advisory lock keeps their
/// writes apart and the second adopts the first one's salts.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability", "CA1001", Justification = "The semaphore never makes a wait handle, so it holds nothing to free; disposing it would race the last flush at shutdown.")]
internal sealed class UsageCounter(IServiceScopeFactory scopes, TimeProvider clock, ILogger<UsageCounter> logger) : IUsageCounter
{
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended('lt2.usage', 0))";

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly Dictionary<Key, byte[]> _salts = [];
    private readonly Dictionary<(string Audience, ulong Hash), DateTimeOffset> _online = [];
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

        public Dictionary<(DateOnly Day, string Path), int> Pages { get; } = [];

        public Dictionary<(DateOnly Day, string Host), int> Hosts { get; } = [];
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
        var path = UsageRules.PathOf(view.Path);
        var host = UsageRules.ReferrerHost(view.Referrer, view.OwnHost);

        lock (_gate)
        {
            if (data is not null)
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

                    var hash = Sketch.Hash(salt, data);
                    sketch.Add(hash);
                    if (period == UsageRules.Day)
                    {
                        _online[(audience, hash)] = now;
                    }
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

            _pending.Pages[(day, path)] = _pending.Pages.GetValueOrDefault((day, path)) + 1;
            if (host is not null)
            {
                _pending.Hosts[(day, host)] = _pending.Hosts.GetValueOrDefault((day, host)) + 1;
            }
        }
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

            // Counted with another salt before this host adopted the stored one (at start, or beside another instance): merged
            // all the same, so such a visitor may count twice that period, never not at all.
            var merged = Sketch.From(row.Registers);
            merged.Merge(sketch);
            row.Registers = merged.ToBytes();
        }

        // Counting goes on with the stored salts (another instance's, or this one's before a restart).
        lock (_gate)
        {
            foreach (var row in rows.Where(r => !UsageRules.HasEnded(r.Period, r.Starts, today)))
            {
                _salts[new Key(row.Period, row.Starts, row.Audience)] = row.Salt;
            }
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

        await AddCappedAsync(
            taken.Pages, UsageRules.PathsPerDay, UsageRules.OtherPaths,
            (day, ct) => db.Pages.CountAsync(p => p.Day == day, ct),
            async (day, name, ct) => await db.Pages.FindAsync([day, name], ct) is { } row ? (Action<int>)(v => row.Views += v) : null,
            (day, name, views) => db.Pages.Add(new PageRow { Day = day, Path = name, Views = views }),
            cancellationToken);
        await AddCappedAsync(
            taken.Hosts, UsageRules.HostsPerDay, UsageRules.OtherHosts,
            (day, ct) => db.Referrers.CountAsync(r => r.Day == day, ct),
            async (day, name, ct) => await db.Referrers.FindAsync([day, name], ct) is { } row ? (Action<int>)(v => row.Views += v) : null,
            (day, name, views) => db.Referrers.Add(new ReferrerRow { Day = day, Host = name, Views = views }),
            cancellationToken);

        // A period that is over: its count is written above, so its salt and sketch go. Nothing links anyone after this.
        var ended = rows.Where(r => UsageRules.HasEnded(r.Period, r.Starts, today)).ToList();
        db.Sketches.RemoveRange(ended);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        lock (_gate)
        {
            foreach (var row in ended)
            {
                _salts.Remove(new Key(row.Period, row.Starts, row.Audience));
            }
        }
    }

    /// <summary>Adds per-day counts by name, keeping at most <paramref name="cap"/> names a day and counting the rest under <paramref name="other"/>.</summary>
    private static async Task AddCappedAsync(
        Dictionary<(DateOnly Day, string Name), int> counts,
        int cap,
        string other,
        Func<DateOnly, CancellationToken, Task<int>> countOfDay,
        Func<DateOnly, string, CancellationToken, Task<Action<int>?>> existing,
        Action<DateOnly, string, int> add,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<DateOnly, int>();
        foreach (var ((day, name), views) in counts.OrderByDescending(c => c.Value))
        {
            if (await existing(day, name, cancellationToken) is { } increase)
            {
                increase(views);
                continue;
            }

            if (!names.TryGetValue(day, out var known))
            {
                known = await countOfDay(day, cancellationToken);
            }

            // One place is kept for the rest.
            if (known < cap - 1)
            {
                add(day, name, views);
                names[day] = known + 1;
            }
            else if (await existing(day, other, cancellationToken) is { } rest)
            {
                rest(views);
                names[day] = known;
            }
            else
            {
                add(day, other, views);
                names[day] = known + 1;
            }
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

        foreach (var (key, views) in taken.Pages)
        {
            _pending.Pages[key] = _pending.Pages.GetValueOrDefault(key) + views;
        }

        foreach (var (key, views) in taken.Hosts)
        {
            _pending.Hosts[key] = _pending.Hosts.GetValueOrDefault(key) + views;
        }
    }
}

/// <summary>Writes the counts at start (finishing periods that ended while the site was down), every minute, and at shutdown.</summary>
internal sealed class UsageFlushService(UsageCounter counter, ILogger<UsageFlushService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Before the server takes requests, so counting starts with the stored salts of the periods under way.
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
