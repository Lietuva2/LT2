using System.Text.RegularExpressions;

namespace Lt2.ArchitectureTests;

/// <summary>
/// The web host serves its own fonts (design/ui/README.md): every font comes from our origin, the licence texts travel
/// with the font files, and no stylesheet points at another host.
/// </summary>
public sealed partial class StaticAssetTests
{
    private static readonly DirectoryInfo WebRoot = new(Path.Combine(RepositoryRoot().FullName, "src", "Lt2.Web", "wwwroot"));

    [Fact]
    public void Every_font_face_points_at_a_font_file_that_is_shipped()
    {
        var fonts = new DirectoryInfo(Path.Combine(WebRoot.FullName, "fonts"));
        var css = File.ReadAllText(Path.Combine(fonts.FullName, "fonts.css"));

        var urls = UrlPattern().Matches(css).Select(m => m.Groups["url"].Value).ToArray();
        urls.ShouldNotBeEmpty();
        foreach (var url in urls)
        {
            url.ShouldEndWith(".woff2");
            url.ShouldNotContain("/");
            File.Exists(Path.Combine(fonts.FullName, url)).ShouldBeTrue(url);
        }

        foreach (var family in new[] { "Sofia Sans", "Sofia Sans Extra Condensed", "Brygada 1918" })
        {
            css.ShouldContain($"font-family: \"{family}\";");
        }
    }

    [Theory]
    [InlineData("OFL-sofia-sans.txt")]
    [InlineData("OFL-sofia-sans-extra-condensed.txt")]
    [InlineData("OFL-brygada-1918.txt")]
    public void The_font_licences_sit_next_to_the_font_files(string licence)
    {
        var text = File.ReadAllText(Path.Combine(WebRoot.FullName, "fonts", licence));
        text.ShouldContain("SIL OPEN FONT LICENSE", Case.Insensitive);
    }

    [Fact]
    public void No_stylesheet_loads_anything_from_another_host()
    {
        foreach (var file in WebRoot.EnumerateFiles("*.css", SearchOption.AllDirectories))
        {
            var css = File.ReadAllText(file.FullName);
            css.ShouldNotContain("@import", customMessage: file.Name);
            foreach (Match match in UrlPattern().Matches(css))
            {
                var url = match.Groups["url"].Value;
                url.ShouldNotContain("//", customMessage: $"{file.Name}: {url}");
            }
        }
    }

    [Fact]
    public void The_root_layout_links_the_fonts_before_the_main_stylesheet()
    {
        var app = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "src", "Lt2.Web", "Components", "App.razor"));
        var fonts = app.IndexOf("fonts/fonts.css", StringComparison.Ordinal);
        var main = app.IndexOf("css/lt2.css", StringComparison.Ordinal);
        fonts.ShouldBeGreaterThan(-1);
        fonts.ShouldBeLessThan(main);
        app.ShouldNotContain("googleapis", Case.Insensitive);
        app.ShouldNotContain("gstatic", Case.Insensitive);
    }

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

    [GeneratedRegex("""url\(\s*["']?(?<url>[^"')]+)["']?\s*\)""")]
    private static partial Regex UrlPattern();
}
