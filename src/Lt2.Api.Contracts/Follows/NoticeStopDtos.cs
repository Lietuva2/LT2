namespace Lt2.Api.Contracts.Follows;

// Design 8.24.1 (the designer's proposal of 2026-11-17): every email of the member's rhythm carries the account's stop link. One
// press turns every email switch off; the follows stay. Whoever holds the link may use it, without an account or a session.

/// <summary>The stop link's route and page (the modules' <c>MemberMailLinks</c> make the same addresses, pinned by a unit test).</summary>
public static class NoticeStopRoutes
{
    /// <summary>
    /// <c>POST</c>, the one-click stop of RFC 8058 (<c>List-Unsubscribe-Post</c>) and the page's button alike: mapped outside the
    /// API group's same-origin and antiforgery filter, as an MP's one-click stop is (design 8.19.4), because a mail program has
    /// neither. It can only turn the token's account's email switches off, and it answers 204 whatever the token.
    /// </summary>
    public const string Stop = ApiRoutes.Prefix + "/notices/{token}/stop";

    /// <summary>The page "Nebegauti laiškų" a line in every such email opens: its GET changes nothing.</summary>
    public const string Page = "/laiskai/nebegauti";

    public static string StopOf(string token) => ApiRoutes.Prefix + "/notices/" + Uri.EscapeDataString(token) + "/stop";

    public static string PageOf(string token) => Page + "/" + Uri.EscapeDataString(token);
}

/// <summary>The member's stop link (design 8.24.1).</summary>
public interface INoticeStopApi
{
    /// <summary>Turns every email switch of the token's account off; succeeds the same for an unknown or malformed token.</summary>
    Task<ApiResult> StopAsync(string token, CancellationToken cancellationToken = default);
}
