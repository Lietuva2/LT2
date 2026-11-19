using System.Text.Json;
using Lt2.Audit;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.19.3, 8.19.6: the three email streams in the outbox dispatcher. A stream at its daily ceiling, or whose sender the
/// mail server refused, waits while the other streams go on; a sender's limit is never a bad recipient.
/// </summary>
public sealed class EmailStreamDispatchTests(DatabaseFixture database)
{
    private static readonly string[] Ceilings = ["Email:CeilingCodesPerDay", "Email:CeilingNoticesPerDay", "Email:CeilingMpPerDay"];

    private Guid[]? _known;

    private Guid[] _knownSuppressions => _known ?? [];

    private async Task ResetAsync()
    {
        if (_known is null)
        {
            _known = await database.ScalarAsync<Guid[]>("SELECT coalesce(array_agg(id), ARRAY[]::uuid[]) FROM notify.suppression") ?? [];
        }

        await database.ExecuteAsync("DELETE FROM notify.suppression WHERE id <> ALL(@known)", ("known", _knownSuppressions));
        await database.ExecuteAsync("DELETE FROM notify.outbox WHERE payload->>'to' LIKE '%@streams.pavyzdys.test'");
        await database.ExecuteAsync("DELETE FROM notify.stream_send");
        await database.ExecuteAsync("DELETE FROM config.setting WHERE key LIKE 'Email:Ceiling%'");
    }

    /// <summary>
    /// The tests share one database and the dispatcher takes every due row, so what other tests left due is put a month ahead for
    /// the length of this test (and the ceiling counts only this test's mail).
    /// </summary>
    private async Task<Guid[]> ParkOthersAsync() =>
        await database.ScalarAsync<Guid[]>(
            "WITH moved AS (UPDATE notify.outbox SET due_at = due_at + interval '30 days' WHERE failed_at IS NULL AND due_at <= now() AND payload->>'to' NOT LIKE '%@streams.pavyzdys.test' RETURNING id) SELECT coalesce(array_agg(id), ARRAY[]::uuid[]) FROM moved")
        ?? [];

    private Task UnparkAsync(Guid[] ids) =>
        database.ExecuteAsync("UPDATE notify.outbox SET due_at = due_at - interval '30 days' WHERE id = ANY(@ids)", ("ids", ids));

