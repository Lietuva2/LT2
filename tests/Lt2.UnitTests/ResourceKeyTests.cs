using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lt2.UnitTests;

/// <summary>
/// A key the resource file lacks shows on the page as the key itself, with no error anywhere (AGENTS.md: user-facing text
/// goes through <c>IStringLocalizer</c>). This reads the code instead of running it: every key written as a literal at a
/// localizer must be in the <c>.resx</c> of the marker class that localizer reads. A literal key is <c>T["Key"]</c>,
/// <c>T["Key", args]</c>, <c>T.Format("Key", …)</c>, <c>T.OrElse("Key", …)</c>, <c>T.GetString("Key")</c>, either key of
/// <c>T[cond ? "A" : "B"]</c>, and a file's one-line wrapper such as <c>Format("Key", …)</c> =&gt; <c>l.Format(key, …)</c>.
/// Keys built at runtime (concatenation, interpolation, a variable, a helper returning a key) are not checked here.
/// <para>
/// A localizer is found as <c>@inject IStringLocalizer&lt;M&gt; T</c>, an <c>IStringLocalizer&lt;M&gt;</c> parameter,
/// field or property, <c>var l = …GetRequiredService&lt;IStringLocalizer&lt;M&gt;&gt;()</c>, a member inherited from a
/// base class (<c>InteractiveBase&lt;M&gt;.L</c>; a key in the base itself must be in every M its subclasses give), a public
/// localizer property of an injected class (<c>Formats.T</c>), and a plain <c>IStringLocalizer</c> in a project with one
/// resource file. A key-like literal at anything else fails as unresolved rather than going unchecked.
/// </para>
/// </summary>
public sealed partial class ResourceKeyTests
{
    /// <summary>
    /// Keys the code asks for whose Lithuanian copy is not written yet (new copy needs a native speaker's read first), as
    /// "file key resource". Each entry fails once the key exists, so the list cannot outlive the gap.
    /// </summary>
    private static readonly HashSet<string> CopyMissing = [];

    /// <summary>
    /// Indexers and calls given a key-like literal that are not localizers, as "file receiver". Empty: put one here only
    /// with a comment saying what it is.
    /// </summary>
    private static readonly HashSet<string> NotLocalizers = [];

    private static readonly Lazy<Scan> Scanned = new(() => new Scan(RepositoryRoot()));

    [Fact]
    public void Every_key_the_code_asks_a_localizer_for_is_in_its_resource_file()
    {
        var scan = Scanned.Value;
        var missing = scan.Uses
            .Where(u => u.Resource is { } r && !scan.Resources[r].Keys.Contains(u.Key))
            .Where(u => !CopyMissing.Contains($"{u.File} {u.Key} {u.Resource}"))
            .Select(u => $"{u.File}:{u.Line} {u.Key} → {scan.Resources[u.Resource!].File}")
            .Distinct()
            .ToList();

        if (missing.Count > 0)
        {
            Assert.Fail($"{missing.Count} keys missing from the resource file their localizer reads:\n" + string.Join("\n", missing));
        }
    }

    [Fact]
    public void Every_localizer_given_a_literal_key_reads_a_known_resource_file()
    {
        var unresolved = Scanned.Value.Uses
            .Where(u => u.Resource is null && !NotLocalizers.Contains($"{u.File} {u.Receiver}"))
            .Select(u => $"{u.File}:{u.Line} {u.Receiver}[\"{u.Key}\"]: {u.Marker}")
            .Distinct()
            .ToList();

        if (unresolved.Count > 0)
        {
            Assert.Fail(
                $"{unresolved.Count} literal keys at a localizer whose resource file this test cannot tell (teach the test the "
                + "construct, or put a non-localizer on NotLocalizers):\n" + string.Join("\n", unresolved));
        }
    }

    [Fact]
    public void The_exceptions_hold_only_what_still_fails()
    {
        var scan = Scanned.Value;
        var stillMissing = scan.Uses
            .Where(u => u.Resource is { } r && !scan.Resources[r].Keys.Contains(u.Key))
            .Select(u => $"{u.File} {u.Key} {u.Resource}")
            .ToHashSet(StringComparer.Ordinal);
        var stillUnresolved = scan.Uses.Where(u => u.Resource is null).Select(u => $"{u.File} {u.Receiver}").ToHashSet(StringComparer.Ordinal);

        CopyMissing.Where(e => !stillMissing.Contains(e)).ShouldBeEmpty("CopyMissing entries that no longer fail; remove them");
        NotLocalizers.Where(e => !stillUnresolved.Contains(e)).ShouldBeEmpty("NotLocalizers entries that no longer occur; remove them");
    }

