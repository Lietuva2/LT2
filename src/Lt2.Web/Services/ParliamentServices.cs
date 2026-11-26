using System.Security.Cryptography;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Meta;
using Lt2.Api.Contracts.Parliament;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Microsoft.Extensions.Options;

namespace Lt2.Web.Services;

/// <summary>
/// The parliament reads both the HTTP endpoints and the server-rendered pages use: the module's answers mapped to the
/// API contracts, or <c>not-found</c>.
/// </summary>
internal sealed class ParliamentReads(IParliamentQueries queries, Lt2.Appeals.IBillCards billCards, Lt2.Appeals.IAppealsModule appeals, IPlannedAgenda agenda, IMpDirectory mps, Lt2.Identity.IPoliticians politicians, IFollowerCounts followers, Lt2.Identity.IInventedMembersNotice inventedMembers)
{
    public const string ImportSource = "seimas-open-data";
    private static readonly Error NotFound = Error.NotFound("not-found");

    public async Task<Result<TermResponse>> CurrentTermAsync(CancellationToken ct) =>
        await queries.CurrentTermAsync(ct) is { } term ? Map(term) : NotFound;

    public async Task<Result<MpListResponse>> MpsAsync(string? faction, string? committee, string? q, string? cursor, CancellationToken ct)
    {
        var page = await queries.MpsAsync(new MpFilter(Blank(faction), Blank(committee), Blank(q), cursor), ct);
        return new MpListResponse(page.Items.Select(Map).ToList(), page.Total, page.NextCursor);
    }

    public async Task<Result<MpProfileResponse>> MpAsync(string slug, CancellationToken ct)
    {
        if (await queries.MpAsync(slug, ct) is not { } p)
        {
            return NotFound;
        }

        var recent = p.RecentVotes.Select(Map).ToList();
        var titles = await PlainTitlesAsync(recent.Select(v => v.Vote), ct);
        return new MpProfileResponse(
                Map(p.Mp),
                Map(p.Term),
                new MandateResponse(p.Mandate.From, p.Mandate.To, p.Mandate.NominatingParty, p.Mandate.ElectedHow, p.Mandate.Constituency, p.Mandate.TermsServed),
                p.Memberships.Select(m => new MembershipResponse(Map(m.Body), m.Role, m.From, m.To, m.Current)).ToList(),
                p.Contacts.Select(c => new ContactResponse(c.Kind, c.Value)).ToList(),
                p.Statistics is { } s ? new MpStatisticsResponse(s.VotesHeld, s.VotesTakenPart, s.Participation, s.VotesWithFactionPosition, s.VotedWithFaction, s.WithFaction, s.FreeVotes) : null,
                recent.Select(v => v with { Vote = Titled(v.Vote, titles) }).ToList(),
                await ManagedSinceAsync(slug, ct),
                await FollowersAsync(slug, ct));
    }

    /// <summary>
    /// Design 8.13.1: the readable titles of the bills a list of votes names, one read for the answer (open and closed records
    /// only; a bill without one is not in the answer).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> PlainTitlesAsync(IEnumerable<VoteLink> votes, CancellationToken ct)
    {
        var numbers = votes.Where(v => v.Bill is not null).Select(v => v.Bill!.Number).Distinct().ToList();
        return numbers.Count == 0
            ? new Dictionary<string, string>()
            : (await appeals.PlainTitlesAsync(numbers, ct)).ToDictionary(t => t.Key, t => t.Value.Title);
    }

    private static VoteLink Titled(VoteLink v, IReadOnlyDictionary<string, string> titles) =>
        v.Bill is { } b && titles.TryGetValue(b.Number, out var title) ? v with { Bill = b with { PlainTitle = title } } : v;

    private static VoteSummaryResponse Titled(VoteSummaryResponse v, IReadOnlyDictionary<string, string> titles) => v with { Vote = Titled(v.Vote, titles) };

    /// <summary>Design 8.11.2: how many follow the MP, from Notifications; a number only.</summary>
    private async Task<int> FollowersAsync(string slug, CancellationToken ct) =>
        await mps.FindMpAsync(slug, ct) is { } mp ? await followers.MpAsync(mp.SeimasPersonId, ct) : 0;

