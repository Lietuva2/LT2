using Lt2.SharedKernel;

namespace Lt2.Notifications.Outbox;

/// <summary>
/// A stream's daily ceiling (design 8.19.3): the sender-side limit that keeps a stream under its mailbox's cap at the provider.
/// A pure rule; the dispatcher counts the sends and leaves a stream's messages waiting when it has none left.
/// </summary>
internal static class StreamCeilings
{
    /// <summary>The window the ceiling counts over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>How many more emails the stream may send now: unlimited when the ceiling is 0 (or below), else what is left of it.</summary>
    public static int Remaining(int ceiling, int sentInWindow) =>
        ceiling <= 0 ? int.MaxValue : Math.Max(0, ceiling - Math.Max(0, sentInWindow));

    /// <summary>Whether the stream has sent its ceiling and must wait.</summary>
    public static bool Reached(int ceiling, int sentInWindow) => Remaining(ceiling, sentInWindow) == 0;

    /// <summary>The setting that holds a stream's ceiling.</summary>
    public static SettingKey<int> KeyOf(string stream) => stream switch
    {
        EmailStreams.Codes => SettingKeys.EmailCeilingCodesPerDay,
        EmailStreams.Notices => SettingKeys.EmailCeilingNoticesPerDay,
        EmailStreams.Mp => SettingKeys.EmailCeilingMpPerDay,
        _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, "Unknown email stream."),
    };
}

/// <summary>A row of <c>notify.stream_send</c>: one email sent on a stream, so the ceiling can count the last 24 hours. No address, no kind.</summary>
internal sealed class StreamSend
{
    public Guid Id { get; set; }

    public string Stream { get; set; } = "";

    public DateTimeOffset SentAt { get; set; }
}
