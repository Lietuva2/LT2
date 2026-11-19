using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Lt2.Notifications;

/// <summary>
/// <c>--demo-emails</c> (local stack and trial only): one email of every outbox kind in <c>EmailStreams</c>, and of every variant a
/// kind has, from invented data, addressed to <c>NAME@pavyzdys.test</c>, so Mailpit lists them by name and the worker renders them
/// through the real renderers (what is queued is <c>DemoEmailSet</c>). Nothing is written but outbox rows: no account, no delivery
/// row. Refuses unless <c>Demo:Allowed</c> is true and <c>Email:Caught</c> is true (every email of the deployment lands in a
/// catcher), so it cannot run where mail goes out.
/// </summary>
public static class DemoEmails
{
    /// <summary>The domain every demonstration address is under (never a real one).</summary>
    public const string Domain = Outbox.DemoEmailSet.Domain;

    /// <summary>Refuses (throws) unless <c>Demo:Allowed</c> and <c>Email:Caught</c> are both true.</summary>
    public static void Require(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("Demo:Allowed"))
        {
            throw new InvalidOperationException("Refused: --demo-emails runs only where Demo:Allowed is true (the local stack and the trial).");
        }

        if (!configuration.GetValue<bool>("Email:Caught"))
        {
            throw new InvalidOperationException("Refused: --demo-emails runs only where Email:Caught is true (every email of the deployment lands in Mailpit).");
        }
    }

    /// <summary>Writes the demonstration emails to the outbox in one transaction; returns how many.</summary>
    public static async Task<int> EnqueueAsync(IConfiguration configuration, string connectionString, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Require(configuration);
        var emails = DemoEmailSet.Build(clock.GetUtcNow());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var outbox = new OutboxWriter(clock);
        foreach (var email in emails)
        {
            await outbox.EnqueueAsync(email.Message, transaction, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return emails.Count;
    }
}
