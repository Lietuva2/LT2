namespace Lt2.Api.Contracts.Meta;

/// <summary>One data source and its imports.</summary>
/// <param name="Source"><c>seimas-open-data</c>.</param>
/// <param name="LastSuccessAt">When the last successful import finished; the pages show it with the data.</param>
public sealed record ImportSourceResponse(string Source, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastRunAt, string? LastRunStatus);

/// <param name="ParliamentDataInvented">The parliament data was imported from the stand-in: every page says it is invented.</param>
public sealed record ImportsResponse(IReadOnlyList<ImportSourceResponse> Sources, bool ParliamentDataInvented);

/// <summary>How the two statistics are counted, as built; the methodology page states whatever this says.</summary>
/// <param name="TwoThirds">A faction has a position when at least this share of its members who voted chose one option.</param>
/// <param name="LabelsThatAreNotFactions">Faction labels of the per-MP results that get no faction position.</param>
public sealed record MethodologyResponse(string TwoThirds, IReadOnlyList<string> LabelsThatAreNotFactions);

/// <summary>Design 7.4 <c>GET /api/v1/meta/imports</c>: the last successful import per source; and the methodology settings.</summary>
public interface IMetaApi
{
    Task<ApiResult<ImportsResponse>> GetImportsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MethodologyResponse>> GetMethodologyAsync(CancellationToken cancellationToken = default);
}
