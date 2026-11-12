using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.ImportTests.StandIn;

namespace Lt2.Parliament.ImportTests.Register;

/// <summary>The register of assumptions lists exactly what the parsers rely on, each line seen (with a source) or assumed.</summary>
public sealed class RegisterTests(ITestOutputHelper output)
{
    [Fact]
    public void The_element_and_attribute_lines_of_each_service_are_exactly_the_names_the_parsers_read()
    {
        var register = AssumptionsRegister.Load();
        var files = Directory.EnumerateFiles(StandInFixture.SyntheticDirectory, "*.xml").GroupBy(AssumptionsRegister.ServiceOf);
        foreach (var service in files)
        {
            var read = new HashSet<XmlUse>();
            using (XmlRead.Record(read))
            {
                foreach (var file in service)
                {
                    AssumptionsRegister.ParserFor(service.Key)(File.ReadAllBytes(file));
                }
            }

            var listed = register
                .Where(l => l.Service == service.Key && l.Kind is "element" or "attribute")
                .Select(l => new XmlUse(l.Kind, l.Name, l.Within))
                .ToHashSet();
            read.Except(listed).ShouldBeEmpty($"{service.Key}: read by a parser but not in the register");
            listed.Except(read).ShouldBeEmpty($"{service.Key}: in the register but not read by any parser");
        }
    }

    [Fact]
    public void Every_line_is_seen_or_assumed_and_every_seen_line_names_its_source()
    {
        var register = AssumptionsRegister.Load();
        register.ShouldNotBeEmpty();
        register.Select(l => l.Id).ShouldBeUnique();
        register.ShouldAllBe(l => l.Status == "seen" || l.Status == "assumed");
        register.Where(l => l.IsSeen).ShouldAllBe(l => l.Evidence.Length > 0);
        register.ShouldAllBe(l => new[] { "element", "attribute", "value", "format", "parameter", "behaviour" }.Contains(l.Kind));
    }

    [Fact]
    public void The_register_counts_are_as_reported()
    {
        var register = AssumptionsRegister.Load();
        var seen = register.Count(l => l.IsSeen);
        var assumed = register.Count - seen;
        foreach (var kind in register.GroupBy(l => l.Kind))
        {
            output.WriteLine($"{kind.Key}: {kind.Count(l => l.IsSeen)} seen, {kind.Count(l => !l.IsSeen)} assumed");
        }

        output.WriteLine($"total: {register.Count} lines, {seen} seen, {assumed} assumed");
        (register.Count, seen, assumed).ShouldBe((Expected.Lines, Expected.Seen, Expected.Assumed));
    }

    /// <summary>The numbers the M1 report gives; change them together with the register.</summary>
    private static class Expected
    {
        public const int Lines = 90;
        public const int Seen = 31;
        public const int Assumed = 59;
    }
}
