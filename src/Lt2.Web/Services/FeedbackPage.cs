namespace Lt2.Web.Services;

/// <summary>What the feedback form offers to send along (design 8.10): the page's path only, and the browser's name and version.</summary>
internal static class FeedbackPage
{
    public static string? PathOf(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? Lt2.Identity.FeedbackSuggestions.PagePath(parsed.AbsolutePath) : null;

    public static string? BrowserOf(string? userAgent) => Lt2.Identity.FeedbackSuggestions.Browser(userAgent);
}
