using Lt2.Api.Contracts;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Services;

/// <summary>Marker type for the Lithuanian problem texts in ProblemText.resx.</summary>
public sealed class ProblemText;

/// <summary>Turns module errors into RFC 9457 problems with Lithuanian messages and a stable <c>code</c>.</summary>
internal sealed class ErrorMapper(IStringLocalizer<ProblemText> l)
{
    public static int StatusOf(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        };
    }

    public ApiProblem ToProblem(Error error)
    {
        var status = StatusOf(error);
        var detail = l[error.Code].Value;
        return new ApiProblem
        {
            Status = status,
            Title = l[$"Title.{status}"],
            Detail = detail,
            Code = error.Code,
            Errors = error.Field is null ? null : new Dictionary<string, string[]> { [error.Field] = [detail] },
        };
    }

    public IResult ToResult(Error error)
    {
        var problem = ToProblem(error);
        var extensions = new Dictionary<string, object?> { ["code"] = problem.Code };
        return problem.Errors is null
            ? TypedResults.Problem(statusCode: problem.Status, title: problem.Title, detail: problem.Detail, extensions: extensions)
            : TypedResults.ValidationProblem(problem.Errors, detail: problem.Detail, title: problem.Title, extensions: extensions);
    }

    public IResult ValidationFailed(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(
            errors,
            detail: l["validation"],
            title: l["Title.400"],
            extensions: new Dictionary<string, object?> { ["code"] = "validation" });
}

/// <summary>Writes a problem body for API responses that would otherwise be empty (401, 403, 404, 405).</summary>
internal static class ProblemStatusPages
{
    public static async Task WriteAsync(StatusCodeContext context)
    {
        var http = context.HttpContext;
        if (!http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var l = http.RequestServices.GetRequiredService<IStringLocalizer<ProblemText>>();
        var status = http.Response.StatusCode;
        var code = status switch
        {
            StatusCodes.Status401Unauthorized => "auth.required",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not-found",
            _ => "http." + status,
        };
        await TypedResults.Problem(
                statusCode: status,
                title: l[$"Title.{status}"],
                detail: l[code],
                extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(http);
    }
}

/// <summary>Readiness: the database answers.</summary>
internal sealed class DatabaseHealthCheck(Npgsql.NpgsqlDataSource dataSource) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
        }
        catch (Npgsql.NpgsqlException ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Database unavailable", ex);
        }
    }
}
