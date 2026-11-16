using Lt2.Identity;
using Lt2.Web.Client;

namespace Lt2.Web.Security;

/// <summary>
/// Design 8.22.1: everything the site stores in a browser, the one list. The privacy notice's part "Slapukai ir naršyklės
/// atmintis" is built from it (each row carries its name in <c>data-storage</c>), and the end-to-end storage test reads that
/// part and fails when a cookie or a storage key outside it appears; <c>BrowserStorageTests</c> checks the names against the
/// code that sets them. Nothing here is needed for anything but what the person asked for, so there is no cookie banner.
/// Adding a row is a design change, not a build detail.
/// </summary>
internal static class BrowserStorage
{
    public const string Cookie = "cookie";
    public const string Local = "local";
    public const string Session = "session";

    /// <summary>The request-forgery cookie's name (design 6.4), set by <see cref="SecurityServices"/>.</summary>
    public const string AntiforgeryCookieName = "__Host-lt2.af";

    /// <summary>
    /// Name, where it is kept, and the resource keys of its purpose and lifetime in <c>LegalText.resx</c>
    /// (<c>Storage.{Text}.Purpose</c>, <c>Storage.{Text}.Lifetime</c>).
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Kind, string Text)> Inventory =
    [
        (IdentityModule.SessionCookieName, Cookie, "Session"),
        (IdentityModule.PasskeyCookieName, Cookie, "Passkey"),
        (AntiforgeryCookieName, Cookie, "Antiforgery"),
        (IdentityModule.DeviceCookieName, Cookie, "Device"),
        (BrowserKeys.Theme, Local, "Theme"),
        (BrowserKeys.FrameworkResourceHash, Local, "Framework"),
        (BrowserKeys.Landing, Session, "Landing"),
        (BrowserKeys.InvitationCode, Session, "InvitationCode"),
    ];
}