    /// <summary>Design 8.5.2: the one public trace of a link, composed from the Identity module (the host composes modules).</summary>
    private async Task<DateTimeOffset?> ManagedSinceAsync(string slug, CancellationToken ct) =>
        await mps.FindMpAsync(slug, ct) is { } mp && (await politicians.TakenOverAsync([mp.SeimasPersonId], ct)).TryGetValue(mp.SeimasPersonId, out var since)
            ? since
            : null;

    public async Task<Result<MpVotesResponse>> MpVotesAsync(string slug, string? cursor, int limit, CancellationToken ct)
    {
        if (await queries.MpVotesAsync(slug, cursor, limit, ct) is not { } page)
        {
            return NotFound;
        }

        var items = page.Items.Select(Map).ToList();
        var titles = await PlainTitlesAsync(items.Select(i => i.Vote), ct);
        return new MpVotesResponse(items.Select(i => i with { Vote = Titled(i.Vote, titles) }).ToList(), page.NextCursor);
    }

    public async Task<Result<IReadOnlyList<BodyListItem>>> BodiesAsync(string kind, CancellationToken ct) =>
        Result<IReadOnlyList<BodyListItem>>.Success((await queries.BodiesAsync(kind, ct)).Select(Map).ToList());

    public async Task<Result<BodyResponse>> BodyAsync(string kind, string slug, CancellationToken ct)
    {
        if (await queries.BodyAsync(kind, slug, ct) is not { } b)
        {
            return NotFound;
        }

        var recent = b.RecentVotes.Select(v => new FactionVoteResponse(Map(v.Vote), Map(v.Split))).ToList();
        var titles = await PlainTitlesAsync(recent.Select(v => v.Vote), ct);
        return new BodyResponse(
            Map(b.Body),
            Map(b.Term),
            b.Members.Select(m => new BodyMemberResponse(Map(m.Mp), m.Role, m.From, m.To, m.Current)).ToList(),
            recent.Select(v => v with { Vote = Titled(v.Vote, titles) }).ToList());
    }

    public async Task<Result<BillListResponse>> BillsAsync(string? q, string? cursor, CancellationToken ct)
    {
        var page = await queries.BillsAsync(await billCards.SearchAsync(Blank(q), ct), cursor, 50, ct);
        return new BillListResponse(page.Items.Select(b => new BillListItem(b.Number, b.Title, b.LastVoteAt)).ToList(), page.Total, page.NextCursor);
    }

    public async Task<Result<BillResponse>> BillAsync(string number, CancellationToken ct)
    {
        if (await queries.BillAsync(number, ct) is not { } b)
        {
            return NotFound;
        }

        var planned = (await agenda.ForBillsAsync([b.Number], ct)).GetValueOrDefault(b.Number);

        // Design 8.4 (owner, 2026-11-16, 2026-11-25): an entered date's source is the day's agenda document, its row's address;
        // a date the watcher read is published on the agenda page it reads (one address, assumption A7), kept with every
        // snapshot, since that row's own link is the bill's document (A5) and shows no date. A moderator's date has none.
        var agendaPage = planned switch
        {
            null or { SetByModerator: true } => null,
            { Entered: true } => planned.SourceUrl,
            _ => (await queries.AgendaReadAsync(ct)).WatcherUrl,
        };
        var votes = b.Votes.Select(Map).ToList();
        var titles = await PlainTitlesAsync(votes.Select(v => v.Vote), ct);
        return new BillResponse(b.Number, b.Title, b.ESeimasUrl, votes.Select(v => Titled(v, titles)).ToList(), AppealMapping.Map(planned), agendaPage);
    }

    /// <summary>A vote summary as the API writes it (the agenda's recent stage results use it too).</summary>
    public static VoteTotalsResponse Totals(VoteSummary v) =>
        new(v.Totals.For, v.Totals.Against, v.Totals.Abstain, v.Totals.Voted, v.Totals.Seats);

    /// <summary>Design 8.18: the history of votes; the kind and the search are read as <see cref="VoteListQuery"/> reads them.</summary>
    public async Task<Result<VoteListResponse>> VotesAsync(string? q, string? kind, DateOnly? until, int? limit, string? cursor, CancellationToken ct)
    {
        var historyKind = VoteListQuery.Kind(kind) switch
        {
            VoteListQuery.All => VoteHistoryKind.All,
            VoteListQuery.Adoptions => VoteHistoryKind.Adoptions,
            _ => VoteHistoryKind.StageResults,
        };
        var size = limit is { } n ? Math.Clamp(n, 1, VoteListQuery.MaxLimit) : VoteListQuery.DefaultLimit;
        var page = await queries.VoteHistoryAsync(new VoteHistoryFilter(await billCards.SearchAsync(VoteListQuery.Query(q), ct), historyKind, until, cursor, size), ct);
        var items = page.Items.Select(i => new VoteListItem(Map(i.Summary), i.AgendaItemTitle)).ToList();
        var titles = await PlainTitlesAsync(items.Select(i => i.Summary.Vote), ct);
        return new VoteListResponse(items.Select(i => i with { Summary = Titled(i.Summary, titles) }).ToList(), page.Total, page.NextCursor);
    }

