using System.Globalization;
using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Mps;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>Hand-written mapping of the Appeals module's responses to the API contracts (design 8.5.5, 8.5.6).</summary>
internal static class MpResponseMapping
{
    public static MpResponseResponse Map(ResponseView v) => new(
        v.Id, v.Status, v.Stance, v.Text, v.PublishedAt, v.EditedAt, v.WithdrawnAt, v.HideStatement,
        v.Earlier.Select(e => new MpResponseVersionResponse(e.Stance, e.Text, e.WrittenAt, e.ReplacedAt, e.Withdrawn)).ToList(),
        v.Vote is { } vote ? new MpResponseVoteResponse(vote.VoteId, vote.HeldAt, vote.Ballot) : null) { Comments = v.Comments };

    public static NewResponse ToModule(MpResponseRequest r) => new(r.Stance, r.Text);
}

/// <summary>
/// MPs' responses composed by the host (design 8.5.5 to 8.5.7): Identity says whom the signed-in account speaks for and
/// whether the session may write (read from the database on every request); Parliament gives the MPs' names, factions and
/// whether the mandate has ended; Appeals keeps the responses by the Seimas person number and the bill's number. No answer
/// or log here names the account that wrote a response.
/// </summary>
internal sealed class MpResponseReads(
    IMpResponses responses, IMpResponseModeration moderation, IPoliticians politicians, IMpDirectory mps, IParliamentQueries queries, IDigestSource digests)
{
    public static readonly Error NotLinked = Error.NotFound("mp.not-linked");
    public static readonly Error MandateEnded = Error.Conflict("response.mandate-ended");
    private static readonly StringComparer Lithuanian = StringComparer.Create(CultureInfo.GetCultureInfo("lt-LT"), ignoreCase: true);

    public async Task<Result<BillMpResponsesResponse>> OnBillAsync(string number, CancellationToken ct)
    {
        var result = await responses.OnBillAsync(number, ct);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        var bill = result.Value!;
        var who = await RespondersAsync(bill.Items.Select(i => i.SeimasPersonId).ToList(), ct);

        // Design 8.5.6: "už", "prieš", "neapsisprendė", within each by last name; then withdrawn and hidden ones, by last name.
        var items = bill.Items
            .Select(i => (Item: i, Mp: who.GetValueOrDefault(i.SeimasPersonId) ?? Unknown(i.SeimasPersonId)))
            .OrderBy(x => Order(x.Item))
            .ThenBy(x => x.Mp.LastName, Lithuanian)
            .ThenBy(x => x.Mp.FirstName, Lithuanian)
            .ThenBy(x => x.Mp.Slug, StringComparer.Ordinal)
            .Select(x => new BillMpResponseResponse(x.Mp, MpResponseMapping.Map(x.Item)))
            .ToList();
        var shown = bill.Items.Where(i => i.Status == "published").ToList();
        var counts = new MpResponseCountsResponse(shown.Count, shown.Count(i => i.Stance == "for"), shown.Count(i => i.Stance == "against"), shown.Count(i => i.Stance == "undecided"));
        return new BillMpResponsesResponse(bill.BillNumber, bill.Open, counts, items);
    }

    public async Task<Result<MpAnswersResponse>> OfMpAsync(string slug, CancellationToken ct)
    {
        if (await mps.FindMpAsync(slug, ct) is not { } mp)
        {
            return Error.NotFound("not-found");
        }

        var answers = await responses.OfPersonAsync(mp.SeimasPersonId, ct);
        return new MpAnswersResponse(mp.Slug, answers.Select(a => new MpAnswerResponse(AppealMapping.Map(a.Bill), a.BillOpen, MpResponseMapping.Map(a.Response))).ToList());
    }

    public async Task<Result<MyMpResponseResponse>> MineAsync(ClaimsPrincipal user, string number, CancellationToken ct)
    {
        if (await politicians.SpeakerAsync(user, ct) is not { } speaker || await LinkAsync(speaker, ct) is not { } link)
        {
            return NotLinked;
        }

        var own = await responses.OwnAsync(number, speaker.SeimasPersonId, ct);
        if (!own.IsSuccess)
        {
            return own.Error!;
        }

        var o = own.Value!;
        var refusal = o.Refusal ?? (await SittingAsync(link.Slug, ct) ? null : MandateEnded.Code) ?? (speaker.MayWrite ? null : PoliticianAccessCodes.PasskeyRequired);
        return new MyMpResponseResponse(o.BillNumber, link, o.Open, refusal is null, refusal, o.Response is { } r ? MpResponseMapping.Map(r) : null);
    }

    public async Task<Result<MpResponseSavedResponse>> SaveAsync(ClaimsPrincipal user, string number, MpResponseRequest request, CancellationToken ct)
    {
        var writer = await WriterAsync(user, ct);
        if (!writer.IsSuccess)
        {
            return writer.Error!;
        }

        var saved = await responses.SaveAsync(number, writer.Value!.SeimasPersonId, writer.Value.UserId, MpResponseMapping.ToModule(request), ct);
        return saved.IsSuccess ? new MpResponseSavedResponse(saved.Value!.Id, saved.Value.Status) : saved.Error!;
    }

    public async Task<Result<Unit>> WithdrawAsync(ClaimsPrincipal user, string number, CancellationToken ct)
    {
        var writer = await WriterAsync(user, ct);
        return writer.IsSuccess ? await responses.WithdrawAsync(number, writer.Value!.SeimasPersonId, writer.Value.UserId, ct) : writer.Error!;
    }

    /// <summary>
    /// Design 8.5.7: the summaries' list on a page, live: the open bills that reached the threshold and are not held, in the
    /// summaries' order, with the MP's own response. A code session may read it. Nothing about other MPs.
    /// </summary>
    public async Task<Result<MpBillsResponse>> MyBillsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (await politicians.SpeakerAsync(user, ct) is not { } speaker || await LinkAsync(speaker, ct) is not { } link)
        {
            return NotLinked;
        }

        var listed = DigestLists.InSummariesNow(await digests.CandidatesAsync(ct));
        var own = await responses.OnBillsAsync(speaker.SeimasPersonId, listed.Select(c => c.BillNumber).ToList(), ct);
        return new MpBillsResponse(
            link,
            listed.Select(c => new MpBillResponse(
                c.BillNumber, c.BillTitle, AppealMapping.Map(c.Planned), c.For, c.ForOlderThan30Days, c.Against, c.AgainstOlderThan30Days,
                own.TryGetValue(c.BillNumber, out var r) ? MpResponseMapping.Map(r) : null)).ToList());
    }

    public async Task<MpResponseModerationResponse> ModerationAsync(CancellationToken ct)
    {
        var items = await moderation.ItemsAsync(ct);
        var who = await RespondersAsync(items.Queue.Select(q => q.SeimasPersonId).Concat(items.Hidden.Select(h => h.SeimasPersonId)).Distinct().ToList(), ct);
        MpResponderResponse Mp(long person) => who.GetValueOrDefault(person) ?? Unknown(person);
        return new MpResponseModerationResponse(
            items.Queue.Select(q => new QueueMpResponseResponse(
                q.Id, AppealMapping.Map(q.Bill), Mp(q.SeimasPersonId), q.Status, q.Stance, q.Text, q.Flags, q.Reports.Select(AppealMapping.Map).ToList(),
                q.PublishedAt, q.EditedAt, q.BillOpen)).ToList(),
            items.Hidden.Select(h => new HiddenMpResponseResponse(
                h.Id, AppealMapping.Map(h.Bill), Mp(h.SeimasPersonId), h.Stance, h.Text, h.HiddenAt, h.HiddenBy, h.Statement, h.Restorable)).ToList());
    }

    /// <summary>
    /// Who may write (design 8.5.4, 8.5.5): the MP's account or an office member, linked now, in a passkey session, for an MP
    /// whose mandate has not ended. Every request asks again.
    /// </summary>
    private async Task<Result<MpSpeaker>> WriterAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (await politicians.SpeakerAsync(user, ct) is not { } speaker || await LinkAsync(speaker, ct) is not { } link)
        {
            return NotLinked;
        }

        if (!speaker.MayWrite)
        {
            return Error.Forbidden(PoliticianAccessCodes.PasskeyRequired);
        }

        return await SittingAsync(link.Slug, ct) ? speaker : MandateEnded;
    }

    private async Task<MpLinkResponse?> LinkAsync(MpSpeaker speaker, CancellationToken ct) =>
        (await mps.MpsAsync([speaker.SeimasPersonId], ct)).TryGetValue(speaker.SeimasPersonId, out var mp)
            ? new MpLinkResponse(mp.Slug, mp.FirstName, mp.LastName, speaker.Part, speaker.MayWrite)
            : null;

    /// <summary>Design 8.5.2: an MP whose mandate has ended publishes nothing new.</summary>
    private async Task<bool> SittingAsync(string slug, CancellationToken ct) =>
        (await mps.SittingMpsAsync(ct)).Any(m => string.Equals(m.Slug, slug, StringComparison.Ordinal));

    /// <summary>The MPs' public names and current factions, by Seimas person number.</summary>
    private async Task<Dictionary<long, MpResponderResponse>> RespondersAsync(List<long> persons, CancellationToken ct)
    {
        if (persons.Count == 0)
        {
            return [];
        }

        var facts = await mps.MpsAsync(persons, ct);
        var factions = (await queries.MpsAsync(new MpFilter(Limit: 300), ct)).Items.ToDictionary(m => m.Slug, m => m.Faction?.Name, StringComparer.Ordinal);
        return facts.ToDictionary(f => f.Key, f => new MpResponderResponse(f.Value.Slug, f.Value.FirstName, f.Value.LastName, factions.GetValueOrDefault(f.Value.Slug)));
    }

    private static MpResponderResponse Unknown(long person) => new("", "", person.ToString(CultureInfo.InvariantCulture), null);

    private static int Order(ResponseView v) => v.Status != "published" ? 3 : v.Stance switch
    {
        "for" => 0,
        "against" => 1,
        _ => 2,
    };
}

