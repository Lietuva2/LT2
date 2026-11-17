namespace Lt2.Web.Client;

/// <summary>
/// What the site keeps in the browser's own storage (design 8.22.1): only these keys, each strictly needed for what the person
/// is doing. They are part of the inventory the privacy notice lists (<c>Lt2.Web.Security.BrowserStorage</c>) and the
/// end-to-end storage test checks; a new key is a design change.
/// </summary>
public static class BrowserKeys
{
    /// <summary>
    /// Local storage, written by the framework itself (blazor.web.js) whenever a WebAssembly island starts, for visitors too: a
    /// hash of the application's files, so the next page knows whether they are already loaded. Nothing about the person; it is
    /// replaced when the application changes. The key ends with the client assembly's name.
    /// </summary>
    public const string FrameworkResourceHash = "blazor-resource-hash:Lt2.Web.Client";

    /// <summary>Local storage: the theme the person chose (<c>light</c> or <c>dark</c>), only once they choose one (js/lt2-theme.js).</summary>
    public const string Theme = "lt2-theme";

    /// <summary>
    /// Session storage: the page a sign-in link comes back to (design 6.9, 8.11.5, <c>tikslas</c>), kept through the sign-in
    /// steps, which reload the page; removed when used.
    /// </summary>
    public const string Landing = "lt2.tikslas";

    /// <summary>
    /// Session storage: the invitation code of the link the person came by (an invitation link or a share line's link, after
    /// its <c>#</c>, taken by js/lt2-invitation.js on any page, design 8.11.5) or typed at registration, kept in this tab until
    /// the registration is completed and sent again then (design 8.14.14, stage B); removed when used or once the person is
    /// signed in to a finished account.
    /// </summary>
    public const string InvitationCode = "lt2.kodas";
}