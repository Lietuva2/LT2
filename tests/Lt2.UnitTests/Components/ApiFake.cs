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

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(targetMethod!.Name);
        Arguments.Add(args ?? []);
        return _answers.TryGetValue(targetMethod.Name, out var answer)
            ? answer
            : throw new NotSupportedException($"{targetMethod.DeclaringType?.Name}.{targetMethod.Name} was not expected here.");
    }
}
