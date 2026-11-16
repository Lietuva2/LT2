namespace Lt2.Api.Contracts.Follows;

// Design 8.24: how often a member's emails come. Its own route beside /me/notices, so saving the switches and saving the
// choice never overwrite each other.

/// <summary>The values of the choice (design 8.24).</summary>
public static class NoticeFrequencies
{
    /// <summary>"Iš karto": within the follow job's next run, one email per run (the default).</summary>
    public const string Immediate = "immediate";

    /// <summary>"Kartą per dieną": one morning email.</summary>
    public const string Daily = "daily";

    /// <summary>"Kartą per savaitę ir artėjant balsavimui": Monday's email, and a planned sitting that cannot wait.</summary>
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<string> All = [Immediate, Daily, Weekly];
}

/// <summary>The caller's choice: <c>immediate</c>, <c>daily</c> or <c>weekly</c>.</summary>
public sealed record NoticeFrequencyResponse(string Frequency);

public sealed record NoticeFrequencyRequest
{
    public string Frequency { get; init; } = NoticeFrequencies.Immediate;
}

public static class NoticeFrequencyRoutes
{
    public const string Mine = ApiRoutes.Prefix + "/me/notices/frequency";
}

/// <summary>Design 8.24: the caller's choice of how often emails come. Needs a signed-in member.</summary>
public interface INoticeFrequencyApi
{
    Task<ApiResult<NoticeFrequencyResponse>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary><c>notices.frequency-invalid</c>.</summary>
    Task<ApiResult> SetAsync(NoticeFrequencyRequest request, CancellationToken cancellationToken = default);
}
