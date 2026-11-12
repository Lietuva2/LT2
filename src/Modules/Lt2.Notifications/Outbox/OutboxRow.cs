namespace Lt2.Notifications.Outbox;

/// <summary>A row of <c>notify.outbox</c>. Sent rows are deleted, so the table holds only pending and failed messages.</summary>
internal sealed class OutboxRow
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = "";

    /// <summary>JSON payload (<c>jsonb</c>).</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Set when the message gave up after the last retry; it then waits for a person.</summary>
    public DateTimeOffset? FailedAt { get; set; }
}

internal sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 8;
}
