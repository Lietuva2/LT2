namespace Lt2.Api.Contracts.Meta;

/// <summary>Where the data on the pages comes from.</summary>
/// <param name="ParliamentDataInvented">The parliament data was imported from the stand-in: every page must say it is invented.</param>
public sealed record DataNoticeResponse(bool ParliamentDataInvented);

/// <summary>
/// Notices every page shows about its data. The server-rendered layout uses the in-process implementation; the HTTP
/// endpoint comes with the parliament pages and their public API (design 7.4, <c>GET /api/v1/meta/imports</c>).
/// </summary>
public interface IMetaApi
{
    Task<ApiResult<DataNoticeResponse>> GetDataNoticeAsync(CancellationToken cancellationToken = default);
}
