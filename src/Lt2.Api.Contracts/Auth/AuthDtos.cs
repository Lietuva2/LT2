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

    /// <summary>The solved proof of work (design 6.4), as the site's script made it; never in an address.</summary>
    public string? BotCheck { get; init; }
}

/// <summary>
/// A registration's answer (design 6.2). <see cref="SignedIn"/> only on a trial site without email
/// (<c>Registration:WithoutEmail</c>): a new address with a working invitation code is signed in at once, with no code to type, and
/// <see cref="AccountChanged"/> says whether the browser was signed in to another account, whose session ended. Everywhere else
/// both are false and a code was emailed.
/// </summary>
public sealed record RegisterResponse(bool SignedIn, bool AccountChanged);

public sealed record RequestCodeRequest
{
    public string Email { get; init; } = "";

    /// <summary>The solved proof of work (design 6.4); never in an address.</summary>
    public string? BotCheck { get; init; }
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

/// <summary>
/// A proof-of-work challenge in the ALTCHA format (design 6.4). The site's own script finds the number whose SHA-256 with
/// <see cref="Salt"/> is <see cref="Challenge"/> (0 to <see cref="MaxNumber"/>) and sends the five fields back with the number.
/// </summary>
public sealed record BotChallengeResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "SHA-256";

    [System.Text.Json.Serialization.JsonPropertyName("challenge")]
    public string Challenge { get; init; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("maxnumber")]
    public int MaxNumber { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("salt")]
    public string Salt { get; init; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("signature")]
    public string Signature { get; init; } = "";
}
