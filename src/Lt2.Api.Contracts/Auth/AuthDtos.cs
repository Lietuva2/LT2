namespace Lt2.Api.Contracts.Auth;

/// <summary>Registration by email and invitation code, with the self-declared age confirmation.</summary>
public sealed record RegisterRequest
{
    public string Email { get; init; } = "";

    public string? InvitationCode { get; init; }

    /// <summary>Optional name others will see next to what the person writes. Never shown with a side.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The person confirms they are 18 or older.</summary>
    public bool ConfirmsAdult { get; init; }
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
