using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Mps;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// Comments composed by the host (design 8.23): Identity says whom the account speaks for as an MP and whether the session is a
/// passkey session (read from the database per request), Parliament gives the MPs' names and whether the mandate has ended,
/// Appeals keeps the comments. No answer or log here names the account behind an organisation's or an MP's comment.
/// </summary>
internal sealed class CommentReads(IComments comments, ICommentModeration moderation, IPoliticians politicians, IMpDirectory mps)
{
    public async Task<Result<CommentThreadResponse>> ThreadAsync(string kind, Guid targetId, int? limit, string? cursor, CancellationToken ct)
    {
        var result = await comments.ThreadAsync(kind, targetId, cursor, limit, ct);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        var t = result.Value!;
        var names = await MpNamesAsync(t.Items.Select(i => i.Author.SeimasPersonId), ct);
        return new CommentThreadResponse(
            t.Kind, t.TargetId, t.BillNumber, t.Count, t.Open, t.BillOpen, t.Items.Select(i => Map(i, names)).ToList(),
            t.Cards.Select(c => new AuthorCardResponse(c.ProfileId, c.Name, c.About)).ToList(),
            t.NextCursor);
    }

    public async Task<Result<CommentPlaceResponse>> PlaceAsync(Guid commentId, CancellationToken ct)
    {
        var place = await comments.PlaceAsync(commentId, ct);
        return place.IsSuccess
            ? new CommentPlaceResponse(place.Value!.CommentId, place.Value.Kind, place.Value.TargetId, place.Value.BillNumber, place.Value.Position)
            : place.Error!;
    }

    public async Task<Result<MyCommentsResponse>> MineAsync(ClaimsPrincipal user, string number, CancellationToken ct)
    {
        if (user.UserId() is not { } id)
        {
            return Error.Unauthorized("auth.required");
        }

        var mine = await comments.MineAsync(id, number, await SpeakerAsync(user, ct), ct);
        if (!mine.IsSuccess)
        {
            return mine.Error!;
        }

        var m = mine.Value!;
        return new MyCommentsResponse(
            m.AgreedBefore, m.Restricted, m.MpAccount, m.Deletable, m.Written, m.Agreed,
            m.Voices.Select(v => new CommentTargetVoiceResponse(v.Kind, v.TargetId, v.Voice, v.Refusal)).ToList(),
            m.PerTargetLimit, m.WrittenPerTarget) {
            Editable = m.Editable,
            Own = m.Own,
            Hidden = m.Hidden.Select(h => new MyHiddenCommentResponse(h.Id, h.Kind, h.TargetId, h.Text, h.CreatedAt, h.Statement)).ToList(),
        };
    }

    public async Task<Result<CommentCreatedResponse>> WriteAsync(ClaimsPrincipal user, string kind, Guid targetId, CommentRequest request, CancellationToken ct)
    {
        if (user.UserId() is not { } id)
        {
            return Error.Unauthorized("auth.required");
        }

        var written = await comments.WriteAsync(id, kind, targetId, request.Text, await SpeakerAsync(user, ct), ct);
        return written.IsSuccess ? new CommentCreatedResponse(written.Value!.Id) : written.Error!;
    }

    public async Task<Result<Unit>> DeleteAsync(ClaimsPrincipal user, Guid commentId, CancellationToken ct) =>
        user.UserId() is { } id ? await comments.DeleteAsync(id, commentId, await SpeakerAsync(user, ct), ct) : Error.Unauthorized("auth.required");

    public async Task<Result<Unit>> EditAsync(ClaimsPrincipal user, Guid commentId, CommentRequest request, CancellationToken ct) =>
        user.UserId() is { } id ? await comments.EditAsync(id, commentId, request.Text, await SpeakerAsync(user, ct), ct) : Error.Unauthorized("auth.required");

