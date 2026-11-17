namespace Lt2.SharedKernel;

/// <summary>
/// The member's stop link (design 8.24.1, the designer's proposal of 2026-11-17): every email that goes in the member's rhythm
/// carries its account's token in two addresses, the page "Nebegauti laiškų" (a line in the email) and the one-click stop of
/// RFC 8058 (<c>List-Unsubscribe</c>). Like an MP's link (design 8.19.4) the token is in the path, because the one-click form a
/// mail program sends has nowhere else to put it; logs keep these paths with the token replaced (<c>Lt2.Hosting.SecretPaths</c>).
/// The contracts' <c>NoticeStopRoutes</c> name the same addresses (pinned by a unit test).
/// </summary>
public static class MemberMailLinks
{
    /// <summary>The page, opened from the email's line: its GET changes nothing.</summary>
    public const string PagePrefix = "/laiskai/nebegauti";

    /// <summary>The API's part before the token.</summary>
    public const string OneClickPrefix = "/api/v1/notices";

    public static string Page(string token) => PagePrefix + "/" + token;

    public static string OneClick(string token) => OneClickPrefix + "/" + token + "/stop";
}
