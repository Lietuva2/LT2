using System.Reflection;
using Lt2.Api.Contracts;

namespace Lt2.UnitTests.Components;

/// <summary>Answers the calls it was told about, by method name; any other call fails the test. For any API client interface.</summary>
public class ApiFake : DispatchProxy
{
    private readonly Dictionary<string, object> _answers = [];

    /// <summary>The methods called, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The arguments of each call, in the order of <see cref="Calls"/>.</summary>
    public List<object?[]> Arguments { get; } = [];

    public void On<T>(string method, ApiResult<T> answer) => _answers[method] = Task.FromResult(answer);

    public void On(string method, ApiResult answer) => _answers[method] = Task.FromResult(answer);

    /// <summary>Answers with a task the test completes itself, for a call that is still going.</summary>
    public void OnTask<T>(string method, Task<ApiResult<T>> answer) => _answers[method] = answer;

    /// <summary>Answers successive calls of <paramref name="method"/> in turn; the last answer repeats.</summary>
    public void OnEach<T>(string method, params ApiResult<T>[] answers) =>
        _answers[method] = new Queue<object>(answers.Select(a => (object)Task.FromResult(a)));

    /// <summary>As the generic one, for a method that answers no value.</summary>
    public void OnEach(string method, params ApiResult[] answers) =>
        _answers[method] = new Queue<object>(answers.Select(a => (object)Task.FromResult(a)));

    /// <summary>Answers each call of <paramref name="method"/> from its arguments.</summary>
    public void OnCall<T>(string method, Func<object?[], ApiResult<T>> answer) => _answers[method] = (Func<object?[], object>)(a => Task.FromResult(answer(a)));

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(targetMethod!.Name);
        Arguments.Add(args ?? []);
        return _answers.TryGetValue(targetMethod.Name, out var answer)
            ? answer is Queue<object> queue ? (queue.Count > 1 ? queue.Dequeue() : queue.Peek())
            : answer is Func<object?[], object> call ? call(args ?? [])
            : answer
            : throw new NotSupportedException($"{targetMethod.DeclaringType?.Name}.{targetMethod.Name} was not expected here.");
    }
}


/// <summary>
/// The comments' API clients (design 8.23) answering an empty thread, an empty own answer and an empty moderators' answer, for
/// component tests of the islands that show threads.
/// </summary>
public static class CommentFakes
{
    public static (ApiFake Comments, ApiFake Moderation) Add(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        var comments = DispatchProxy.Create<Lt2.Api.Contracts.Comments.ICommentsApi, ApiFake>();
        var moderation = DispatchProxy.Create<Lt2.Api.Contracts.Comments.ICommentModerationApi, ApiFake>();
        var c = (ApiFake)(object)comments;
        var m = (ApiFake)(object)moderation;
        c.On("GetThreadAsync", ApiResult<Lt2.Api.Contracts.Comments.CommentThreadResponse>.Ok(new("reason", Guid.Empty, "XVP-0412", 0, true, true, [])));
        c.On("GetMineAsync", ApiResult<Lt2.Api.Contracts.Comments.MyCommentsResponse>.Ok(new(false, false, false, [], [], [], [], 0, new Dictionary<Guid, int>())));
        m.On("GetOnBillAsync", ApiResult<IReadOnlyList<Lt2.Api.Contracts.Comments.QueueCommentResponse>>.Ok([]));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, comments);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, moderation);
        return (c, m);
    }
}
/// <summary>
/// The account's own answer (<c>GET /me</c>) as the browser reads it, for islands that ask it (MyAccountState: whether the caller
/// is an MP's own account, design 8.5.4; PasskeyStep for staff): an ordinary member unless another account is given.
/// </summary>
public static class MeFakes
{
    public static readonly Lt2.Api.Contracts.Me.MeResponse Member = new()
    {
        Email = "narys@pavyzdys.test",
        Roles = ["member"],
        RegistrationComplete = true,
    };

    /// <summary>A staff account whose <paramref name="role"/> is active in this session (design 6.2).</summary>
    public static Lt2.Api.Contracts.Me.MeResponse Staff(string role) => Member with { StaffAccount = true, ActiveStaffRoles = [role] };

    /// <summary>The MP's own linked account (design 8.5.4) in a passkey session.</summary>
    public static readonly Lt2.Api.Contracts.Me.MeResponse MpAccount = Member with
    {
        Mp = new Lt2.Api.Contracts.Mps.MpLinkResponse("jonas-petraitis", "Jonas", "Petraitis", "mp", true),
    };

    public static ApiFake Add(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Lt2.Api.Contracts.Me.MeResponse? account = null)
    {
        var me = DispatchProxy.Create<Lt2.Api.Contracts.Me.IMeApi, ApiFake>();
        var fake = (ApiFake)(object)me;
        fake.On("GetAsync", ApiResult<Lt2.Api.Contracts.Me.MeResponse>.Ok(account ?? Member));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, me);
        return fake;
    }
}

/// <summary>
/// A bill's arguments as the API answers them (design 8.12.1, owner 2026-11-19): the bill's answer with each side's first two and
/// the totals, a side's pages from the given order (cursor and limit as the API takes them), and one argument by its id.
/// </summary>
public static class ReasonFakes
{
    public static void Serve(ApiFake appeals, string bill, params Lt2.Api.Contracts.Appeals.ReasonResponse[] all)
    {
        ArgumentNullException.ThrowIfNull(appeals);
        appeals.On("GetRecordAsync", ApiResult<Lt2.Api.Contracts.Appeals.BillRecordResponse>.Ok(Record(bill, all)));
        appeals.OnCall("GetReasonsAsync", a =>
        {
            var side = (string)a[1]!;
            var ofSide = all.Where(r => r.Side == side).ToList();
            var offset = int.TryParse(a[2] as string, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var o) ? o : 0;
            var items = ofSide.Skip(offset).Take((int?)a[3] ?? Lt2.Api.Contracts.Appeals.ReasonLimits.Step).ToList();
            var next = offset + items.Count < ofSide.Count ? (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return ApiResult<Lt2.Api.Contracts.Appeals.ReasonPageResponse>.Ok(new(side, items, ofSide.Count, next, []));
        });
        appeals.OnCall("GetReasonAsync", a => all.FirstOrDefault(r => r.Id == (Guid)a[0]!) is { } found
            ? ApiResult<Lt2.Api.Contracts.Appeals.ReasonOnBillResponse>.Ok(new(bill, found, []))
            : ApiResult<Lt2.Api.Contracts.Appeals.ReasonOnBillResponse>.Fail(new ApiProblem { Status = 404, Code = "reason.not-found" }));
    }

    /// <summary>The bill's answer: each side's first two of <paramref name="reasons"/>, in their order, and each side's count.</summary>
    public static Lt2.Api.Contracts.Appeals.BillRecordResponse Record(string bill, params Lt2.Api.Contracts.Appeals.ReasonResponse[] reasons) => new(
        Guid.NewGuid(), new Lt2.Api.Contracts.Appeals.BillRefResponse(bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "", null,
        new Lt2.Api.Contracts.Appeals.SideCountsResponse(1, 1, 0, 0), new Lt2.Api.Contracts.Appeals.ThresholdResponse(100, "default", null, 1, false, false),
        reasons.GroupBy(r => r.Side).SelectMany(g => g.Take(Lt2.Api.Contracts.Appeals.ReasonLimits.First)).ToList(),
        ReasonTotals: new Lt2.Api.Contracts.Appeals.ReasonTotalsResponse(reasons.Count(r => r.Side == "for"), reasons.Count(r => r.Side == "against")));
}