    /// <summary>Design 8.18, 8.13.1: a vote with its ballots; its bill carries the readable title, as in every list of votes.</summary>
    public async Task<Result<VoteResponse>> VoteAsync(long id, CancellationToken ct)
    {
        if (await queries.VoteAsync(id, ct) is not { } v)
        {
            return NotFound;
        }

        var summary = Map(v.Summary);
        var titles = await PlainTitlesAsync([summary.Vote], ct);
        return new VoteResponse(
            Titled(summary, titles),
            v.SittingStartsAt,
            v.AgendaItemTitle,
            v.Factions.Select(Map).ToList(),
            v.Ballots.Select(b => new BallotResponse(Map(b.Mp), b.FactionLabel, b.Choice)).ToList(),
            v.ClassificationCorrected);
    }

    public async Task<Result<ImportsResponse>> ImportsAsync(CancellationToken ct)
    {
        var info = await queries.ImportsAsync(ct);
        return new ImportsResponse([new ImportSourceResponse(ImportSource, info.LastSuccessAt, info.LastRunAt, info.LastRunStatus)], info.ParliamentDataInvented,
            await inventedMembers.HoldsInventedMembersAsync(ct));
    }

    /// <summary>Design 7.2: a vote's classification as the moderators see it.</summary>
    public static VoteClassificationResponse Map(VoteClassificationState s) =>
        new(s.VoteId, s.Kind, s.Stage, s.RulesKind, s.RulesStage, s.Corrected, s.CorrectedAt, s.Statement, s.ClosesRecord);

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static TermResponse Map(TermInfo t) => new(t.Id, t.Name, t.From, t.To);

    private static BodyLink Map(BodyRef b) => new(b.Slug, b.Name, b.Kind);

    private static MpLink Map(MpRef m) => new(m.Slug, m.FirstName, m.LastName);

    private static MpListItem Map(MpSummary m) => new(m.Slug, m.FirstName, m.LastName, m.Gender, m.Sitting, m.Faction is { } f ? Map(f) : null, m.NominatingParty);

    private static BodyListItem Map(BodySummary b) => new(b.Slug, b.Name, b.Kind, b.Members);

    private static VoteLink Map(VoteRef v) =>
        new(v.Id, v.HeldAt, v.Wording, v.Kind, v.Stage, v.IsConsensus, v.HasResult, v.Bill is { } b ? new BillLink(b.Number, b.Title) : null);

    private static FactionSplitResponse Map(FactionSplit s) =>
        new(s.Label, s.Faction is { } f ? Map(f) : null, s.Position, s.For, s.Against, s.Abstain, s.DidNotVote);

    private static MpVoteResponse Map(MpVote v) => new(Map(v.Vote), v.Choice, v.Faction is { } f ? Map(f) : null);

    private static VoteSummaryResponse Map(VoteSummary v) =>
        new(Map(v.Vote), Totals(v), v.OfficialNote);
}

