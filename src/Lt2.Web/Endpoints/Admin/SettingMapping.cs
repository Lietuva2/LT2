using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Audit;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

internal static class SettingMapping
{
    public static SettingResponse ToResponse(SettingValue value) => new()
    {
        Key = value.Key,
        Value = value.Value,
        Default = Lt2.SharedKernel.SettingKeys.Find(value.Key)?.DefaultValue,
        ChangedBy = value.ChangedBy,
        ChangedAt = value.ChangedAt,
    };
}
