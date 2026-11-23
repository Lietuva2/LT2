namespace Lt2.Parliament.Import.Client;

/// <summary>
/// Configuration section <c>Seimas</c>. The numbers are operational defaults, not product rules: the design asks for
/// retries, time-outs and a circuit breaker (3, 7.2) and about one request a second during the backfill, without values.
/// </summary>
internal sealed class SeimasOptions
{
    public const string Section = "Seimas";

    /// <summary>Base address of the open-data service, ending in a slash. Production: <c>https://apps.lrs.lt/sip/</c>.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The term to import (<c>kadencijos_id</c>; 10 is 2024–2028).</summary>
    public int TermId { get; set; } = 10;

    /// <summary>Time-out of one attempt.</summary>
    public double AttemptTimeoutSeconds { get; set; } = 30;

    /// <summary>Time-out of one request with all its retries.</summary>
    public double TotalTimeoutSeconds { get; set; } = 180;

    /// <summary>Retries after a failed attempt (dropped connection, time-out, 5xx, 408, 429), with exponential back-off.</summary>
    public int RetryAttempts { get; set; } = 5;

    /// <summary>First back-off delay; doubles each retry, with jitter.</summary>
    public double RetryDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Further attempts when the service answers 200 with a body that is not well-formed XML. The 2026 prototype
    /// (<c>fetch_vote.py</c>) had to retry until the expected element appeared.
    /// </summary>
    public int MalformedRetries { get; set; } = 2;

    /// <summary>Smallest gap between two requests (design 7.2: about one per second).</summary>
    public double MinimumIntervalMilliseconds { get; set; } = 1000;

    /// <summary>Agenda, proceedings and vote results of sittings that started within this many days are fetched again
    /// on every run; older ones only until they were fetched once.</summary>
    public int RefetchDays { get; set; } = 14;
}