    /// <summary>The scan must find something in every file, or a localizer it no longer recognises would pass unseen.</summary>
    [Fact]
    public void Every_resource_file_is_read_through_some_literal_key()
    {
        var scan = Scanned.Value;
        var read = scan.Uses.Where(u => u.Resource is not null).Select(u => u.Resource!).ToHashSet(StringComparer.Ordinal);

        scan.Resources.Count.ShouldBeGreaterThan(0);
        scan.Resources.Keys.Where(r => !read.Contains(r)).ShouldBeEmpty("resource files no literal key was checked against");
    }

    /// <summary>
    /// How the test maps a marker class to its file is how the localizer does: with no <c>ResourcesPath</c>, the
    /// resource of <c>N.M</c> is the embedded <c>RootNamespace.Folder.M.resources</c>, so the marker must be declared next
    /// to its <c>.resx</c> in the namespace of that folder; and there is only the neutral (Lithuanian) file of each.
    /// </summary>
    [Fact]
    public void Every_resource_file_has_a_marker_class_beside_it_in_its_folders_namespace()
    {
        var scan = Scanned.Value;

        scan.MarkerProblems.ShouldBeEmpty(string.Join("\n", scan.MarkerProblems));
        scan.Sources.Where(s => s.Text.Contains("ResourcesPath", StringComparison.Ordinal)).Select(s => s.File)
            .ShouldBeEmpty("a ResourcesPath changes where the localizer looks for every resource file");
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LT2.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("LT2.sln not found above " + AppContext.BaseDirectory);
    }

    private sealed record Source(string Path, string File, string Project, bool IsRazor, string Text);

    private sealed record ResourceFile(string Name, string File, string Project, IReadOnlySet<string> Keys);

    /// <summary>A localizer a file can use by <see cref="Name"/>; <see cref="Resources"/> empty when it is not known.</summary>
    private sealed record Localizer(string Name, string Marker, IReadOnlyList<string> Resources, int Position);

    /// <summary>A literal key at a localizer; <see cref="Resource"/> null when the localizer's file is not known.</summary>
    private sealed record KeyUse(string File, int Line, string Receiver, string Key, string? Resource, string Marker);

    private sealed class TypeInfo
    {
        public string? TypeParameter { get; set; }

        public string? Base { get; set; }

        public string? BaseArgument { get; set; }

        /// <summary>Localizer members by name: the marker's short name, or the type parameter's name.</summary>
        public Dictionary<string, string> Members { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Public { get; } = new(StringComparer.Ordinal);
    }

    private sealed partial class Scan
    {
        /// <summary>A whole string literal (no escapes, no interpolation holes): a key as written.</summary>
        private const string Literal = "\"(?<key>[^\"\\\\{}\\r\\n]*)\"";

        /// <summary>A literal, or a condition choosing between two literals.</summary>
        private const string Argument = "(?:" + Literal + "|(?<cond>[^\\[\\]\";]*?)\\?\\s*" + Literal + "\\s*:\\s*" + Literal + ")";

        private static readonly HashSet<string> Keywords =
            new(["return", "in", "is", "case", "await", "yield", "new", "params", "else", "and", "or", "not", "when", "throw", "default"], StringComparer.Ordinal);

        private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);

        private readonly Dictionary<string, HashSet<string>> _genericArguments = new(StringComparer.Ordinal);

        public Scan(string root)
        {
            var src = Path.Combine(root, "src");
            var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(src, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj" or "Migrations"))
                .ToList();
            string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

            foreach (var path in files.Where(f => f.EndsWith(".resx", StringComparison.Ordinal)))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var keys = XDocument.Load(path).Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
                if (name.Contains('.', StringComparison.Ordinal))
                {
                    MarkerProblems.Add($"{Relative(path)}: a culture-specific resource file; this test reads only the neutral ones");
                    continue;
                }

                if (!Resources.TryAdd(name, new ResourceFile(name, Relative(path), ProjectOf(path), keys)))
                {
                    MarkerProblems.Add($"{Relative(path)}: a second resource file named {name}; the test maps markers by name");
                }
            }

            Sources = files.Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                .Select(f => new Source(f, Relative(f), ProjectOf(f), f.EndsWith(".razor", StringComparison.Ordinal), WithoutComments(File.ReadAllText(f))))
                .ToList();

            CheckMarkers();
            foreach (var source in Sources)
            {
                CollectTypes(source);
            }

            foreach (var info in _types.Values.Where(t => t.Base is not null && t.BaseArgument is not null && t.BaseArgument != t.TypeParameter))
            {
                if (_types.TryGetValue(Short(info.Base!), out var b) && b.TypeParameter is not null)
                {
                    GenericArgumentsOf(Short(info.Base!)).Add(Short(info.BaseArgument!));
                }
            }

            foreach (var source in Sources)
            {
                CollectUses(source);
            }
        }

