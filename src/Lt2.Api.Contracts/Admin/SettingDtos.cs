using System.Text.Json;

namespace Lt2.Api.Contracts.Admin;

public sealed record SettingResponse
{
    public string Key { get; init; } = "";

    public JsonElement Value { get; init; }

    public Guid? ChangedBy { get; init; }

    public DateTimeOffset? ChangedAt { get; init; }
}

public sealed record UpdateSettingRequest
{
    public JsonElement Value { get; init; }
}
