namespace Lt2.Identity.Domain;

/// <summary>
/// Known browsers (design 6.2, 8.20.2) as pure functions. A browser that has signed in to an account has its own allowance for that
/// account, the same two settings as the shared one (five an hour and ten a day by default) counted separately per known browser; a code it asked for is accepted only from it, so
/// requests and wrong guesses from elsewhere can neither use up nor cancel it. The cookie gives no access by itself: these
/// rules only choose which allowance counts a request and which codes a browser may try.
/// </summary>
internal static class KnownBrowserRules
{
    /// <summary>Known browsers kept per account; the one seen longest ago goes first (design 8.20.2).</summary>
    public const int MaxPerAccount = 20;

    /// <summary>How long the cookie lives, renewed on every sign-in, and how long an unseen row is kept.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    /// <summary>
    /// Whether a known browser may be sent another code: the same two limits as the shared allowance (<c>SignIn:CodesPerHour</c>,
    /// <c>SignIn:CodesPerDay</c>), counted over the codes this browser asked for only. The daily limit bounds guessing for whoever
    /// holds the cookie (a shared computer keeps it after signing out). Codes asked for elsewhere do not count, and its own do not
    /// count against the shared limits.
    /// </summary>
    public static bool MayIssue(int issuedToThisBrowserInLastHour, int issuedToThisBrowserInLastDay, CodeLimits limits) =>
        SignInCodeRules.MayIssue(issuedToThisBrowserInLastHour, issuedToThisBrowserInLastDay, limits);

    /// <summary>
    /// Whether a browser may use a code: a code any browser may use (asked for by a browser not known to the account) is
    /// accepted from every browser; a code a known browser asked for, only from that browser.
    /// </summary>
    /// <param name="codeBrowser">The known browser the code was issued to, or null.</param>
    /// <param name="browser">The known browser making the attempt (known to this account), or null for any other.</param>
    public static bool MayUse(Guid? codeBrowser, Guid? browser) => codeBrowser is null || codeBrowser == browser;

    /// <summary>
    /// The codes a browser tries a typed code against: the newest open code any browser may use and, for a known browser, the
    /// newest open code it asked for itself. Codes of other known browsers are never among them. Each allowance's newest code
    /// replaced its earlier ones, so at most two codes are tried.
    /// </summary>
    public static IReadOnlyList<SignInCode> CodesToTry(IEnumerable<SignInCode> openCodes, Guid? browser)
    {
        ArgumentNullException.ThrowIfNull(openCodes);
        return openCodes
            .Where(c => c.ConsumedAt is null && MayUse(c.KnownDeviceId, browser))
            .GroupBy(c => c.KnownDeviceId)
            .Select(g => g.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).First())
            .OrderBy(c => c.KnownDeviceId is null ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// The browsers to forget so an account keeps at most <see cref="MaxPerAccount"/>: those seen longest ago beyond the newest
    /// twenty.
    /// </summary>
    public static IReadOnlyList<Guid> BeyondCap(IEnumerable<(Guid Id, DateTimeOffset LastSeenAt)> browsers)
    {
        ArgumentNullException.ThrowIfNull(browsers);
        return browsers.OrderByDescending(b => b.LastSeenAt).ThenByDescending(b => b.Id).Skip(MaxPerAccount).Select(b => b.Id).ToList();
    }

    /// <summary>A browser not seen for a year is deleted by the daily clean-up.</summary>
    public static bool IsStale(DateTimeOffset lastSeenAt, DateTimeOffset now) => now - lastSeenAt > Lifetime;

    /// <summary>The cut-off of the daily clean-up: rows last seen before it go.</summary>
    public static DateTimeOffset StaleBefore(DateTimeOffset now) => now - Lifetime;
}