    [Fact]
    public async Task A_notices_stream_at_its_ceiling_waits_while_codes_go_on_and_the_rest_goes_when_the_ceiling_is_lifted()
    {
        await ResetAsync();
        var parked = await ParkOthersAsync();
        try
        {
            await SetCeilingAsync("Email:CeilingNoticesPerDay", 2);
            (await database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'setting.changed' AND target = 'Email:CeilingNoticesPerDay' AND actor_role = 'migrator'"))
                .ShouldBeGreaterThanOrEqualTo(1, "a change of a ceiling is audited like any setting");

            var transport = new RecordingTransport();
            var notices = Enumerable.Range(0, 4).Select(i => $"pranesimas{i}@streams.pavyzdys.test").ToList();
            var code = "kodas@streams.pavyzdys.test";
            await using (var provider = WorkerServices(transport))
            {
                foreach (var to in notices)
                {
                    await EnqueueAsync(provider, MessageKinds.FollowPlannedEmail, Planned(to), DateTimeOffset.UtcNow.AddHours(-1));
                }

                await EnqueueAsync(provider, MessageKinds.SignInCodeEmail, new SignInCodeEmail(code, "246810", 10));
                await DrainAsync(provider);
            }

            transport.Sent.Count(m => m.Stream == "notices").ShouldBe(2, "the ceiling");
            transport.Sent.Single(m => m.To == code).Stream.ShouldBe("codes");
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' LIKE 'pranesimas%@streams.pavyzdys.test'")).ShouldBe(2, "the rest waits");
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' LIKE 'pranesimas%@streams.pavyzdys.test' AND attempts = 0 AND failed_at IS NULL")).ShouldBe(2, "not failed, not dropped, not counted as tried");
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.stream_send WHERE stream = 'notices'")).ShouldBe(2);
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.stream_send WHERE stream = 'codes'")).ShouldBe(1);

            // The ceiling lifted (a new worker, so a fresh read of the setting): the rest goes.
            await database.ExecuteAsync("DELETE FROM config.setting WHERE key LIKE 'Email:Ceiling%'");
            await using (var provider = WorkerServices(transport))
            {
                await DrainAsync(provider);
            }

            transport.Sent.Count(m => m.Stream == "notices").ShouldBe(4);
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' LIKE '%@streams.pavyzdys.test'")).ShouldBe(0);
        }
        finally
        {
            await ResetAsync();
            await UnparkAsync(parked);
        }
    }

    [Fact]
    public async Task A_ceiling_counts_only_the_last_twenty_four_hours()
    {
        await ResetAsync();
        var parked = await ParkOthersAsync();
        try
        {
            await SetCeilingAsync("Email:CeilingNoticesPerDay", 1);
            await database.ExecuteAsync(
                "INSERT INTO notify.stream_send (id, stream, sent_at) VALUES (gen_random_uuid(), 'notices', now() - interval '25 hours'), (gen_random_uuid(), 'notices', now() - interval '1 hour')");
            var transport = new RecordingTransport();
            await using var provider = WorkerServices(transport);
            await EnqueueAsync(provider, MessageKinds.FollowPlannedEmail, Planned("pranesimas@streams.pavyzdys.test"));
            await DrainAsync(provider);

            transport.Sent.ShouldBeEmpty("one went an hour ago, which is the ceiling; the one of 25 hours ago does not count");
            await database.ExecuteAsync("UPDATE notify.stream_send SET sent_at = now() - interval '26 hours'");
            await using var later = WorkerServices(transport);
            await DrainAsync(later);
            transport.Sent.ShouldHaveSingleItem().Stream.ShouldBe("notices");
        }
        finally
        {
            await ResetAsync();
            await UnparkAsync(parked);
        }
    }

    [Fact]
    public async Task A_quota_refusal_suppresses_nobody_pauses_that_stream_only_and_the_messages_go_later()
    {
        await ResetAsync();
        var parked = await ParkOthersAsync();
        try
        {
            var a = "ruta@streams.pavyzdys.test";
            var b = "jonas@streams.pavyzdys.test";
            var code = "kodas@streams.pavyzdys.test";
            var transport = new RecordingTransport
            {
                // The provider's quota, answered at RCPT TO: not about the recipient's mailbox.
                Refuse = m => m.Stream == "mp"
                    ? new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, (SmtpStatusCode)550, new MimeKit.MailboxAddress("", m.To), "550 5.4.5 Daily sending limit exceeded")
                    : null,
            };
            await using (var provider = WorkerServices(transport))
            {
                foreach (var to in new[] { a, b })
                {
                    await EnqueueAsync(provider, MpMailKinds.Notice, new MpMailNoticeEmail(to, MpMailNotices.NewLink, "Laima Uosytė", null, "/suvestines/nustatymai/x", null));
                }

                await DrainAsync(provider);
                (await database.ScalarAsync<long>("SELECT count(*) FROM notify.suppression WHERE id <> ALL(@known)", ("known", _knownSuppressions))).ShouldBe(0, "a sender's limit is not a bad recipient");
                (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' LIKE '%@streams.pavyzdys.test' AND attempts = 0 AND failed_at IS NULL")).ShouldBe(2);
                var tried = transport.Tried;
                tried.ShouldBe(1, "the stream stopped at the first refusal; the second message was not even tried");

                // The stream is paused: even once the quota is lifted this worker leaves it alone, and codes still go.
                transport.Refuse = null;
                await EnqueueAsync(provider, MessageKinds.SignInCodeEmail, new SignInCodeEmail(code, "246810", 10));
                await DrainAsync(provider);
                transport.Sent.Select(m => m.To).ShouldBe([code]);
            }

            // A worker that starts again (or an hour later) tries the stream again, and both go.
            await using (var provider = WorkerServices(transport))
            {
                await DrainAsync(provider);
            }

            transport.Sent.Where(m => m.Stream == "mp").Select(m => m.To).ShouldBe([a, b], ignoreOrder: true);
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.suppression WHERE id <> ALL(@known)", ("known", _knownSuppressions))).ShouldBe(0);
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' LIKE '%@streams.pavyzdys.test'")).ShouldBe(0);
        }
        finally
        {
            await ResetAsync();
            await UnparkAsync(parked);
        }
    }

    [Fact]
    public async Task A_refusal_of_the_mailbox_still_suppresses_an_mp_address()
    {
        await ResetAsync();
        var parked = await ParkOthersAsync();
        try
        {
            var to = "ruta@streams.pavyzdys.test";
            var transport = new RecordingTransport
            {
                Refuse = m => new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, (SmtpStatusCode)550, new MimeKit.MailboxAddress("", m.To), "550 5.1.1 User unknown"),
            };
            await using var provider = WorkerServices(transport);
            await EnqueueAsync(provider, MpMailKinds.Notice, new MpMailNoticeEmail(to, MpMailNotices.NewLink, "Laima Uosytė", null, "/suvestines/nustatymai/x", null));
            await DrainAsync(provider);

            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.suppression WHERE id <> ALL(@known)", ("known", _knownSuppressions))).ShouldBe(1);
            (await database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' LIKE '%@streams.pavyzdys.test'")).ShouldBe(0);
        }
        finally
        {
            await ResetAsync();
            await UnparkAsync(parked);
        }
    }

    private static FollowPlannedEmail Planned(string to) =>
        new(to, "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", null, new DateOnly(2026, 12, 1), "/seimas/XVP-0412", "/paskyra");

    private async Task SetCeilingAsync(string key, int value)
    {
        key.ShouldBeOneOf(Ceilings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(database.MigratorConnectionString));
        services.AddAuditModule();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISettingsAdministration>()
            .SetAsServerAsync(key, JsonSerializer.SerializeToElement(value), "migrator", CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
    }

    private static async Task DrainAsync(IServiceProvider provider)
    {
        var dispatcher = provider.GetRequiredService<INotificationsDispatcher>();
        while (await dispatcher.DispatchDueAsync(CancellationToken.None) > 0)
        {
        }
    }

    private ServiceProvider WorkerServices(RecordingTransport transport)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MailHashKey"] = "test-mail-hash-key" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(NpgsqlDataSource.Create(database.WorkerConnectionString));
        services.AddNotificationsModule(configuration);
        services.AddNotificationsDispatcher(configuration);
        services.AddAuditModule();
        services.AddSingleton<IEmailTransport>(transport);
        return services.BuildServiceProvider();
    }

    private static async Task EnqueueAsync<T>(IServiceProvider provider, string kind, T payload, DateTimeOffset? due = null)
    {
        await using var connection = await provider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await provider.GetRequiredService<IOutbox>().EnqueueAsync(OutboxMessage.Create(kind, payload, due), transaction);
        await transaction.CommitAsync();
    }

    /// <summary>A mail server that records what it took and can refuse what it is told to.</summary>
    private sealed class RecordingTransport : IEmailTransport
    {
        private readonly List<EmailMessage> _sent = [];
        private int _tried;

        public Func<EmailMessage, Exception?>? Refuse { get; set; }

        public int Tried => _tried;

        public List<EmailMessage> Sent
        {
            get
            {
                lock (_sent)
                {
                    return [.. _sent];
                }
            }
        }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (!message.To.EndsWith("@streams.pavyzdys.test", StringComparison.Ordinal))
            {
                return Task.CompletedTask; // another test's mail that came due meanwhile
            }

            Interlocked.Increment(ref _tried);
            if (Refuse?.Invoke(message) is { } error)
            {
                throw error;
            }

            lock (_sent)
            {
                _sent.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
