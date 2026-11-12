namespace Lt2.Api.Contracts.Me;

public sealed record MeResponse
{
    public Guid Id { get; init; }

    public string Email { get; init; } = "";

    public string? DisplayName { get; init; }

    /// <summary><c>none</c> until the first code is confirmed, then <c>email</c>.</summary>
    public string VerificationLevel { get; init; } = "";

    public IReadOnlyList<string> Roles { get; init; } = [];

    public int PasskeyCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record SessionResponse
{
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>A short, coarse description of the browser, for recognising the session.</summary>
    public string? Device { get; init; }

    /// <summary>How the session was signed in: <c>otp</c> (email code) or <c>pop</c> (passkey).</summary>
    public string Method { get; init; } = "";

    public bool IsCurrent { get; init; }
}
