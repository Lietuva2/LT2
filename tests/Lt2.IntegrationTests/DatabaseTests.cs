using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>The audit trigger, schema ownership (design 5.5) and the outbox dispatcher with SKIP LOCKED (design 5.4).
/// What each role may do is in <see cref="DatabaseRoleTests"/>.</summary>
public sealed class DatabaseTests(DatabaseFixture database)
{
    [Fact]
    public async Task Even_the_owner_cannot_change_the_audit_log()
    {
        var error = await Should.ThrowAsync<PostgresException>(() => database.ExecuteAsync("UPDATE audit.event SET action = 'x'"));
        error.MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task Every_module_schema_exists_and_is_owned_by_the_migrator()
    {
        foreach (var schema in new[] { "identity", "notify", "audit", "config", "parliament", "appeals" })
        {
            (await database.ScalarAsync<string>("SELECT nspowner::regrole::text FROM pg_namespace WHERE nspname = @s", ("s", schema)))
                .ShouldBe(DatabaseRoles.Migrator, schema);
        }
    }

    [Fact]
    public async Task The_worker_sends_outbox_messages_and_removes_them()
    {
        var transport = new CapturingTransport();
        await using var provider = WorkerServices(transport);
        var dispatcher = provider.GetRequiredService<INotificationsDispatcher>();
        var email = TestData.Email();
        await EnqueueAsync(provider, email);

        while (await dispatcher.DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        var sent = transport.Sent.Single(m => m.To == email);
        sent.Subject.ShouldBe("Lietuva 2.0 prisijungimo kodas: 246810");
        (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @to", ("to", email))).ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_send_is_kept_and_retried_later()
    {
        var transport = new CapturingTransport { Fail = true };
        await using var provider = WorkerServices(transport);
        var email = TestData.Email();
        await EnqueueAsync(provider, email);

        while (await provider.GetRequiredService<INotificationsDispatcher>().DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        (await database.ScalarAsync<int>("SELECT attempts FROM notify.outbox WHERE payload->>'to' = @to", ("to", email))).ShouldBe(1);
        (await database.ScalarAsync<bool>("SELECT due_at > now() FROM notify.outbox WHERE payload->>'to' = @to", ("to", email))).ShouldBeTrue();
        await database.ExecuteAsync("DELETE FROM notify.outbox WHERE payload->>'to' = @to", ("to", email));
    }

    [Fact]
    public async Task Two_dispatchers_never_take_the_same_message()
    {
        var transport = new CapturingTransport { Delay = TimeSpan.FromMilliseconds(200) };
        await using var provider = WorkerServices(transport);
        var emails = Enumerable.Range(0, 6).Select(_ => TestData.Email()).ToList();
        foreach (var email in emails)
        {
            await EnqueueAsync(provider, email);
        }

        var dispatcher = provider.GetRequiredService<INotificationsDispatcher>();
        await Task.WhenAll(
            Task.Run(() => dispatcher.DispatchDueAsync(CancellationToken.None)),
            Task.Run(() => dispatcher.DispatchDueAsync(CancellationToken.None)));
        while (await dispatcher.DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        transport.Sent.Where(m => emails.Contains(m.To)).Select(m => m.To).ShouldBe(emails, ignoreOrder: true);
    }

    [Fact]
    public async Task A_sign_in_code_is_sent_before_bulk_mail_that_waited_longer()
    {
        var transport = new CapturingTransport();
        await using var provider = WorkerServices(transport, batchSize: 1);
        var dispatcher = provider.GetRequiredService<INotificationsDispatcher>();
        while (await dispatcher.DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        // Follow emails due an hour ago, then a code due now (design 6.8: codes go first).
        var follower = TestData.Email();
        await using (var connection = await provider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            for (var i = 0; i < 3; i++)
            {
                await provider.GetRequiredService<IOutbox>().EnqueueAsync(
                    OutboxMessage.Create(
                        MessageKinds.FollowPlannedEmail,
                        new FollowPlannedEmail(follower, "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", null, new DateOnly(2026, 12, 1), "/seimas/XVP-0412", "/paskyra"),
                        DateTimeOffset.UtcNow.AddHours(-1)),
                    transaction);
            }

            await transaction.CommitAsync();
        }

        var email = TestData.Email();
        await EnqueueAsync(provider, email);

        var before = transport.Sent.Count;
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        transport.Sent.Skip(before).Select(m => m.To).ShouldBe([email], "the one message of the batch is the code");
        while (await dispatcher.DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        transport.Sent.Count(m => m.To == follower).ShouldBe(3);
    }

    [Fact]
    public async Task A_failure_keeps_only_the_type_of_the_error_and_old_failures_are_purged()
    {
        var transport = new CapturingTransport { Fail = true };
        await using var provider = WorkerServices(transport);
        var email = TestData.Email();
        await EnqueueAsync(provider, email);
        while (await provider.GetRequiredService<INotificationsDispatcher>().DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        (await database.ScalarAsync<string>("SELECT last_error FROM notify.outbox WHERE payload->>'to' = @to", ("to", email)))
            .ShouldBe("InvalidOperationException", "never the error's text, which can quote the address");

        await database.ExecuteAsync("UPDATE notify.outbox SET failed_at = now() - interval '31 days' WHERE payload->>'to' = @to", ("to", email));
        (await provider.GetRequiredService<INotificationsDispatcher>().PurgeFailedAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @to", ("to", email))).ShouldBe(0);
    }

    /// <summary>
    /// Design 6.8 and the log rule of AGENTS.md, on the worker's side: a send that fails with an error quoting the address
    /// leaves the address and the error's text in no log line, at any level, the exception included.
    /// </summary>
    [Fact]
    public async Task A_failed_send_logs_neither_the_address_nor_the_errors_text()
    {
        var logs = new CollectingLogs();
        var transport = new CapturingTransport { Fail = true };
        await using var provider = WorkerServices(transport, logs: logs);
        var email = TestData.Email();
        await EnqueueAsync(provider, email);
        while (await provider.GetRequiredService<INotificationsDispatcher>().DispatchDueAsync(CancellationToken.None) > 0)
        {
        }

        var lines = logs.Lines;
        lines.ShouldContain(l => l.Contains("failed", StringComparison.Ordinal), "the failure was logged");
        foreach (var line in lines)
        {
            line.ShouldNotContain(email, Case.Insensitive);
            line.ShouldNotContain("@pavyzdys.test", Case.Insensitive);
            line.ShouldNotContain("rejected", Case.Insensitive, "the error's text, which quotes the address");
        }

        await database.ExecuteAsync("DELETE FROM notify.outbox WHERE payload->>'to' = @to", ("to", email));
    }

    private ServiceProvider WorkerServices(CapturingTransport transport, int batchSize = 20, CollectingLogs? logs = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Security:MailHashKey"] = "test-mail-hash-key",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logs is not null)
            {
                b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
            }
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(NpgsqlDataSource.Create(database.WorkerConnectionString));
        services.AddNotificationsModule(configuration);
        services.AddNotificationsDispatcher(configuration);
        services.AddSingleton<IEmailTransport>(transport);
        return services.BuildServiceProvider();
    }

    private static async Task EnqueueAsync(IServiceProvider provider, string email)
    {
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await provider.GetRequiredService<IOutbox>().EnqueueAsync(
            OutboxMessage.Create(MessageKinds.SignInCodeEmail, new SignInCodeEmail(email, "246810", 10)),
            transaction);
        await transaction.CommitAsync();
    }

    /// <summary>Every log line at every level, with its exception, as text.</summary>
    private sealed class CollectingLogs : ILoggerProvider
    {
        private readonly List<string> _lines = [];

        public List<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CollectingLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._lines)
                {
                    owner._lines.Add(formatter(state, exception) + " " + exception);
                }
            }
        }
    }
    private sealed class CapturingTransport : IEmailTransport
    {
        private readonly List<EmailMessage> _sent = [];

        public bool Fail { get; init; }

        public TimeSpan Delay { get; init; }

        public List<EmailMessage> Sent
        {
            get
            {
                lock (_sent)
                {
                    return _sent.ToList();
                }
            }
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            await Task.Delay(Delay, cancellationToken);
            if (Fail)
            {
                throw new InvalidOperationException($"SMTP is down (test): 550 <{message.To}> rejected.");
            }

            lock (_sent)
            {
                _sent.Add(message);
            }
        }
    }
}
