using System.Text;
using Lt2.Migrator;
using Lt2.Parliament.ImportTests.Client;
using Lt2.Parliament.ImportTests.StandIn;
using Lt2.SeimasStandIn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Lt2.Parliament.ImportTests.Import;

/// <summary>A real PostgreSQL in a container, prepared by the migrator exactly as in production; the import connects as <c>lt2_worker</c>.</summary>
public sealed class ImportDatabase : IAsyncLifetime
{
    private const string WorkerPassword = "worker-test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("lt2").WithUsername("postgres").WithPassword("postgres-test-password").Build();

    public string AdminConnectionString => _container.GetConnectionString();

    /// <summary>For tests that hold a session lock: closing really ends the session, so the lock goes with it.</summary>
    public string UnpooledAdminConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString) { Pooling = false }.ConnectionString;

    public string WorkerConnectionString =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Username = DatabaseRoles.Worker, Password = WorkerPassword }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await RunMigratorAsync();
    }

    /// <summary>The migrator, as a deploy runs it, with optional commands such as <c>--reset-parliament</c>.</summary>
    public async Task RunMigratorAsync(params string[] args)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Admin"] = AdminConnectionString,
            ["ConnectionStrings:Lt2"] = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Username = DatabaseRoles.Migrator, Password = "migrator-test-password" }.ConnectionString,
            ["Database:Password"] = "migrator-test-password",
            ["Database:AppPassword"] = "app-test-password",
            ["Database:WorkerPassword"] = WorkerPassword,
            ["Database:ReadonlyPassword"] = "readonly-test-password",
        }).Build();
        await MigrationRunner.RunAsync(configuration, args, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Empties the parliament schema between tests.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            DO $$ DECLARE t record; BEGIN
              FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'parliament' AND tablename <> '__ef_migrations_history' LOOP
                EXECUTE format('TRUNCATE TABLE parliament.%I CASCADE', t.tablename);
              END LOOP;
            END $$;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task<List<string>> RowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.IsDBNull(i) ? "∅" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
            }

            rows.Add(string.Join(" | ", values));
        }

        return rows;
    }

    /// <summary>
    /// Every data row by natural keys (Seimas identifiers, bill numbers), without our own UUIDs or fetch times, so two
    /// imports of the same data compare equal however they got there.
    /// </summary>
    public async Task<string> SnapshotAsync()
    {
        string[] queries =
        [
            "SELECT 'term', id, name, \"from\", \"to\" FROM parliament.term ORDER BY id",
            "SELECT 'session', id, term_id, name, \"from\", \"to\" FROM parliament.session ORDER BY id",
            "SELECT 'sitting', id, session_id, number, kind, starts_at, ends_at FROM parliament.sitting ORDER BY id",
            "SELECT 'person', seimas_person_id, first_name, last_name, slug, gender FROM parliament.person ORDER BY seimas_person_id",
            "SELECT 'mandate', p.seimas_person_id, m.term_id, m.\"from\", m.\"to\", m.nominating_party, m.elected_how, m.constituency, m.terms_served FROM parliament.mandate m JOIN parliament.person p ON p.id = m.person_id ORDER BY 2, 3",
            "SELECT 'body', seimas_id, term_id, kind, name, slug FROM parliament.body ORDER BY seimas_id",
            "SELECT 'membership', p.seimas_person_id, b.seimas_id, m.role, m.\"from\", m.\"to\" FROM parliament.membership m JOIN parliament.person p ON p.id = m.person_id JOIN parliament.body b ON b.id = m.body_id ORDER BY 2, 3, 5",
            "SELECT 'contact', p.seimas_person_id, c.kind, c.value FROM parliament.contact c JOIN parliament.person p ON p.id = c.person_id ORDER BY 2, 3, 4",
            "SELECT 'bill', number, title, e_seimas_url FROM parliament.bill ORDER BY number",
            "SELECT 'item', i.id, i.sitting_id, b.number, i.number, i.title, i.stage FROM parliament.agenda_item i LEFT JOIN parliament.bill b ON b.id = i.bill_id ORDER BY i.id",
            "SELECT 'vote', v.id, v.agenda_item_id, b.number, v.held_at, v.wording, v.type, v.kind, v.stage, v.is_consensus, v.has_result, v.for, v.against, v.abstain, v.voted, v.seats, v.official_note FROM parliament.vote v LEFT JOIN parliament.bill b ON b.id = v.bill_id ORDER BY v.id",
            "SELECT 'ballot', x.vote_id, p.seimas_person_id, x.faction_label, f.seimas_id, x.choice FROM parliament.ballot x JOIN parliament.person p ON p.id = x.person_id LEFT JOIN parliament.body f ON f.id = x.faction_id ORDER BY 2, 3",
            "SELECT 'position', x.vote_id, x.faction_label, f.seimas_id, x.position, x.for, x.against, x.abstain, x.did_not_vote FROM parliament.faction_position x LEFT JOIN parliament.body f ON f.id = x.faction_id ORDER BY 2, 3",
            "SELECT 'stat', p.seimas_person_id, s.term_id, s.votes_held, s.votes_taken_part, s.votes_with_faction_position, s.voted_with_faction, s.free_votes FROM parliament.mp_stat s JOIN parliament.person p ON p.id = s.person_id ORDER BY 2",
        ];
        var text = new StringBuilder();
        foreach (var query in queries)
        {
            foreach (var row in await RowsAsync(query))
            {
                text.AppendLine(row);
            }
        }

        return text.ToString();
    }
}