        public Dictionary<string, ResourceFile> Resources { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<Source> Sources { get; }

        public List<KeyUse> Uses { get; } = [];

        public List<string> MarkerProblems { get; } = [];

        [GeneratedRegex(@"@\*.*?\*@", RegexOptions.Singleline)]
        private static partial Regex RazorComment();

        [GeneratedRegex(@"^[ \t]*//[^\n]*", RegexOptions.Multiline)]
        private static partial Regex LineComment();

        [GeneratedRegex(@"^namespace\s+(?<ns>[\w.]+)", RegexOptions.Multiline)]
        private static partial Regex Namespace();

        [GeneratedRegex(@"\b(?:class|record)\s+(?<name>\w+)(?:<(?<tp>\w+)>)?(?:\([^)]*\))?(?:\s*:\s*(?<base>[\w.]+)(?:<(?<arg>[\w.]+)>)?)?")]
        private static partial Regex TypeDeclaration();

        [GeneratedRegex(@"^@inherits\s+(?<base>[\w.]+)(?:<(?<arg>[\w.]+)>)?", RegexOptions.Multiline)]
        private static partial Regex Inherits();

        [GeneratedRegex(@"\bIStringLocalizer<\s*(?<marker>[\w.]+)\s*>\s+(?<name>\w+)")]
        private static partial Regex TypedLocalizer();

        [GeneratedRegex(@"\bvar\s+(?<name>\w+)\s*=[^;]*?\bIStringLocalizer<\s*(?<marker>[\w.]+)\s*>>")]
        private static partial Regex ServiceLocalizer();

        [GeneratedRegex(@"\bIStringLocalizer\s+(?<name>\w+)")]
        private static partial Regex UntypedLocalizer();

        [GeneratedRegex(@"\G\s*(?:\{|=>|=|;)")]
        private static partial Regex MemberTail();

        [GeneratedRegex(@"\b(?<method>\w+)\(\s*string\s+(?<param>\w+)\b[^)]*\)\s*=>\s*(?<recv>[A-Za-z_]\w*(?:\.\w+)*?)(?:\.(?:Format|OrElse|GetString)\(\s*|\[\s*)\k<param>\b")]
        private static partial Regex Wrapper();

        [GeneratedRegex(@"(?<![\w.])(?<recv>[A-Za-z_]\w*(?:\.\w+)*)[ \t]*\[\s*" + Argument + @"\s*[\],]")]
        private static partial Regex Indexer();

        [GeneratedRegex(@"(?<![\w.])(?<recv>[A-Za-z_]\w*(?:\.\w+)*)\.(?:Format|OrElse|GetString)\(\s*" + Argument + @"\s*[,)]")]
        private static partial Regex Call();

        [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9_-]+)+$")]
        private static partial Regex KeyLike();

        /// <summary>Comments name keys in passing; only code counts. Line numbers stay as they were.</summary>
        private static string WithoutComments(string text) =>
            LineComment().Replace(RazorComment().Replace(text, m => new string('\n', m.Value.Count(c => c == '\n'))), "");

        private static string Short(string name) => name[(name.LastIndexOf('.') + 1)..];

        private static string ProjectOf(string path)
        {
            for (var dir = Path.GetDirectoryName(path); dir is not null; dir = Path.GetDirectoryName(dir))
            {
                if (Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault() is { } project)
                {
                    return project;
                }
            }

            throw new InvalidOperationException("No project above " + path);
        }

        private static string RootNamespaceOf(string project) =>
            XDocument.Load(project).Descendants("RootNamespace").Select(e => e.Value.Trim()).FirstOrDefault()
            ?? Path.GetFileNameWithoutExtension(project);

