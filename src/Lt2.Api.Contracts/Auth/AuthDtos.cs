namespace Lt2.Api.Contracts.Auth;

/// <summary>
/// Registration by email and invitation code (design 6.2). It creates a pending account only; the display name and the
/// age declaration are asked once the address is proven (<see cref="Me.CompleteRegistrationRequest"/>).
/// </summary>
public sealed record RegisterRequest
{
    public string Email { get; init; } = "";

    public string? InvitationCode { get; init; }
}

public sealed record RequestCodeRequest
{
    public string Email { get; init; } = "";
}

public sealed record SignInRequest
{
    public string Email { get; init; } = "";

    public string Code { get; init; } = "";
}

/// <summary>The output of the browser's <c>PublicKeyCredential.toJSON()</c>, passed through as a string.</summary>
public sealed record PasskeyCredentialRequest
{
    public string Credential { get; init; } = "";

    /// <summary>Optional name the person gives the passkey.</summary>
    public string? Name { get; init; }
}

public sealed record AntiforgeryTokenResponse
{
    public string Token { get; init; } = "";

    public string HeaderName { get; init; } = ApiHeaders.Antiforgery;
}
