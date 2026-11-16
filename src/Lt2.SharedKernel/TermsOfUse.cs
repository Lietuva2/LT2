namespace Lt2.SharedKernel;

/// <summary>
/// "Naudojimo taisyklės" (design 8.22.2): the account rests on them. A member accepts the version in force at registration and
/// again whenever it changes; until then the account reads, exports and deletes, and its other writes are refused with
/// <see cref="RequiredCode"/>. The accepted version is a consent of kind <see cref="Kind"/> (design 5.2).
/// </summary>
public static class TermsOfUse
{
    public const string Kind = "terms";

    /// <summary>
    /// The version in force, a date (<c>yyyy-MM-dd</c>): also the day it applies from ("Galioja nuo …"). The text is in the web
    /// host's resources (<c>Components/Pages/LegalText.resx</c>); a changed text gets a new, later version, and every member is
    /// asked again. The browser sends it back when accepting (<c>Lt2.Api.Contracts.Me.TermsText.Version</c>, the same value).
    /// </summary>
    public const string Version = "2026-11-17";

    /// <summary>The error code of a write refused until the member accepts.</summary>
    public const string RequiredCode = "terms.required";

    /// <summary>
    /// Whether a member who accepted <paramref name="acceptedVersion"/> (null: never) must accept the version in force
    /// <paramref name="current"/>: when they never accepted or accepted an earlier one. Versions are dates, compared as text.
    /// </summary>
    public static bool NeedsAcceptance(string? acceptedVersion, string current = Version) =>
        string.IsNullOrEmpty(acceptedVersion) || string.CompareOrdinal(acceptedVersion, current) < 0;
}
