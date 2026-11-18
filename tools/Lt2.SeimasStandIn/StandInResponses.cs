using System.Collections.Concurrent;
using System.Text;

namespace Lt2.SeimasStandIn;

/// <summary>A canned answer.</summary>
internal sealed record StandInResponse(int Status, byte[] Body);

/// <summary>
/// The canned responses, one file per request: <c>&lt;service&gt;.xml</c> for a service without parameters, or
/// <c>&lt;service&gt;@&lt;parameter&gt;=&lt;value&gt;.xml</c>, for example
/// <c>p2b.ad_seimo_nariai@kadencijos_id=10.xml</c>. A known service asked about an unknown identifier answers an
/// empty document; an unknown service answers 404.
/// </summary>
internal sealed class StandInResponses
{
    /// <summary>Sent with every response; the import marks what it stores from it as invented data.</summary>
    public const string SyntheticHeader = "X-Lt2-Synthetic";

    public const string EmptyDocument =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- LT2-SYNTHETIC: stand-in answer for an identifier it has no canned response for. -->\n<SeimoInformacija/>\n";

    private readonly ConcurrentDictionary<string, byte[]> _responses;
    private readonly HashSet<string> _services;

    /// <summary>The invented upcoming-agenda page (an lrs.lt page, not open data), with date tokens; null when absent.</summary>
    public string? AgendaTemplate { get; private set; }

    private StandInResponses(Dictionary<string, byte[]> responses)
    {
        _responses = new ConcurrentDictionary<string, byte[]>(responses, StringComparer.Ordinal);
        _services = responses.Keys.Select(k => k.Split('?')[0]).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The local overlay's answers, rendered when asked: their <c>{{day:N}}</c> and <c>{{past:N}}</c> (<see cref="RenderDays"/>)
    /// date the recent sitting days and votes relative to today (design 8.11.1, "recently voted").
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _dated = new(StringComparer.Ordinal);

    public static StandInResponses Load(string directory) => Load(directory, null);

    /// <summary>
    /// The canned answers of <paramref name="directory"/>, and, when given and present, those of <paramref name="overlay"/>
    /// in their place (the local container's <c>Recent/</c>; tests never load it, so their data stays fixed).
    /// </summary>
    public static StandInResponses Load(string directory, string? overlay)
    {
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.xml"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            responses[KeyFromFileName(name)] = File.ReadAllBytes(file);
        }

        if (responses.Count == 0)
        {
            throw new InvalidOperationException($"No canned responses in {directory}.");
        }

        var dated = new Dictionary<string, string>(StringComparer.Ordinal);
        var agenda = Path.Combine(directory, "agenda", "darbotvarke.html");
        if (overlay is not null && Directory.Exists(overlay))
        {
            foreach (var file in Directory.EnumerateFiles(overlay, "*.xml"))
            {
                var key = KeyFromFileName(Path.GetFileNameWithoutExtension(file));
                responses[key] = File.ReadAllBytes(file);
                dated[key] = File.ReadAllText(file);
            }

            var overlayAgenda = Path.Combine(overlay, "agenda", "darbotvarke.html");
            agenda = File.Exists(overlayAgenda) ? overlayAgenda : agenda;
        }

        var loaded = new StandInResponses(responses) { AgendaTemplate = File.Exists(agenda) ? File.ReadAllText(agenda) : null };
        foreach (var (key, text) in dated)
        {
            loaded._dated[key] = text;
        }

        return loaded;
    }

    /// <summary>
    /// <c>{{day:N}}</c> becomes <paramref name="today"/> plus N days, and <c>{{past:N}}</c> the N-th Tuesday or Thursday
    /// before <paramref name="today"/> (the overlay's recent sitting days), both <c>yyyy-MM-dd</c>.
    /// </summary>
    public static string RenderDays(string template, DateOnly today) =>
        System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{(day|past):(-?\d+)\}\}",
            m =>
            {
                var n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                var day = m.Groups[1].Value == "day" ? today.AddDays(n) : SittingDayBefore(today, n);
                return day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            });

    /// <summary>The <paramref name="n"/>-th Tuesday or Thursday before <paramref name="today"/> (1: the most recent).</summary>
    public static DateOnly SittingDayBefore(DateOnly today, int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        var day = today;
        while (n > 0)
        {
            day = day.AddDays(-1);
            if (day.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday)
            {
                n--;
            }
        }

        return day;
    }

    /// <summary>
    /// The agenda page for <paramref name="today"/>: <c>{{sitting:N}}</c> becomes the N-th Tuesday or Thursday at least
    /// two days ahead, written "2026 m. lapkričio 17 d. (antradienis)".
    /// </summary>
    public static string RenderAgenda(string template, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);
        string[] months = ["sausio", "vasario", "kovo", "balandžio", "gegužės", "birželio", "liepos", "rugpjūčio", "rugsėjo", "spalio", "lapkričio", "gruodžio"];
        var sittings = Enumerable.Range(2, 30).Select(today.AddDays)
            .Where(d => d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday)
            .Take(6)
            .ToList();
        for (var n = 1; n <= sittings.Count; n++)
        {
            var d = sittings[n - 1];
            var weekday = d.DayOfWeek == DayOfWeek.Tuesday ? "antradienis" : "ketvirtadienis";
            template = template.Replace(
                "{{sitting:" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}",
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{d.Year} m. {months[d.Month - 1]} {d.Day} d. ({weekday})"),
                StringComparison.Ordinal);
        }

        return template;
    }

    /// <summary><c>p2b.ad_seimo_nariai@kadencijos_id=10</c> → <c>p2b.ad_seimo_nariai?kadencijos_id=10</c>.</summary>
    public static string KeyFromFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var at = name.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? name : name[..at] + "?" + name[(at + 1)..];
    }

    public static string Key(string service, IEnumerable<KeyValuePair<string, string>> query)
    {
        var parameters = query.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value).ToList();
        return parameters.Count == 0 ? service : service + "?" + string.Join("&", parameters);
    }

    public IReadOnlyCollection<string> Keys => _responses.Keys.ToList();

    public StandInResponse Respond(string service, IEnumerable<KeyValuePair<string, string>> query)
    {
        var key = Key(service, query);
        if (_dated.TryGetValue(key, out var dated))
        {
            return new StandInResponse(200, Encoding.UTF8.GetBytes(RenderDays(dated, StandInHandler.Today())));
        }

        if (_responses.TryGetValue(key, out var body))
        {
            return new StandInResponse(200, body);
        }

        return _services.Contains(service)
            ? new StandInResponse(200, Encoding.UTF8.GetBytes(EmptyDocument))
            : new StandInResponse(404, Encoding.UTF8.GetBytes("Unknown service."));
    }

    /// <summary>Tests: replace the answer to one request, as if the Seimas had changed its data.</summary>
    public void Replace(string key, string body) => _responses[key] = Encoding.UTF8.GetBytes(body);

    /// <summary>Tests: the current answer to one request.</summary>
    public string Text(string key) => Encoding.UTF8.GetString(_responses[key]);

    /// <summary>The answers as loaded, for a test that changes some of them.</summary>
    public StandInResponses Copy() => new(new Dictionary<string, byte[]>(_responses, StringComparer.Ordinal)) { AgendaTemplate = AgendaTemplate };

    /// <summary>Tests: replace the agenda page, as if lrs.lt had changed it.</summary>
    public void ReplaceAgenda(string template) => AgendaTemplate = template;
}
