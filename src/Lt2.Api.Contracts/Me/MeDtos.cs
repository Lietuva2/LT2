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

    /// <summary>
    /// <c>false</c> while the account is pending (design 6.2): the address is proven but the display name and the age
    /// declaration have not been given yet. A pending account can only complete the registration, see and end its
    /// sessions, and sign out.
    /// </summary>
    public bool RegistrationComplete { get; init; }

    /// <summary>
    /// The staff roles that work in this session: only when it was signed in with the account's staff passkey, after
    /// the role was activated (design 6.2). <see cref="Roles"/> lists every role granted.
    /// </summary>
    public IReadOnlyList<string> ActiveStaffRoles { get; init; } = [];

    /// <summary>A staff role was granted and its activation code has not been used yet.</summary>
    public bool AwaitingActivation { get; init; }
}

/// <summary>The one-time code an administrator passed on, used in a passkey session (design 6.2).</summary>
public sealed record RoleActivationRequest
{
    public string Code { get; init; } = "";
}

/// <summary>
/// Completes a pending registration once the address is proven (design 6.2): the display name (optional) and the
/// declaration of being 18 or older. The invitation use is counted now; if the code held has run out, another may be
/// given here.
/// </summary>
public sealed record CompleteRegistrationRequest
{
    /// <summary>Optional name others will see next to what the person writes. Never shown with a side.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The person declares they are 18 or older.</summary>
    public bool ConfirmsAdult { get; init; }

    /// <summary>Only when the code given at registration has run out or is no longer valid.</summary>
    public string? InvitationCode { get; init; }
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