/// <summary>"Now" for the import, settable; timers and delays stay real (Polly's retries use them).</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>The import as the worker composes it, against the in-process stand-in.</summary>
internal sealed class ImportHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    /// <param name="settings">Configuration that replaces the defaults below (for example another agenda address).</param>
    /// <param name="agendaHandler">The agenda page's server, when it is not the stand-in (a page that never answers).</param>
    public ImportHarness(ImportDatabase database, StandInResponses? responses = null, IReadOnlyDictionary<string, string?>? settings = null, HttpMessageHandler? agendaHandler = null)
    {
        StandIn = new StandInHandler(responses ?? StandInFixture.Responses());
        var values = new Dictionary<string, string?>
        {
            ["Seimas:BaseAddress"] = "http://seimas-standin.test/sip/",
            ["Seimas:TermId"] = "10",
            ["Seimas:AttemptTimeoutSeconds"] = "2",
            ["Seimas:TotalTimeoutSeconds"] = "10",
            ["Seimas:RetryAttempts"] = "2",
            ["Seimas:RetryDelaySeconds"] = "0.01",
            ["Seimas:MinimumIntervalMilliseconds"] = "0",
            ["Outbound:AllowedHosts:0"] = "seimas-standin.test",
            ["Agenda:BaseAddress"] = "http://seimas-standin.test/",
            ["Agenda:TimeoutSeconds"] = "2",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddProvider(Logs));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(database.WorkerConnectionString).Build());
        services.AddParliamentModule();
        services.AddParliamentImport(configuration);
        services.AddHttpClient(Lt2.Parliament.Import.Client.SeimasClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => StandIn)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        services.AddHttpClient(Lt2.Parliament.Import.Agenda.AgendaWatcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => agendaHandler ?? StandIn)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        _services = services.BuildServiceProvider();
    }

    public TestClock Clock { get; } = new();

    public StandInHandler StandIn { get; }

    /// <summary>Error lines the services logged (the agenda watcher's alert).</summary>
    public CollectingLogs Logs { get; } = new();

    public async Task<AgendaOutcome> RunAgendaAsync()
    {
        StandInHandler.Today = () => DateOnly.FromDateTime(Clock.Now.UtcDateTime);
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAgendaWatch>().RunAsync(CancellationToken.None);
    }

    public async Task<IReadOnlyList<Lt2.SharedKernel.PlannedDate>> PlannedAsync(DateOnly from, DateOnly to)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Lt2.SharedKernel.IPlannedAgenda>().BetweenAsync(from, to, CancellationToken.None);
    }

    /// <summary>A run as the scheduled job starts it, or, with <paramref name="force"/>, as an administrator's manual job does.</summary>
    public async Task<ImportOutcome> RunAsync(ImportParts force = ImportParts.None)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IParliamentImport>().RunAsync(force, CancellationToken.None);
    }

    public async Task<bool> IsDueAsync() => (await DueAsync()).IsDue;

    public async Task<ImportDue> DueAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IParliamentImport>().DueAsync(CancellationToken.None);
    }

    public async Task<bool> IsInventedAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IParliamentDataNotice>().IsParliamentDataInventedAsync(CancellationToken.None);
    }

    /// <summary>Requests the stand-in received since the last call.</summary>
    public List<string> TakeRequests()
    {
        lock (StandIn.Requests)
        {
            var taken = StandIn.Requests.ToList();
            StandIn.Requests.Clear();
            return taken;
        }
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

/// <summary>Collects error-level log lines.</summary>
internal sealed class CollectingLogs : ILoggerProvider
{
    public List<string> Errors { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CollectingLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                lock (owner.Errors)
                {
                    owner.Errors.Add(formatter(state, exception));
                }
            }
        }
    }
}