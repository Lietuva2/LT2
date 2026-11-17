namespace Lt2.Api.Contracts.Auth;

/// <summary>
/// Registration by email and invitation code (design 6.2). It creates a pending account only; the display name and the
/// age declaration are asked once the address is proven (<see cref="Me.CompleteRegistrationRequest"/>).
/// </summary>
public sealed record RegisterRequest
{
    public string Email { get; init; } = "";

    public string? InvitationCode { get; init; }

    /// <summary>
    /// An MP office member's invitation code in place of an invitation code (design 8.5.3, owner 2026-11-17): it lets the
    /// registration through the closed trial's gate while it is valid; it is not kept on the pending account.
    /// </summary>
    public string? OfficeInvitation { get; init; }
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

/// <summary>
/// A sign-in's answer (design 6.2, owner 2026-11-17): <see cref="AccountChanged"/> when the browser was signed in to another
/// account, whose session ended with this sign-in; the page then says so.
/// </summary>
public sealed record SignInResponse(bool AccountChanged);

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
