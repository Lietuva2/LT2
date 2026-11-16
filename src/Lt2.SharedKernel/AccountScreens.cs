// One source file, compiled twice, as TextExcerpt.cs: into Lt2.SharedKernel for the modules (the links in emails) and, linked
// by Lt2.Web.Client.csproj with LT2_CLIENT defined, into the browser's assembly for the pages.
#if LT2_CLIENT
namespace Lt2.Web.Client;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>
/// The account screens (design 8.21; the owner, 2026-11-16: "split into several forms"): four pages joined by a row of plain
/// links, and the anchors emails, legal texts and hints link to. Every link to an account screen is built from here.
/// </summary>
public static class AccountScreens
{
    /// <summary>"Profilis": the display name, "Apie mane", "Viešas profilis", party support.</summary>
    public const string Profile = "/sveiki";

    /// <summary>"Sekimas ir laiškai": what the person follows and the email switches.</summary>
    public const string Follows = "/sveiki/sekimas";

    /// <summary>"Dalyvavimas": the MP part, organisations, the invitation code, the staff part.</summary>
    public const string Taking = "/sveiki/dalyvavimas";

    /// <summary>"Saugumas ir duomenys": passkeys, sessions, known browsers, the positions consent, export and deletion.</summary>
    public const string Security = "/sveiki/saugumas";

    /// <summary>The follows and their switches; every follow email links here (design 8.11.2).</summary>
    public const string FollowsList = Follows + "#sekami";

    /// <summary>The known browsers (design 8.20.2).</summary>
    public const string KnownBrowsers = Security + "#zinomos-narsykles";

    /// <summary>The export and deleting the account (design 6.5); the legal texts link here.</summary>
    public const string MyData = Security + "#my-data";

    /// <summary>The personal invitation code (design 6.2).</summary>
    public const string MyInvitation = Taking + "#my-invitation";

    /// <summary>The four screens in their order.</summary>
    public static readonly IReadOnlyList<string> All = [Profile, Follows, Taking, Security];
}
