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

    /// <summary>Answers successive calls of <paramref name="method"/> in turn; the last answer repeats.</summary>
    public void OnEach<T>(string method, params ApiResult<T>[] answers) =>
        _answers[method] = new Queue<object>(answers.Select(a => (object)Task.FromResult(a)));

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(targetMethod!.Name);
        Arguments.Add(args ?? []);
        return _answers.TryGetValue(targetMethod.Name, out var answer)
            ? answer is Queue<object> queue ? (queue.Count > 1 ? queue.Dequeue() : queue.Peek()) : answer
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