    public async Task<CommentModerationResponse> ModerationAsync(CancellationToken ct)
    {
        var items = await moderation.ItemsAsync(ct);
        var names = await MpNamesAsync(items.Reported.Concat(items.Hidden).SelectMany(q => new[] { q.Author.SeimasPersonId, q.Target.SeimasPersonId }), ct);
        return new CommentModerationResponse(
            items.Reported.Select(q => Map(q, names)).ToList(), items.Hidden.Select(q => Map(q, names)).ToList(), items.ReportedTotal, items.HiddenTotal);
    }

    public async Task<Result<IReadOnlyList<QueueCommentResponse>>> OnBillAsync(string number, CancellationToken ct)
    {
        var result = await moderation.OnBillAsync(number, ct);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        var names = await MpNamesAsync(result.Value!.SelectMany(q => new[] { q.Author.SeimasPersonId, q.Target.SeimasPersonId }), ct);
        return Result<IReadOnlyList<QueueCommentResponse>>.Success(result.Value!.Select(q => Map(q, names)).ToList());
    }

    /// <summary>
    /// Whom the account speaks for as an MP (design 8.5.4, 8.23.2): the MP, whether this is a passkey session and whether the
    /// mandate has not ended. Null for an account that speaks for no MP.
    /// </summary>
    private async Task<CommentSpeaker?> SpeakerAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (await politicians.SpeakerAsync(user, ct) is not { } speaker)
        {
            return null;
        }

        var sitting = (await mps.SittingMpsAsync(ct)).Any(m => m.SeimasPersonId == speaker.SeimasPersonId);
        return new CommentSpeaker(speaker.SeimasPersonId, speaker.MayWrite, sitting);
    }

    private async Task<IReadOnlyDictionary<long, MpFacts>> MpNamesAsync(IEnumerable<long?> persons, CancellationToken ct)
    {
        var ids = persons.OfType<long>().Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<long, MpFacts>() : await mps.MpsAsync(ids, ct);
    }

    private static CommentAuthorResponse Map(CommentAuthor a, IReadOnlyDictionary<long, MpFacts> names) => a.Voice switch
    {
        "organisation" => new CommentAuthorResponse(a.Voice, a.OrganisationName, a.OrganisationSlug, a.Label is { } l ? new OrganisationLabelResponse(l.ConfirmedBy, l.Domain) : null, null),
        "mp" => new CommentAuthorResponse(a.Voice, null, null, null, Mp(a.SeimasPersonId, names)),
        _ => new CommentAuthorResponse(a.Voice, a.DisplayName, null, null, null, a.ProfileId),
    };

    private static MpResponderResponse? Mp(long? person, IReadOnlyDictionary<long, MpFacts> names) =>
        person is not { } p ? null
        : names.TryGetValue(p, out var mp) ? new MpResponderResponse(mp.Slug, mp.FirstName, mp.LastName, null)
        : new MpResponderResponse("", "", p.ToString(System.Globalization.CultureInfo.InvariantCulture), null);

    private static CommentResponse Map(CommentView v, IReadOnlyDictionary<long, MpFacts> names) =>
        new(v.Id, Map(v.Author, names), v.Text, v.CreatedAt, v.AgreedBy.Select(x => new CommentAgreerResponse(x.Name, x.ProfileId)).ToList());

    private static QueueCommentResponse Map(QueueComment q, IReadOnlyDictionary<long, MpFacts> names) => new(
        q.Id, AppealMapping.Map(q.Bill), Map(q.Author, names), q.Text, q.CreatedAt, q.Status, q.Reports.Select(AppealMapping.Map).ToList(),
        new CommentContextResponse(q.Target.Kind, q.Target.TargetId, q.Target.Text, q.Target.AuthorName, q.Target.OrganisationName, Mp(q.Target.SeimasPersonId, names), q.Target.Shown),
        q.HiddenAt, q.HiddenBy, q.HideStatement);
}

