using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Meta;
using Lt2.Parliament;

namespace Lt2.Web.Services;

/// <summary>Server-side <see cref="IMetaApi"/>: asks the Parliament module in-process.</summary>
internal sealed class ServerMetaApi(IParliamentDataNotice parliament, ILogger<ServerMetaApi> logger) : IMetaApi
{
    public async Task<ApiResult<DataNoticeResponse>> GetDataNoticeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return ApiResult<DataNoticeResponse>.Ok(new DataNoticeResponse(await parliament.IsParliamentDataInventedAsync(cancellationToken)));
        }
#pragma warning disable CA1031 // A notice that cannot be read must not take the page down with it.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Could not read where the parliament data comes from");
            return ApiResult<DataNoticeResponse>.Ok(new DataNoticeResponse(false));
        }
    }
}
