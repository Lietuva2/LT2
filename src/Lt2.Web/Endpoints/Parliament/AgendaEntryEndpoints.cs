using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary>
/// Design 8.4, "Entering a day's agenda", for a moderator or an administrator in a staff-passkey session:
/// <c>GET /api/v1/moderation/agenda</c> lists the entered days from today (day, bills, last saved, document);
/// <c>POST …/agenda/preview</c> says what saving a day's lines would do and stores nothing; <c>PUT …/agenda/{day}</c> reads the
/// lines again and applies them as the day's whole entered agenda, audited (<c>agenda.entered</c>). Refusals:
/// <c>agenda-entry.day</c>, <c>agenda-entry.source</c>, <c>agenda-entry.lines</c>, <c>agenda-entry.empty</c>,
/// <c>agenda-entry.too-long</c> (400).
/// </summary>
internal static class AgendaEntryEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.AgendaEntries), DaysAsync)
            .WithName("GetAgendaEntries")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<AgendaEntryDaysResponse>();

        api.MapPost(ApiEndpoints.Relative(ParliamentRoutes.AgendaEntryPreview), PreviewAsync)
            .WithName("PreviewAgendaEntry")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<AgendaEntryPreviewRequest>()
            .Produces<AgendaEntryResponse>();

        api.MapPut(ApiEndpoints.Relative(ParliamentRoutes.AgendaEntryDay), SaveAsync)
            .WithName("SaveAgendaEntry")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<AgendaEntryRequest>()
            .Produces<AgendaEntryResponse>();
    }

    private static async Task<IResult> DaysAsync(AgendaEntryWrites writes, CancellationToken cancellationToken) =>
        TypedResults.Ok(await writes.DaysAsync(cancellationToken));

    private static async Task<IResult> PreviewAsync(AgendaEntryPreviewRequest request, AgendaEntryWrites writes, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await writes.PreviewAsync(request, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> SaveAsync(
        string day, AgendaEntryRequest request, HttpContext context, AgendaEntryWrites writes, ErrorMapper errors, CancellationToken cancellationToken)
    {
        if (!AgendaEntryWrites.TryDay(day, out var date))
        {
            return errors.ToResult(Error.Validation(AgendaEntryProblems.Day, "day"));
        }

        var result = await writes.SaveAsync(date, request, await ModeratorEndpoint.ModeratorAsync(context), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }
}
