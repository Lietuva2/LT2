namespace Lt2.Api.Contracts;

/// <summary>An RFC 9457 problem as the client sees it.</summary>
public sealed record ApiProblem
{
    public int Status { get; init; }

    public string? Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>Stable error code, for example <c>invitation.invalid</c>.</summary>
    public string? Code { get; init; }

    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}

/// <summary>Outcome of an API call without a body.</summary>
public class ApiResult
{
    protected ApiResult(ApiProblem? problem) => Problem = problem;

    public ApiProblem? Problem { get; }

    public bool IsSuccess => Problem is null;

    public static ApiResult Ok() => new(null);

    public static ApiResult Fail(ApiProblem problem) => new(problem);
}

/// <summary>Outcome of an API call with a body.</summary>
public sealed class ApiResult<T> : ApiResult
{
    private ApiResult(T? value, ApiProblem? problem) : base(problem) => Value = value;

    public T? Value { get; }

    public static ApiResult<T> Ok(T value) => new(value, null);

    public static new ApiResult<T> Fail(ApiProblem problem) => new(default, problem);
}