        private HashSet<string> GenericArgumentsOf(string type)
        {
            if (!_genericArguments.TryGetValue(type, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _genericArguments[type] = set;
            }

            return set;
        }

        private void CheckMarkers()
        {
            foreach (var resource in Resources.Values)
            {
                var declaration = new Regex(@"\b(?:class|record)\s+" + Regex.Escape(resource.Name) + @"\b");
                var marker = Sources.FirstOrDefault(s => !s.IsRazor && FolderOf(s.File) == FolderOf(resource.File) && declaration.IsMatch(s.Text));
                if (marker is null)
                {
                    MarkerProblems.Add($"{resource.File}: no class {resource.Name} declared beside it");
                    continue;
                }

                var projectDir = Path.GetDirectoryName(resource.Project)!;
                var relativeFolder = Path.GetRelativePath(projectDir, Path.GetDirectoryName(marker.Path)!);
                var expected = RootNamespaceOf(resource.Project) + (relativeFolder == "." ? "" : "." + relativeFolder.Replace(Path.DirectorySeparatorChar, '.'));
                var actual = Namespace().Match(marker.Text) is { Success: true } ns ? ns.Groups["ns"].Value : "(none)";
                if (actual != expected)
                {
                    MarkerProblems.Add($"{resource.File}: {resource.Name} is in namespace {actual}, so the localizer looks for {actual}.{resource.Name}.resources, not {expected}.{resource.Name}.resources");
                }
            }
        }

        private static string FolderOf(string file) => file[..file.LastIndexOf('/')];

        private TypeInfo TypeNamed(string name)
        {
            if (!_types.TryGetValue(name, out var info))
            {
                info = new TypeInfo();
                _types[name] = info;
            }

            return info;
        }

        private void CollectTypes(Source source)
        {
            if (source.IsRazor)
            {
                var info = TypeNamed(Path.GetFileNameWithoutExtension(source.Path));
                if (Inherits().Match(source.Text) is { Success: true } inherits)
                {
                    info.Base = inherits.Groups["base"].Value;
                    info.BaseArgument = inherits.Groups["arg"].Success ? inherits.Groups["arg"].Value : null;
                }

                foreach (Match m in TypedLocalizer().Matches(source.Text))
                {
                    info.Members[m.Groups["name"].Value] = Short(m.Groups["marker"].Value);
                }

                return;
            }

            var declarations = TypeDeclaration().Matches(source.Text);
            foreach (Match d in declarations)
            {
                var info = TypeNamed(d.Groups["name"].Value);
                info.TypeParameter ??= d.Groups["tp"].Success ? d.Groups["tp"].Value : null;
                if (d.Groups["base"].Success && info.Base is null)
                {
                    info.Base = d.Groups["base"].Value;
                    info.BaseArgument = d.Groups["arg"].Success ? d.Groups["arg"].Value : null;
                }
            }

            foreach (Match m in TypedLocalizer().Matches(source.Text))
            {
                var owner = declarations.LastOrDefault(d => d.Index < m.Index);
                if (owner is null || !MemberTail().IsMatch(source.Text, m.Index + m.Length))
                {
                    continue;
                }

                var info = TypeNamed(owner.Groups["name"].Value);
                var name = m.Groups["name"].Value;
                info.Members[name] = Short(m.Groups["marker"].Value);
                var lineStart = source.Text.LastIndexOf('\n', m.Index) + 1;
                if (source.Text[lineStart..m.Index].TrimStart().StartsWith("public ", StringComparison.Ordinal))
                {
                    info.Public.Add(name);
                }
            }
        }

        /// <summary>The members a type has from itself and its bases, a base's type parameter replaced by the argument given.</summary>
        private Dictionary<string, string> MembersOf(string type, string? argument, int depth)
        {
            if (depth > 20 || !_types.TryGetValue(Short(type), out var info))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var baseArgument = info.BaseArgument is null ? null : info.BaseArgument == info.TypeParameter ? argument : Short(info.BaseArgument);
            var members = info.Base is null ? new Dictionary<string, string>(StringComparer.Ordinal) : MembersOf(info.Base, baseArgument, depth + 1);
            foreach (var (name, marker) in info.Members)
            {
                members[name] = marker == info.TypeParameter && argument is not null ? argument : marker;
            }

            return members;
        }

        /// <summary>The resource files a marker means in this file: its own, or for a type parameter every argument given to it.</summary>
        private IReadOnlyList<string> ResourcesOf(string marker, IEnumerable<string> ownTypes)
        {
            if (Resources.ContainsKey(marker))
            {
                return [marker];
            }

            foreach (var type in ownTypes)
            {
                if (_types.TryGetValue(type, out var info) && info.TypeParameter == marker)
                {
                    return [.. GenericArgumentsOf(type).Order(StringComparer.Ordinal)];
                }
            }

            return [];
        }

        private void CollectUses(Source source)
        {
            var text = source.Text;
            IReadOnlyList<string> ownTypes = source.IsRazor
                ? [Path.GetFileNameWithoutExtension(source.Path)]
                : TypeDeclaration().Matches(text).Select(d => d.Groups["name"].Value).ToList();
            var localizers = new List<Localizer>();

            foreach (Match m in TypedLocalizer().Matches(text))
            {
                var marker = Short(m.Groups["marker"].Value);
                localizers.Add(new Localizer(m.Groups["name"].Value, marker, ResourcesOf(marker, ownTypes), m.Index));
            }

            foreach (Match m in ServiceLocalizer().Matches(text))
            {
                var marker = Short(m.Groups["marker"].Value);
                localizers.Add(new Localizer(m.Groups["name"].Value, marker, ResourcesOf(marker, ownTypes), m.Index));
            }

            // A plain IStringLocalizer (a helper's parameter, an extension's this) can only read the one file of its project.
            var projectResources = Resources.Values.Where(r => r.Project == source.Project).Select(r => r.Name).ToList();
            foreach (Match m in UntypedLocalizer().Matches(text))
            {
                localizers.Add(new Localizer(m.Groups["name"].Value, "IStringLocalizer", projectResources.Count == 1 ? projectResources : [], m.Index));
            }

            foreach (var type in ownTypes)
            {
                foreach (var (name, marker) in MembersOf(type, null, 0))
                {
                    localizers.Add(new Localizer(name, marker, ResourcesOf(marker, ownTypes), -1));
                }
            }

            foreach (var (holder, info) in _types.Where(t => t.Value.Public.Count > 0))
            {
                foreach (Match m in Regex.Matches(text, @"\b" + Regex.Escape(holder) + @"\s+(?<name>\w+)\b(?!\s*\()"))
                {
                    foreach (var member in info.Public)
                    {
                        var marker = info.Members[member];
                        localizers.Add(new Localizer(m.Groups["name"].Value + "." + member, marker, ResourcesOf(marker, ownTypes), m.Index));
                    }
                }
            }

            var lineStarts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineStarts.Add(i + 1);
                }
            }

