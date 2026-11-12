using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    private ServiceProvider WorkerServices(CapturingTransport transport)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
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

    private sealed class CapturingTransport : IEmailTransport
    {
        private readonly List<EmailMessage> _sent = [];

        public bool Fail { get; init; }

        public TimeSpan Delay { get; init; }

        public IReadOnlyList<EmailMessage> Sent
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
                throw new InvalidOperationException("SMTP is down (test).");
            }

            lock (_sent)
            {
                _sent.Add(message);
            }
        }
    }
}