/// <summary>The problem code of step one's passkey rule (design 8.5.4), the same the Identity module answers.</summary>
internal static class PoliticianAccessCodes
{
    public const string PasskeyRequired = "mp.passkey-required";
}

/// <summary>
/// Server-side <see cref="IMpResponsesApi"/>: the pages rendered on the server and prerendered islands call the same
/// composition in-process, as the signed-in person of the request.
/// </summary>
internal sealed class ServerMpResponsesApi(MpResponseReads reads, IHttpContextAccessor accessor, ErrorMapper errors) : IMpResponsesApi
{
    public async Task<ApiResult<BillMpResponsesResponse>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.OnBillAsync(number, cancellationToken), r => r);

    public async Task<ApiResult<MpAnswersResponse>> GetOfMpAsync(string slug, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.OfMpAsync(slug, cancellationToken), r => r);

    public async Task<ApiResult<MyMpResponseResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MineAsync(User, number, cancellationToken), r => r);

    public async Task<ApiResult<MpResponseSavedResponse>> SaveAsync(string number, MpResponseRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.SaveAsync(User, number, request, cancellationToken), r => r);

    public async Task<ApiResult> WithdrawAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.WithdrawAsync(User, number, cancellationToken));

    public async Task<ApiResult<MpBillsResponse>> GetMyBillsAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MyBillsAsync(User, cancellationToken), r => r);

    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();
}

/// <summary>Server-side <see cref="IMpResponseModerationApi"/>: moderators in a staff-passkey session, as the HTTP endpoints.</summary>
internal sealed class ServerMpResponseModerationApi(
    MpResponseReads reads, IMpResponseModeration moderation, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors,
    IConfiguration configuration) : IMpResponseModerationApi
{
    public async Task<ApiResult<MpResponseModerationResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator)
            ? ApiResult<MpResponseModerationResponse>.Ok(await reads.ModerationAsync(cancellationToken))
            : ApiResult<MpResponseModerationResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.KeepAsync(id, request.Statement, m, cancellationToken));

    public Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideAsync(id, request.Statement, m, AppealMapping.Contact(configuration), cancellationToken));

    public Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.RestoreAsync(id, request.Statement, m, AppealMapping.Contact(configuration), cancellationToken));

    private Task<ApiResult> ActAsync(Func<StaffActor, Task<Result<Unit>>> action) => ServerStaff.ActAsync(accessor, identity, ipHasher, errors, action);
}