using System.Globalization;

namespace Lt2.Parliament.Import.Client;

/// <summary>
/// The open-data services of the chain in design 7.1, by their path under the base address
/// (<c>https://apps.lrs.lt/sip/</c> in production).
/// </summary>
internal static class SeimasServices
{
    public const string Terms = "p2b.ad_seimo_kadencijos";
    public const string Members = "p2b.ad_seimo_nariai";
    public const string Sessions = "p2b.ad_seimo_sesijos";
    public const string Sittings = "p2b.ad_seimo_posedziai";
    public const string Agenda = "p2b.ad_sp_darbotvarke";
    public const string Proceedings = "p2b.ad_seimo_posedzio_eiga_full";
    public const string VoteResults = "p2b.ad_sp_balsavimo_rezultatai";

    /// <summary>The TAIS projects of every institution with an event on the current day; no parameter (design 7.2).</summary>
    public const string Projects = "p2b.tais_docs";

    public const string TermParameter = "kadencijos_id";
    public const string SessionParameter = "sesijos_id";
    public const string SittingParameter = "posedzio_id";
    public const string VoteParameter = "balsavimo_id";
}

/// <summary>One request to the open-data service: a service path and at most one query parameter.</summary>
internal sealed record SeimasRequest(string Service, string? Parameter = null, string? Value = null)
{
    /// <summary>The path and query relative to the base address, for example <c>p2b.ad_seimo_nariai?kadencijos_id=10</c>.
    /// Also the key under which raw responses are stored.</summary>
    public string RelativeUri => Parameter is null ? Service : $"{Service}?{Parameter}={Uri.EscapeDataString(Value ?? "")}";

    public override string ToString() => RelativeUri;

    public static SeimasRequest Terms() => new(SeimasServices.Terms);

    public static SeimasRequest Members(int termId) => new(SeimasServices.Members, SeimasServices.TermParameter, Text(termId));

    public static SeimasRequest Sessions(int termId) => new(SeimasServices.Sessions, SeimasServices.TermParameter, Text(termId));

    public static SeimasRequest Sittings(long sessionId) => new(SeimasServices.Sittings, SeimasServices.SessionParameter, Text(sessionId));

    public static SeimasRequest Agenda(long sittingId) => new(SeimasServices.Agenda, SeimasServices.SittingParameter, Text(sittingId));

    public static SeimasRequest Proceedings(long sittingId) => new(SeimasServices.Proceedings, SeimasServices.SittingParameter, Text(sittingId));

    public static SeimasRequest VoteResults(long voteId) => new(SeimasServices.VoteResults, SeimasServices.VoteParameter, Text(voteId));

    public static SeimasRequest Projects() => new(SeimasServices.Projects);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A response as received: the raw body is what gets stored (design 7.2), parsing comes later.</summary>
/// <param name="Synthetic">The response came from the stand-in service, not from the Seimas (see <see cref="SyntheticMarker"/>).</param>
internal sealed record SeimasResponse(SeimasRequest Request, Uri Url, byte[] Body, bool Synthetic, DateTimeOffset FetchedAt);

/// <summary>
/// How a stand-in response announces itself. The stand-in sends the header on every response, and every canned file
/// carries the comment; either one marks the stored document, and through it the site, as invented data. The open-data
/// client and the agenda watcher both read it here.
/// </summary>
internal static class SyntheticMarker
{
    public const string Header = "X-Lt2-Synthetic";

    public const string Comment = "LT2-SYNTHETIC";

    private static readonly byte[] CommentBytes = System.Text.Encoding.UTF8.GetBytes(Comment);

    /// <summary>Whether a response is the stand-in's: the header on it, or the comment in its body.</summary>
    public static bool IsOn(HttpResponseMessage response, byte[] body) => response.Headers.Contains(Header) || IsIn(body);

    /// <summary>Whether the body carries the comment.</summary>
    public static bool IsIn(byte[] body) => body.AsSpan().IndexOf(CommentBytes) >= 0;
}
