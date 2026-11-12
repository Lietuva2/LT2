using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;

namespace Lt2.Web.Client.Api;

/// <summary>Small helpers that turn HTTP responses into <see cref="ApiResult"/>s with RFC 9457 problems.</summary>
internal sealed class ApiHttp(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<ApiResult> SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, uri, body, cancellationToken);
        return response.IsSuccessStatusCode ? ApiResult.Ok() : ApiResult.Fail(await ReadProblemAsync(response, cancellationToken));
    }

    public async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, uri, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResult<T>.Fail(await ReadProblemAsync(response, cancellationToken));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        return value is null ? ApiResult<T>.Fail(new ApiProblem { Status = 500, Code = "response.empty" }) : ApiResult<T>.Ok(value);
    }

    /// <summary>For endpoints that return JSON the browser passes on untouched (WebAuthn options).</summary>
    public async Task<ApiResult<string>> SendForJsonTextAsync(HttpMethod method, string uri, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, uri, null, cancellationToken);
        return response.IsSuccessStatusCode
            ? ApiResult<string>.Ok(await response.Content.ReadAsStringAsync(cancellationToken))
            : ApiResult<string>.Fail(await ReadProblemAsync(response, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(Json, cancellationToken);
            if (problem is not null)
            {
                return problem with { Status = (int)response.StatusCode };
            }
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return new ApiProblem
        {
            Status = (int)response.StatusCode,
            Code = response.StatusCode == HttpStatusCode.TooManyRequests ? "rate-limited" : "http." + (int)response.StatusCode,
        };
    }
}
