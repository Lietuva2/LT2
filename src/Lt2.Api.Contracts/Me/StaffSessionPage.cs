namespace Lt2.Api.Contracts.Me;

/// <summary>
/// Design 6.2, 8.21 (owner, 2026-11-16): the page where a code session on a staff account becomes a staff session with the
/// staff passkey, and the page to go back to afterwards, carried after the <c>#</c> as <c>grizti</c> (only the browser reads
/// it). The path to go back to is checked as the terms' acceptance screen checks its own (<see cref="TermsText.Back"/>: a
/// plain path on this site), and never this page itself; anything else goes to the account page.
/// </summary>
public static class StaffSessionPage
{
    public const string Path = "/prisijungti/raktas";

    /// <summary>The parameter after the <c>#</c>, the same name as the acceptance screen's.</summary>
    public const string BackParameter = TermsText.BackParameter;

    /// <summary>The page with <paramref name="back"/> to return to, or the page alone when it is not acceptable.</summary>
    public static string Link(string? back) =>
        Back(back) is { } path ? $"{Path}#{BackParameter}={Uri.EscapeDataString(path)}" : Path;

    /// <summary>The path to go back to, or null when <paramref name="back"/> is not a plain path on this site or is this page.</summary>
    public static string? Back(string? back) =>
        TermsText.Back(back) is { } path && !path.StartsWith(Path, StringComparison.Ordinal) ? path : null;
}
