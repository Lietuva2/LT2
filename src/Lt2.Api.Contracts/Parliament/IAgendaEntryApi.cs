namespace Lt2.Api.Contracts.Parliament;

/// <summary>
/// Design 8.4, "Entering a day's agenda" (<c>/api/v1/moderation/agenda</c>): staff enter a sitting day's agenda from its
/// e-seimas document, for a moderator or an administrator in a staff-passkey session; anyone else is refused (403).
/// </summary>
public interface IAgendaEntryApi
{
    /// <summary>The days from today that have entered rows.</summary>
    Task<ApiResult<AgendaEntryDaysResponse>> GetDaysAsync(CancellationToken cancellationToken = default);

    /// <summary>What saving would do; stores nothing.</summary>
    Task<ApiResult<AgendaEntryResponse>> PreviewAsync(AgendaEntryPreviewRequest request, CancellationToken cancellationToken = default);

    /// <summary>PUT: the day's whole entered agenda, read again and applied.</summary>
    Task<ApiResult<AgendaEntryResponse>> SaveAsync(DateOnly day, AgendaEntryRequest request, CancellationToken cancellationToken = default);
}
