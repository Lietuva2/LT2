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
    /// sessions, export or delete what it holds, and sign out.
    /// </summary>
    public bool RegistrationComplete { get; init; }

    /// <summary>
    /// The staff roles that work in this session: only when it was signed in with the account's staff passkey, after
    /// the role was activated (design 6.2). <see cref="Roles"/> lists every role granted.
    /// </summary>
    public IReadOnlyList<string> ActiveStaffRoles { get; init; } = [];

    /// <summary>A staff role was granted and its activation code has not been used yet.</summary>
    public bool AwaitingActivation { get; init; }

    /// <summary>The explicit consent to store positions (design 6.5) is given to the text version in force.</summary>
    public bool HasPositionConsent { get; init; }

    /// <summary>
    /// The account holds an activated staff role, in any session. Deleting it needs a session signed in with its staff
    /// passkey, which is when <see cref="ActiveStaffRoles"/> is not empty (design 6.9).
    /// </summary>
    public bool StaffAccount { get; init; }

    /// <summary>
    /// Design 8.5.1: the proven address is this MP's official address and the account is not linked yet ("Perimti
    /// profilį"). Only in the answer to the browser: the page the server renders holds nothing about it (design 8.5.4).
    /// </summary>
    public Mps.MpOfferResponse? MpOffer { get; init; }

    /// <summary>Design 8.5.4: the MP the account speaks for, in which part, and whether this session may write; as <see cref="MpOffer"/>.</summary>
    public Mps.MpLinkResponse? Mp { get; init; }

    /// <summary>
    /// Design 8.22.2: a member who has not accepted the terms of use in force (an account older than them, or the terms changed).
    /// Until they accept, reading, the export, deleting the account and signing out work and other writes are refused with
    /// <c>terms.required</c>, so a page shows the acceptance screen instead. Always false for a pending account, which accepts
    /// them when completing its registration.
    /// </summary>
    public bool TermsRequired { get; init; }
}

/// <summary>The version of the terms of use the client shows and accepts (design 8.22.2); the server accepts this one only.</summary>
public static class TermsText
{
    public const string Version = "2026-11-17";

    /// <summary>The acceptance screen (design 8.22.2) and its parameter: the page to go back to afterwards.</summary>
    public const string AcceptancePage = "/taisykles/sutikimas";

    public const string BackParameter = "grizti";

    /// <summary>
    /// The acceptance screen with <paramref name="back"/> to return to: a path on this site only (one leading <c>/</c>, no
    /// <c>//</c>, no backslash); anything else is dropped and the screen goes to the account page.
    /// </summary>
    public static string AcceptanceLink(string? back) =>
        Back(back) is { } path ? $"{AcceptancePage}?{BackParameter}={Uri.EscapeDataString(path)}" : AcceptancePage;

    /// <summary>The path to go back to, or null when <paramref name="back"/> is not a plain path on this site.</summary>
    public static string? Back(string? back) =>
        string.IsNullOrEmpty(back) || back.Length > 500 || !back.StartsWith('/') || back.StartsWith("//", StringComparison.Ordinal)
            || back.Contains('\\', StringComparison.Ordinal) || back.StartsWith(AcceptancePage, StringComparison.Ordinal)
            ? null
            : back;
}

/// <summary>Accepting the terms of use (design 8.22.2), given to the version the page showed; it must be the one in force.</summary>
public sealed record TermsAcceptanceRequest
{
    public string TextVersion { get; init; } = "";
}

/// <summary>The version of the consent text the client shows (design 6.5); the server accepts consent to this one only.</summary>
public static class PositionConsentText
{
    public const string Version = "2026-11-13";
}

/// <summary>
/// The explicit consent before a first position (design 6.5), given to the text the page showed. The text version must
/// be the one in force.
/// </summary>
public sealed record PositionConsentRequest
{
    public string TextVersion { get; init; } = "";
}

/// <summary>
/// A new display name (design 6.2): shown beside what the person writes in public; at most 60 characters; empty removes it.
/// </summary>
public sealed record DisplayNameRequest
{
    public string? DisplayName { get; init; }
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

    /// <summary>
    /// "Sutinku su naudojimo taisyklėmis" (design 8.22.2): required; the version in force (<see cref="TermsText.Version"/>) is
    /// stored as the account's consent of kind <c>terms</c>.
    /// </summary>
    public bool AcceptsTerms { get; init; }

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

/// <summary>
/// A browser known to the account (design 8.20.2): it has its own sign-in codes. Never the cookie or anything that
/// identifies it; <see cref="IsCurrent"/> marks the browser asking.
/// </summary>
/// <param name="Device">A short, coarse description of the browser when it last signed in, as for sessions.</param>
public sealed record KnownBrowserResponse(Guid Id, string? Device, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, bool IsCurrent);

/// <summary>The party the person said they support (private, optional), and the parties to choose from.</summary>
public sealed record PartySupportResponse(string? Party, IReadOnlyList<string> Choices);

/// <summary>
/// Sets the party, or with <c>null</c> removes it. Giving one needs <see cref="Consent"/>: a party preference is a
/// political opinion; it is used only for group numbers of at least 20 and never shown.
/// </summary>
public sealed record PartySupportRequest
{
    public string? Party { get; init; }

    public bool Consent { get; init; }
}
