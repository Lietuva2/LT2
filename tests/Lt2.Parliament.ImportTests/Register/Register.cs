using Lt2.Parliament.Import.Client;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.ImportTests.StandIn;

namespace Lt2.Parliament.ImportTests.Register;

/// <summary>One line of Seimas/ASSUMPTIONS.md.</summary>
internal sealed record RegisterLine(string Id, string Service, string Kind, string Name, string Within, string Status, string Evidence, string ReliedOn)
{
    public bool IsSeen => Status == "seen";

    public override string ToString() => $"{Id} {Kind} {Name} in {Within} ({Service})";
}

/// <summary>Reads the register; the table format is part of its contract with these tests.</summary>
internal static class AssumptionsRegister
{
    public static string Path { get; } = System.IO.Path.Combine(StandInFixture.SeimasDirectory, "ASSUMPTIONS.md");

    public static IReadOnlyList<RegisterLine> Load() =>
        File.ReadLines(Path)
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Id ", StringComparison.Ordinal))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .Select(c => new RegisterLine(c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7]))
            .ToList();

    /// <summary>The parser for a service, as the import uses it.</summary>
    public static Action<byte[]> ParserFor(string service) => service switch
    {
        SeimasServices.Terms => body => SeimasParsers.Terms(body),
        SeimasServices.Members => body => SeimasParsers.Members(body),
        SeimasServices.Sessions => body => SeimasParsers.Sessions(body),
        SeimasServices.Sittings => body => SeimasParsers.Sittings(body),
        SeimasServices.Agenda => body => SeimasParsers.Agenda(body),
        SeimasServices.Proceedings => body => SeimasParsers.Proceedings(body),
        SeimasServices.VoteResults => body => SeimasParsers.VoteResult(body),
        SeimasServices.Projects => body => SeimasParsers.Projects(body),
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
    };

    /// <summary><c>p2b.ad_seimo_nariai@kadencijos_id=10.xml</c> → <c>p2b.ad_seimo_nariai</c>.</summary>
    public static string ServiceOf(string file) => System.IO.Path.GetFileNameWithoutExtension(file).Split('@')[0];
}
