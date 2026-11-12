using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lt2.Parliament.Import.Client;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.ImportTests.Register;

namespace Lt2.Parliament.ImportTests.Live;

internal enum Verdict
{
    Confirmed,
    Contradicted,
    NotObserved,
    Manual,
}

internal sealed record LineVerdict(RegisterLine Line, Verdict Verdict, string Detail);

/// <summary>A fetched response: its request and raw body.</summary>
internal sealed record Fetched(SeimasRequest Request, byte[] Body)
{
    public string Service => Request.Service;

    public XDocument? Document { get; } = TryLoad(Body);

    private static XDocument? TryLoad(byte[] body)
    {
        try
        {
            return XmlRead.Load(body);
        }
#pragma warning disable CA1031 // A response that is not XML is itself a finding.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}

/// <summary>
/// Gives each line of the register a verdict against a set of responses. Mechanical where a response can show it;
/// "manual" for behaviours no single response proves.
/// </summary>
internal static class ContractChecker
{
    public static IReadOnlyList<LineVerdict> Check(IReadOnlyList<RegisterLine> register, IReadOnlyList<Fetched> responses) =>
        register.Select(line => Check(line, responses)).ToList();

    private static LineVerdict Check(RegisterLine line, IReadOnlyList<Fetched> all)
    {
        var docs = all.Where(f => (line.Service.StartsWith('(') || f.Service == line.Service) && f.Document is not null).Select(f => f.Document!).ToList();
        if (docs.Count == 0 && line.Kind is "element" or "attribute" or "value" or "parameter")
        {
            return new(line, Verdict.NotObserved, "no response of this service was fetched");
        }

        return line.Kind switch
        {
            "element" => Element(line, docs),
            "attribute" => Attribute(line, docs),
            "value" => Value(line, docs),
            "parameter" => Parameter(line, all),
            "format" => Format(line, all),
            _ => Behaviour(line, all),
        };
    }

    private static LineVerdict Element(RegisterLine line, List<XDocument> docs)
    {
        var found = docs.SelectMany(d => d.Descendants()).Where(e => e.Name.LocalName == line.Name).ToList();
        if (found.Count == 0)
        {
            return new(line, Verdict.Contradicted, $"no <{line.Name}> in {docs.Count} response(s); elements seen: {ElementNames(docs)}");
        }

        if (line.Within == XmlRead.AnywhereInDocument)
        {
            return new(line, Verdict.Confirmed, $"{found.Count} found");
        }

        var inside = found.Count(e => e.Ancestors().Any(a => a.Name.LocalName == line.Within));
        return inside > 0
            ? new(line, Verdict.Confirmed, $"{inside} of {found.Count} inside <{line.Within}>")
            : new(line, Verdict.Contradicted, $"<{line.Name}> found, never inside <{line.Within}>; parents: {string.Join(", ", found.Select(e => e.Parent?.Name.LocalName).Distinct())}");
    }

    private static LineVerdict Attribute(RegisterLine line, List<XDocument> docs)
    {
        var owners = docs.SelectMany(d => d.Descendants()).Where(e => e.Name.LocalName == line.Within).ToList();
        if (owners.Count == 0)
        {
            return new(line, Verdict.NotObserved, $"no <{line.Within}> to look on");
        }

        var with = owners.Count(e => e.Attribute(line.Name) is not null);
        return with > 0
            ? new(line, Verdict.Confirmed, $"on {with} of {owners.Count} <{line.Within}>")
            : new(line, Verdict.Contradicted, $"not on any of {owners.Count} <{line.Within}>; attributes there: {string.Join(", ", owners.SelectMany(e => e.Attributes()).Select(a => a.Name.LocalName).Distinct())}");
    }

    private static LineVerdict Value(RegisterLine line, List<XDocument> docs)
    {
        var owners = docs.SelectMany(d => d.Descendants()).Where(e => e.Name.LocalName == line.Within).ToList();
        if (line.Name.Contains("bendru sutarimu", StringComparison.Ordinal))
        {
            var consensus = owners.Count(e => VoteRules.IsConsensus(e.Attribute(N.VoteType)?.Value, e.Attribute(N.Wording)?.Value));
            return consensus > 0
                ? new(line, Verdict.Confirmed, $"{consensus} consensus vote(s)")
                : new(line, Verdict.NotObserved, "no vote marked \"bendru sutarimu\" in the sample: check one known consensus item by hand");
        }

        var parts = line.Name.Split('=', 2);
        var attribute = parts[0];
        var expected = parts[1].Contains("(empty)", StringComparison.Ordinal) ? [""] : parts[1].Split(',').Select(v => v.Trim()).ToArray();
        var actual = owners.Select(e => e.Attribute(attribute)?.Value.Trim()).Where(v => v is not null).Select(v => v!).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(v => !actual.Contains(v)).ToList();
        var others = string.Join(", ", actual.Except(expected).Take(12).Select(v => $"\"{v}\""));
        return missing.Count == 0
            ? new(line, Verdict.Confirmed, others.Length > 0 ? $"also seen: {others}" : "all values seen")
            : actual.Count == 0
                ? new(line, Verdict.Contradicted, $"no {attribute} values at all")
                : new(line, Verdict.NotObserved, $"not seen: {string.Join(", ", missing.Select(v => $"\"{v}\""))}; seen: {string.Join(", ", actual.Take(12).Select(v => $"\"{v}\""))}");
    }