            int LineOf(int index)
            {
                var found = lineStarts.BinarySearch(index);
                return (found >= 0 ? found : ~found - 1) + 1;
            }

            void Add(Match m, string receiver)
            {
                if (m.Groups["cond"].Success && m.Groups["cond"].Value.Count(c => c == '(') != m.Groups["cond"].Value.Count(c => c == ')'))
                {
                    return; // (a ? "X." : "Y.") + z: the ternary is only a part of the key.
                }

                if (receiver.StartsWith("this.", StringComparison.Ordinal))
                {
                    receiver = receiver[5..];
                }

                if (Keywords.Contains(receiver))
                {
                    return;
                }

                var keys = m.Groups["key"].Captures.Select(c => c.Value).ToList();
                var candidates = localizers.Where(l => l.Name == receiver).ToList();
                if (candidates.Count == 0)
                {
                    if (keys.Any(k => KeyLike().IsMatch(k) || Resources.Values.Any(r => r.Keys.Contains(k))))
                    {
                        Uses.AddRange(keys.Select(k => new KeyUse(source.File, LineOf(m.Index), receiver, k, null, "not a localizer the test knows")));
                    }

                    return;
                }

                var localizer = candidates.LastOrDefault(l => l.Position <= m.Index) ?? candidates[0];
                foreach (var key in keys)
                {
                    if (localizer.Resources.Count == 0)
                    {
                        Uses.Add(new KeyUse(source.File, LineOf(m.Index), receiver, key, null, $"reads {localizer.Marker}, which is not a resource file's marker"));
                    }

                    foreach (var resource in localizer.Resources)
                    {
                        Uses.Add(new KeyUse(source.File, LineOf(m.Index), receiver, key, resource, localizer.Marker));
                    }
                }
            }

            foreach (Match m in Indexer().Matches(text))
            {
                Add(m, m.Groups["recv"].Value);
            }

            foreach (Match m in Call().Matches(text))
            {
                Add(m, m.Groups["recv"].Value);
            }

            foreach (Match wrapper in Wrapper().Matches(text))
            {
                var call = new Regex(@"(?<![\w.])" + Regex.Escape(wrapper.Groups["method"].Value) + @"\(\s*" + Argument + @"\s*[,)]");
                foreach (Match m in call.Matches(text))
                {
                    Add(m, wrapper.Groups["recv"].Value);
                }
            }
        }
    }
}
