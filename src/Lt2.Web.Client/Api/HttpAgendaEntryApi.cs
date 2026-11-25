using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="IAgendaEntryApi"/> (design 8.4): the agenda's entry page, "Seimo darbotvarkė".</summary>
internal sealed class HttpAgendaEntryApi(ApiHttp http) : IAgendaEntryApi
{
    public Task<ApiResult<AgendaEntryDaysResponse>> GetDaysAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<AgendaEntryDaysResponse>(HttpMethod.Get, ParliamentRoutes.AgendaEntries, null, cancellationToken);

    public Task<ApiResult<AgendaEntryResponse>> PreviewAsync(AgendaEntryPreviewRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<AgendaEntryResponse>(HttpMethod.Post, ParliamentRoutes.AgendaEntryPreview, request, cancellationToken);

    public Task<ApiResult<AgendaEntryResponse>> SaveAsync(DateOnly day, AgendaEntryRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<AgendaEntryResponse>(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(day), request, cancellationToken);
}