/// <summary>Server-side <see cref="IParliamentApi"/> for the server-rendered pages: the same reads as the endpoints, in-process.</summary>
internal sealed class ServerParliamentApi(ParliamentReads reads, ErrorMapper errors) : IParliamentApi
{
    public async Task<ApiResult<TermResponse>> GetCurrentTermAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.CurrentTermAsync(cancellationToken), x => x);

    public async Task<ApiResult<MpListResponse>> GetMpsAsync(string? faction = null, string? committee = null, string? q = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MpsAsync(faction, committee, q, cursor, cancellationToken), x => x);

    public async Task<ApiResult<MpProfileResponse>> GetMpAsync(string slug, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MpAsync(slug, cancellationToken), x => x);

    public async Task<ApiResult<MpVotesResponse>> GetMpVotesAsync(string slug, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MpVotesAsync(slug, cursor, Math.Clamp(limit ?? MpVoteLimits.Default, 1, MpVoteLimits.Max), cancellationToken), x => x);

    public async Task<ApiResult<IReadOnlyList<BodyListItem>>> GetFactionsAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.BodiesAsync(BodyKinds.Faction, cancellationToken), x => x);

    public async Task<ApiResult<BodyResponse>> GetFactionAsync(string slug, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.BodyAsync(BodyKinds.Faction, slug, cancellationToken), x => x);

    public async Task<ApiResult<IReadOnlyList<BodyListItem>>> GetCommitteesAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.BodiesAsync(BodyKinds.Committee, cancellationToken), x => x);

    public async Task<ApiResult<BodyResponse>> GetCommitteeAsync(string slug, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.BodyAsync(BodyKinds.Committee, slug, cancellationToken), x => x);

    public async Task<ApiResult<BillListResponse>> GetBillsAsync(string? q = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.BillsAsync(q, cursor, cancellationToken), x => x);

    public async Task<ApiResult<BillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.BillAsync(number, cancellationToken), x => x);

    public async Task<ApiResult<VoteListResponse>> GetVotesAsync(
        string? q = null, string? kind = null, DateOnly? until = null, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.VotesAsync(q, kind, until, limit, cursor, cancellationToken), x => x);

    public async Task<ApiResult<VoteResponse>> GetVoteAsync(long id, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.VoteAsync(id, cancellationToken), x => x);
}

/// <summary>
/// Server-side <see cref="IVoteModerationApi"/> (design 7.2), for the vote page prerendered for a moderator: the same policy
/// as the endpoints, applied to the current request, and the action run as that moderator.
/// </summary>
internal sealed class ServerVoteModerationApi(
    IVoteClassifications classifications, Lt2.Identity.IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IVoteModerationApi
{
    public async Task<ApiResult<VoteClassificationResponse>> GetClassificationAsync(long voteId, CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Lt2.Web.Security.Policies.Moderator)
            ? errors.ToApiResult(await classifications.StateAsync(voteId, cancellationToken), ParliamentReads.Map)
            : ApiResult<VoteClassificationResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> CorrectClassificationAsync(long voteId, VoteClassificationRequest request, CancellationToken cancellationToken = default) =>
        ServerStaff.ActAsync(accessor, identity, ipHasher, errors, m => classifications.CorrectAsync(voteId, request.Kind, request.Stage, request.Statement, m, cancellationToken));

    public Task<ApiResult> ResetClassificationAsync(long voteId, VoteClassificationResetRequest request, CancellationToken cancellationToken = default) =>
        ServerStaff.ActAsync(accessor, identity, ipHasher, errors, m => classifications.ResetAsync(voteId, request.Statement, m, cancellationToken));
}

/// <summary>Server-side <see cref="IMetaApi"/>. A failure to read it must not take the page down with it.</summary>
internal sealed class ServerMetaApi(ParliamentReads reads, ILogger<ServerMetaApi> logger) : IMetaApi
{
    public async Task<ApiResult<ImportsResponse>> GetImportsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return ApiResult<ImportsResponse>.Ok((await reads.ImportsAsync(cancellationToken)).Value);
        }
#pragma warning disable CA1031 // The layout asks on every page; an unreadable notice must not break the page.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Could not read the import state");
            return ApiResult<ImportsResponse>.Ok(new ImportsResponse([], false));
        }
    }
}

/// <summary>
/// Design 7.4: the public API is cached with ETags. The body is serialised once, its hash is the ETag, a matching
/// <c>If-None-Match</c> gets 304, and shared caches may keep it for a minute.
/// </summary>
internal static class PublicJson
{
    public static IResult From<T>(HttpContext http, ErrorMapper errors, Result<T> result) => From(http, errors, result, 60);

    /// <summary>As above; <paramref name="maxAgeSeconds"/> 0 makes caches revalidate every time (numbers that change by the minute).</summary>
    public static IResult From<T>(HttpContext http, ErrorMapper errors, Result<T> result, int maxAgeSeconds)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsSuccess)
        {
            return errors.ToResult(result.Error!);
        }

        var options = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var body = JsonSerializer.SerializeToUtf8Bytes(result.Value, options);
        var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(body))[..32] + "\"";
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = maxAgeSeconds > 0
            ? "public, max-age=" + maxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "public, no-cache";
        if (http.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Select(s => s.Trim()).Contains(etag)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Bytes(body, "application/json; charset=utf-8");
    }
}
