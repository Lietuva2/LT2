using System.Globalization;
using System.Text.RegularExpressions;
using Lt2.Parliament.Import.Client;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.ImportTests.Register;

namespace Lt2.Parliament.ImportTests.Live;

/// <summary>What one contract run found.</summary>
internal sealed record ContractResult(IReadOnlyList<LineVerdict> Verdicts, IReadOnlyList<string> ParseErrors, IReadOnlyList<string> FetchErrors, string Report)
{
    public IReadOnlyList<LineVerdict> Contradicted => Verdicts.Where(v => v.Verdict == Verdict.Contradicted).ToList();
}

/// <summary>
/// Walks the chain on a service, saves every response byte for byte, runs every parser on it and checks every line of
/// the register. When the shapes are wrong it falls back to plain patterns to keep walking (for example vote ids found
/// anywhere), so one wrong assumption does not hide the rest.
/// </summary>
internal static partial class ContractRun
{
    /// <summary>The vote the September 2026 notes checked by hand: XVP-1586 presentation, with the mismatch comment.</summary>
    public const long KnownVote = -60034;

    public static async Task<ContractResult> RunAsync(ISeimasClient client, int termId, string saveTo, string source, TimeProvider clock, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(saveTo);
        var fetched = new List<Fetched>();
        var fetchErrors = new List<string>();

        async Task<Fetched?> Get(SeimasRequest request)
        {
            try
            {
                var response = await client.GetAsync(request, cancellationToken);
                var item = new Fetched(request, response.Body);
                fetched.Add(item);
                await File.WriteAllBytesAsync(Path.Combine(saveTo, FileName(request)), response.Body, cancellationToken);
                return item;
            }
            catch (SeimasUnavailableException ex)
            {
                fetchErrors.Add($"{request}: {ex.Message}");
                return null;
            }
        }

        await Get(SeimasRequest.Terms());
        await Get(SeimasRequest.Members(termId));
        var sessions = await Get(SeimasRequest.Sessions(termId));

        // The two latest sessions, then the three latest sittings that have started.
        var sessionIds = Ids(sessions, b => SeimasParsers.Sessions(b).OrderBy(s => s.From).Select(s => s.Id), SessionIdPattern()).TakeLast(2).ToList();
        var sittingIds = new List<long>();
        foreach (var session in sessionIds)
        {
            var sittings = await Get(SeimasRequest.Sittings(session));
            sittingIds.AddRange(Ids(sittings, b => SeimasParsers.Sittings(b).OrderBy(s => s.StartsAt).Select(s => s.Id), SittingIdPattern()));
        }

        var voteIds = new List<long>();
        foreach (var sitting in sittingIds.TakeLast(3))
        {
            await Get(SeimasRequest.Agenda(sitting));
            var proceedings = await Get(SeimasRequest.Proceedings(sitting));
            if (proceedings is not null)
            {
                try
                {
                    voteIds.AddRange(SeimasParsers.Proceedings(proceedings.Body).Votes.Where(v => !v.IsConsensus).Select(v => v.VoteId).Take(3));
                }
                catch (SeimasFormatException)
                {
                    voteIds.AddRange(VoteIdPattern().Matches(System.Text.Encoding.UTF8.GetString(proceedings.Body))
                        .Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Take(3));
                }
            }
        }

        foreach (var vote in voteIds.Append(KnownVote).Distinct())
        {
            await Get(SeimasRequest.VoteResults(vote));
        }

        var parseErrors = new List<string>();
        foreach (var item in fetched)
        {
            try
            {
                AssumptionsRegister.ParserFor(item.Service)(item.Body);
            }
            catch (Exception ex) when (ex is SeimasFormatException or System.Xml.XmlException or FormatException or OverflowException)
            {
                parseErrors.Add($"{item.Request}: {ex.Message}");
            }
        }

        var verdicts = ContractChecker.Check(AssumptionsRegister.Load(), fetched);
        var report = ContractChecker.Markdown(verdicts, parseErrors, fetchErrors, source, clock.GetUtcNow());
        await File.WriteAllTextAsync(Path.Combine(saveTo, "contract-report.md"), report, cancellationToken);
        return new ContractResult(verdicts, parseErrors, fetchErrors, report);
    }

    /// <summary>The synthetic files' naming: <c>&lt;service&gt;@&lt;parameter&gt;=&lt;value&gt;.xml</c>.</summary>
    public static string FileName(SeimasRequest request) =>
        request.Parameter is null ? request.Service + ".xml" : $"{request.Service}@{request.Parameter}={request.Value}.xml";

    private static IEnumerable<long> Ids(Fetched? response, Func<byte[], IEnumerable<long>> parse, Regex fallback)
    {
        if (response is null)
        {
            return [];
        }

        try
        {
            var parsed = parse(response.Body).ToList();
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }
        catch (Exception ex) when (ex is SeimasFormatException or System.Xml.XmlException)
        {
        }

        return fallback.Matches(System.Text.Encoding.UTF8.GetString(response.Body))
            .Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct();
    }

    [GeneratedRegex("sesijos_id=\"(-?\\d+)\"")]
    private static partial Regex SessionIdPattern();

    [GeneratedRegex("pos[eė]d[zž]io_id=\"(-?\\d+)\"")]
    private static partial Regex SittingIdPattern();

    [GeneratedRegex("bals_id=\"(-?\\d+)\"")]
    private static partial Regex VoteIdPattern();
}
