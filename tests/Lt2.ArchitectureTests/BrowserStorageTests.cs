using System.Text.RegularExpressions;
using Lt2.Web.Client;

namespace Lt2.ArchitectureTests;

/// <summary>
/// Design 8.22.1: nothing that is not strictly necessary is kept in the browser, and the privacy notice lists it all. The
/// end-to-end storage test reads the browser; this one reads the code, so a script that starts storing something fails before
/// any browser runs: only the theme's two scripts touch local storage (with <see cref="BrowserKeys.Theme"/>), only the sign-in
/// form (with <see cref="BrowserKeys.Landing"/> and <see cref="BrowserKeys.InvitationCode"/>) and the script that takes a link's
/// invitation code (with <see cref="BrowserKeys.InvitationCode"/> alone) touch session storage, and no script writes a cookie.
/// </summary>
public sealed partial class BrowserStorageTests
{
    private static readonly DirectoryInfo Root = RepositoryRoot();

    [GeneratedRegex(@"\b(localStorage|sessionStorage|document\.cookie|indexedDB|caches\.open)\b")]
    private static partial Regex Storage();

    private static IEnumerable<FileInfo> Sources() =>
        new[] { Path.Combine(Root.FullName, "src", "Lt2.Web"), Path.Combine(Root.FullName, "src", "Lt2.Web.Client") }
            .SelectMany(dir => new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
            .Where(f => f.Extension is ".js" or ".razor" or ".cs")
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void Only_the_theme_scripts_the_invitation_script_and_the_sign_in_form_use_the_browsers_storage()
    {
        var users = Sources().Where(f => Storage().IsMatch(StripComments(File.ReadAllText(f.FullName)))).Select(f => f.Name).Order(StringComparer.Ordinal).ToList();
        users.ShouldBe(["SignInFlow.razor", "lt2-invitation.js", "lt2-theme-blazor.js", "lt2-theme.js"]);
    }

    /// <summary>
    /// Design 8.11.5: the script that takes a link's invitation code from after the <c>#</c> on any page keeps it under the key the
    /// inventory lists for the invitation code, in session storage only, and nothing else.
    /// </summary>
    [Fact]
    public void The_invitation_script_keeps_only_the_invitation_code_in_session_storage()
    {
        var script = StripComments(File.ReadAllText(Path.Combine(Root.FullName, "src", "Lt2.Web", "wwwroot", "js", "lt2-invitation.js")));
        Regex.Matches(script, @"'(lt2[^']*)'").Select(m => m.Groups[1].Value).Distinct().ShouldBe([BrowserKeys.InvitationCode]);
        Regex.Matches(script, @"\b(localStorage|sessionStorage|document\.cookie|indexedDB|caches\.open)\b").Select(m => m.Value).Distinct().ShouldBe(["sessionStorage"]);
        Regex.Matches(script, @"sessionStorage\.(\w+)").Select(m => m.Groups[1].Value).Distinct().ShouldBe(["setItem"]);
    }

    [Fact]
    public void The_theme_scripts_keep_only_the_theme_key()
    {
        foreach (var name in new[] { "lt2-theme.js", "lt2-theme-blazor.js" })
        {
            var script = File.ReadAllText(Path.Combine(Root.FullName, "src", "Lt2.Web", "wwwroot", "js", name));
            Regex.Matches(script, @"'(lt2[^']*)'").Select(m => m.Groups[1].Value).Distinct().ShouldBe([BrowserKeys.Theme], name);
            script.ShouldNotContain("sessionStorage");
        }
    }

    [Fact]
    public void The_sign_in_form_keeps_only_the_landing_page_and_the_invitation_code_in_session_storage()
    {
        var form = File.ReadAllText(Path.Combine(Root.FullName, "src", "Lt2.Web.Client", "Components", "SignInFlow.razor"));
        form.ShouldContain("LandingKey = BrowserKeys.Landing");
        form.ShouldContain("CodeKey = BrowserKeys.InvitationCode");
        form.ShouldNotContain("localStorage");
        Regex.Matches(form, @"sessionStorage\.setItem"", (\w+)").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(["CodeKey", "LandingKey"]);
    }

    [Fact]
    public void The_frameworks_key_names_the_client_assembly() =>
        BrowserKeys.FrameworkResourceHash.ShouldBe("blazor-resource-hash:" + typeof(BrowserKeys).Assembly.GetName().Name);

    /// <summary>Comments explain storage; only code counts.</summary>
    private static string StripComments(string text) =>
        Regex.Replace(Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline), @"@\*.*?\*@", "", RegexOptions.Singleline), @"//[^\n]*", "");

    private static DirectoryInfo RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LT2.sln")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("LT2.sln not found above " + AppContext.BaseDirectory);
    }
}
