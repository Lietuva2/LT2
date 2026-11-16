namespace Lt2.Web.Components.Pages;

/// <summary>
/// Resource marker for the terms of use and the privacy notice (design 8.22.2, 8.22.3): Lithuanian drafts for the lawyer and a
/// native reader. Values "[bus įrašyta]" are facts the owner has not given yet; their resource comments say what.
/// </summary>
public sealed class LegalText;

/// <summary>The versions of the two texts: dates, also the day each applies from.</summary>
public static class LegalVersions
{
    /// <summary>The terms of use: the version members accept (<see cref="Lt2.SharedKernel.TermsOfUse.Version"/>).</summary>
    public const string Terms = Lt2.SharedKernel.TermsOfUse.Version;

    /// <summary>The privacy notice: informs, nobody accepts it (design 8.22.2), so a new version asks nothing of anyone.</summary>
    public const string Privacy = "2026-11-17";
}