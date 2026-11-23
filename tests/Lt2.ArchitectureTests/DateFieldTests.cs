using System.Text.RegularExpressions;

namespace Lt2.ArchitectureTests;

/// <summary>
/// AGENTS.md "Wording": a date in a form is a text field in <c>yyyy-MM-dd</c> with a calendar button (<c>DatePick</c>) that
/// fills it from the browser's own picker; the field itself is never <c>type="date"</c>. No page or component writes a date input,
/// and every field that uses <c>Display.DatePattern</c> is followed directly by its button inside one <c>.date-row</c>.
/// </summary>
public sealed partial class DateFieldTests
{
    private static readonly DirectoryInfo Root = RepositoryRoot();

    [GeneratedRegex(@"type=""date""", RegexOptions.IgnoreCase)]
    private static partial Regex DateControl();

    [GeneratedRegex(@"<div class=""date-row"">\s*<input\b[^>]*pattern=""@Display\.DatePattern""[^>]*/>\s*<DatePick InputId=""[^""]+""[^>]*/>\s*</div>", RegexOptions.Singleline)]
    private static partial Regex DateRow();

    private static IEnumerable<FileInfo> Pages() =>
        new[] { Path.Combine(Root.FullName, "src", "Lt2.Web"), Path.Combine(Root.FullName, "src", "Lt2.Web.Client") }
            .SelectMany(dir => new DirectoryInfo(dir).EnumerateFiles("*.razor", SearchOption.AllDirectories))
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void No_page_or_component_renders_a_date_input()
    {
        Pages().Where(f => DateControl().IsMatch(File.ReadAllText(f.FullName))).Select(f => f.Name).ShouldBeEmpty();
    }

    [Fact]
    public void Every_typed_date_field_has_its_calendar_button_right_after_it_in_one_row()
    {
        var typed = Pages().Where(f => File.ReadAllText(f.FullName).Contains("pattern=\"@Display.DatePattern\"", StringComparison.Ordinal)).ToList();
        typed.ShouldNotBeEmpty();
        foreach (var file in typed)
        {
            var text = File.ReadAllText(file.FullName);
            DateRow().Count(text).ShouldBe(Regex.Count(text, @"pattern=""@Display\.DatePattern"""), file.Name);
        }
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
}
