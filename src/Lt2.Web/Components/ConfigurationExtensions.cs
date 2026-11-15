namespace Lt2.Web.Components;

/// <summary>Configuration the pages read (deployment configuration, never a runtime setting).</summary>
internal static class ConfigurationExtensions
{
    /// <summary>
    /// The moderators' contact address (<c>Moderation:ContactAddress</c>), for visitors who want to report or cannot sign
    /// in; none when it is not set, and then nothing on the pages mentions it.
    /// </summary>
    public static string? ModeratorContact(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["Moderation:ContactAddress"] is { Length: > 0 } address ? address : null;
    }
}