    private static LineVerdict Parameter(RegisterLine line, IReadOnlyList<Fetched> all)
    {
        var asked = all.Where(f => f.Service == line.Service && f.Request.Parameter == line.Name).ToList();
        if (asked.Count == 0)
        {
            return new(line, Verdict.NotObserved, "not asked with this parameter");
        }

        var answered = asked.Count(f => f.Document?.Root?.Descendants().Any() == true);
        return answered > 0
            ? new(line, Verdict.Confirmed, $"{answered} of {asked.Count} request(s) answered with data")
            : new(line, Verdict.Contradicted, $"{asked.Count} request(s) answered without data");
    }

    private static readonly Regex Id = new("_id$", RegexOptions.CultureInvariant);

    private static LineVerdict Format(RegisterLine line, IReadOnlyList<Fetched> all)
    {
        var elements = all.Where(f => f.Document is not null).SelectMany(f => f.Document!.Descendants()).ToList();
        switch (line.Id)
        {
            case "F1":
                return Values(line, elements, a => a is N.From or N.To, v => DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            case "F2":
                return Values(line, elements, a => a is N.Starts or N.Ends or N.VoteTime, v =>
                    DateTime.TryParseExact(v, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            case "F3":
                return Values(line, elements, a => Id.IsMatch(a), v => long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _));
            case "F4":
                var broken = all.Where(f => f.Document is null).Select(f => f.Request.RelativeUri).ToList();
                var namespaced = all.Where(f => f.Document?.Root is { } r && r.Name.Namespace != XNamespace.None).Select(f => f.Request.RelativeUri).ToList();
                var notUtf8 = all.Where(f => f.Document?.Declaration?.Encoding is { } e && !e.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)).Select(f => f.Request.RelativeUri).ToList();
                return broken.Count + namespaced.Count + notUtf8.Count == 0
                    ? new(line, Verdict.Confirmed, $"{all.Count} responses")
                    : new(line, Verdict.Contradicted, $"not XML: {string.Join(", ", broken)}; namespaced: {string.Join(", ", namespaced)}; other encoding: {string.Join(", ", notUtf8)}");
            default:
                return new(line, Verdict.Manual, "no automatic check");
        }
    }

    private static LineVerdict Values(RegisterLine line, List<XElement> elements, Func<string, bool> attribute, Func<string, bool> valid)
    {
        var values = elements.SelectMany(e => e.Attributes()).Where(a => attribute(a.Name.LocalName) && a.Value.Trim().Length > 0).Select(a => a.Value.Trim()).ToList();
        if (values.Count == 0)
        {
            return new(line, Verdict.NotObserved, "no such values");
        }

        var bad = values.Where(v => !valid(v)).Distinct().Take(5).ToList();
        return bad.Count == 0
            ? new(line, Verdict.Confirmed, $"{values.Count} values")
            : new(line, Verdict.Contradicted, $"examples that do not fit: {string.Join(", ", bad.Select(v => $"\"{v}\""))}");
    }

    private static LineVerdict Behaviour(RegisterLine line, IReadOnlyList<Fetched> all)
    {
        var elements = all.Where(f => f.Service == line.Service && f.Document is not null).SelectMany(f => f.Document!.Descendants()).ToList();
        switch (line.Id)
        {
            case "M22":
                var ended = elements.Count(e => e.Name.LocalName == N.Member && !string.IsNullOrWhiteSpace(e.Attribute(N.To)?.Value));
                return ended > 0 ? new(line, Verdict.Confirmed, $"{ended} MP(s) with data_iki") : new(line, Verdict.NotObserved, "no MP with data_iki in the term");
            case "M23":
                var names = elements.Where(e => e.Name.LocalName == N.Membership).Select(e => e.Attribute(N.BodyName)?.Value ?? "").Distinct().ToList();
                var unknown = names.Where(n => VoteRules.Body(n) == BodyKind.Other).ToList();
                return new(line, Verdict.Manual, $"{names.Count} bodies; kind unknown for: {string.Join("; ", unknown.Take(20))}");
            case "R17":
                var empty = elements.Count(e => e.Name.LocalName == N.Ballot && (e.Attribute(N.Choice)?.Value.Trim() ?? "x").Length == 0);
                return empty > 0 ? new(line, Verdict.Confirmed, $"{empty} empty choices") : new(line, Verdict.NotObserved, "every MP in the sample voted");
            case "R19":
                return Sums(line, all.Where(f => f.Service == SeimasServices.VoteResults && f.Document is not null).ToList());
            case "V4":
                var wordings = all.Where(f => f.Service == SeimasServices.Proceedings && f.Document is not null)
                    .SelectMany(f => f.Document!.Descendants().Where(e => e.Name.LocalName == N.Vote))
                    .Select(e => e.Attribute(N.Wording)?.Value ?? "").ToList();
                var other = wordings.Where(w => VoteRules.Classify(w, null).Kind == VoteKind.Other).Distinct().Take(15);
                return new(line, Verdict.Manual, $"{wordings.Count} wordings; unclassified: {string.Join(" | ", other)}");
            default:
                return new(line, Verdict.Manual, "check by hand");
        }
    }

    private static LineVerdict Sums(RegisterLine line, List<Fetched> results)
    {
        var checkedVotes = 0;
        var problems = new List<string>();
        foreach (var result in results)
        {
            ParsedVoteResult? parsed;
            try
            {
                parsed = SeimasParsers.VoteResult(result.Body);
            }
            catch (SeimasFormatException)
            {
                continue;
            }

            if (parsed is null || parsed.Ballots.Count == 0)
            {
                continue;
            }

            checkedVotes++;
            var sums = (For: parsed.Ballots.Count(b => b.Choice == BallotChoice.For), Against: parsed.Ballots.Count(b => b.Choice == BallotChoice.Against), Abstain: parsed.Ballots.Count(b => b.Choice == BallotChoice.Abstain));
            var match = sums.For == parsed.Totals.For && sums.Against == parsed.Totals.Against && sums.Abstain == parsed.Totals.Abstain;
            if (!match && parsed.OfficialNote is null)
            {
                problems.Add($"{parsed.VoteId}: rows {sums.For}:{sums.Against}:{sums.Abstain}, totals {parsed.Totals.For}:{parsed.Totals.Against}:{parsed.Totals.Abstain}, no note");
            }
        }

        return checkedVotes == 0 ? new(line, Verdict.NotObserved, "no vote with per-MP rows")
            : problems.Count == 0 ? new(line, Verdict.Confirmed, $"{checkedVotes} vote(s) sum or carry the note")
            : new(line, Verdict.Contradicted, string.Join("; ", problems));
    }

    private static string ElementNames(List<XDocument> docs) =>
        string.Join(", ", docs.SelectMany(d => d.Descendants()).Select(e => e.Name.LocalName).Distinct().Take(20));

    /// <summary>The report: counts first, then every line.</summary>
    public static string Markdown(IReadOnlyList<LineVerdict> verdicts, IReadOnlyList<string> parseErrors, IReadOnlyList<string> fetchErrors, string source, DateTimeOffset when)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Contract report: {source}, {when:yyyy-MM-dd HH:mm} UTC");
        text.AppendLine();
        var seen = verdicts.Count(v => v.Line.IsSeen);
        text.AppendLine(CultureInfo.InvariantCulture, $"Register: {verdicts.Count} lines, {seen} seen, {verdicts.Count - seen} assumed.");
        foreach (var group in verdicts.GroupBy(v => v.Verdict).OrderBy(g => g.Key))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {group.Key}: {group.Count()} ({group.Count(v => !v.Line.IsSeen)} of them assumed)");
        }

        text.AppendLine();
        text.AppendLine("## Parser errors");
        text.AppendLine(parseErrors.Count == 0 ? "None." : string.Join(Environment.NewLine, parseErrors.Select(e => "- " + e)));
        text.AppendLine();
        text.AppendLine("## Requests that failed");
        text.AppendLine(fetchErrors.Count == 0 ? "None." : string.Join(Environment.NewLine, fetchErrors.Select(e => "- " + e)));
        text.AppendLine();
        text.AppendLine("## Every line");
        text.AppendLine();
        text.AppendLine("| Id | Status | Verdict | Line | Detail |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var v in verdicts.OrderBy(v => v.Verdict == Verdict.Contradicted ? 0 : 1))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {v.Line.Id} | {v.Line.Status} | {v.Verdict} | {v.Line.Kind} {v.Line.Name} in {v.Line.Within} ({v.Line.Service}) | {v.Detail.Replace("|", "/", StringComparison.Ordinal)} |");
        }

        return text.ToString();
    }
}