/// <summary>
/// Server-side <see cref="ICommentsApi"/>: the server-rendered pages and prerendered islands call the same composition
/// in-process, as the signed-in person of the request.
/// </summary>
internal sealed class ServerCommentsApi(CommentReads reads, IComments comments, IHttpContextAccessor accessor, ErrorMapper errors) : ICommentsApi
{
    public async Task<ApiResult<CommentThreadResponse>> GetThreadAsync(string kind, Guid targetId, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.ThreadAsync(kind, targetId, limit, cursor, cancellationToken), r => r);

    public async Task<ApiResult<CommentPlaceResponse>> GetPlaceAsync(Guid commentId, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.PlaceAsync(commentId, cancellationToken), r => r);

    public async Task<ApiResult<MyCommentsResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MineAsync(User, number, cancellationToken), r => r);

    public async Task<ApiResult<CommentCreatedResponse>> WriteAsync(string kind, Guid targetId, CommentRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.WriteAsync(User, kind, targetId, request, cancellationToken), r => r);

    public async Task<ApiResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.DeleteAsync(User, id, cancellationToken));

    public async Task<ApiResult> EditAsync(Guid id, CommentRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.EditAsync(User, id, request, cancellationToken));

    public async Task<ApiResult> AgreeAsync(Guid id, CancellationToken cancellationToken = default) =>
        User.UserId() is { } user ? errors.ToApiResult(await comments.AgreeAsync(user, id, cancellationToken)) : ApiResult.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));

    public async Task<ApiResult> RemoveAgreementAsync(Guid id, CancellationToken cancellationToken = default) =>
        User.UserId() is { } user ? errors.ToApiResult(await comments.RemoveAgreementAsync(user, id, cancellationToken)) : ApiResult.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));

    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();
}

/// <summary>Server-side <see cref="ICommentModerationApi"/>: moderators in a staff-passkey session, as the HTTP endpoints.</summary>
internal sealed class ServerCommentModerationApi(
    CommentReads reads, ICommentModeration moderation, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors,
    IConfiguration configuration) : ICommentModerationApi
{
    private static readonly Error Forbidden = Error.Forbidden("forbidden");

    public async Task<ApiResult<CommentModerationResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator)
            ? ApiResult<CommentModerationResponse>.Ok(await reads.ModerationAsync(cancellationToken))
            : ApiResult<CommentModerationResponse>.Fail(errors.ToProblem(Forbidden));

    public async Task<ApiResult<IReadOnlyList<QueueCommentResponse>>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator)
            ? errors.ToApiResult(await reads.OnBillAsync(number, cancellationToken), r => r)
            : ApiResult<IReadOnlyList<QueueCommentResponse>>.Fail(errors.ToProblem(Forbidden));

    public Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideAsync(id, request.Statement, m, AppealMapping.Contact(configuration), cancellationToken));

    public Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.RestoreAsync(id, request.Statement, m, AppealMapping.Contact(configuration), cancellationToken));

    public Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.KeepAsync(id, request.Statement, m, cancellationToken));

    public async Task<ApiResult<AuthorViewResponse>> GetWriterAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator)
            ? errors.ToApiResult(await moderation.WriterAsync(id, cancellationToken), AppealMapping.Map)
            : ApiResult<AuthorViewResponse>.Fail(errors.ToProblem(Forbidden));

    public async Task<ApiResult<RestrictionCreatedResponse>> RestrictWriterAsync(Guid id, RestrictionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await ServerStaff.IsAllowedAsync(accessor, Policies.Moderator))
        {
            return ApiResult<RestrictionCreatedResponse>.Fail(errors.ToProblem(Forbidden));
        }

        var http = accessor.HttpContext!;
        var moderator = await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), http.RequestAborted);
        return errors.ToApiResult(
            await moderation.RestrictWriterAsync(id, request.Until, request.Statement, moderator, AppealMapping.Contact(configuration), cancellationToken),
            r => new RestrictionCreatedResponse(r));
    }

    private Task<ApiResult> ActAsync(Func<StaffActor, Task<Result<Unit>>> action) => ServerStaff.ActAsync(accessor, identity, ipHasher, errors, action);
}
