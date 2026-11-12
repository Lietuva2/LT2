using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// Behaves like the WebAssembly client: fetches an antiforgery token and sends it in the header with every
/// state-changing call. A fresh token is fetched for each call, since a token is bound to who is signed in.
/// </summary>
public sealed class TestBrowser(HttpClient client, Lt2Factory factory) : IDisposable
{
    public HttpClient Client { get; } = client;

    public async Task<string> AntiforgeryTokenAsync()
    {
        var response = await Client.GetFromJsonAsync<AntiforgeryTokenResponse>(ApiRoutes.Auth.Antiforgery);
        return response!.Token;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, object? body = null, bool antiforgery = true)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = body is string raw
                ? new StringContent(raw, System.Text.Encoding.UTF8, "application/json")
                : JsonContent.Create(body, body.GetType(), options: JsonSerializerOptions.Web);
        }

        if (antiforgery)
        {
            request.Headers.Add(ApiHeaders.Antiforgery, await AntiforgeryTokenAsync());
        }

        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> PostAsync(string uri, object? body = null, bool antiforgery = true) =>
        SendAsync(HttpMethod.Post, uri, body, antiforgery);

    /// <summary>Registers a new account with the trial invitation and returns the emailed code.</summary>
    public async Task<string> RegisterAsync(string email)
    {
        using var response = await PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = email, InvitationCode = TestData.Invitation, ConfirmsAdult = true });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return await factory.LatestCodeAsync(email) ?? throw new InvalidOperationException("No code was written to the outbox.");
    }

    /// <summary>Registers and signs in; returns the address.</summary>
    public async Task<string> RegisterAndSignInAsync(string? email = null)
    {
        email ??= TestData.Email();
        var code = await RegisterAsync(email);
        using var response = await PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return email;
    }

    public void Dispose() => Client.Dispose();
}

public static class FactoryExtensions
{
    /// <summary>The code in the newest sign-in email waiting in the outbox for <paramref name="email"/>.</summary>
    public static Task<string?> LatestCodeAsync(this Lt2Factory factory, string email) =>
        factory.Database.ScalarAsync<string>(
            "SELECT payload->>'code' FROM notify.outbox WHERE kind = 'email.sign-in-code' AND payload->>'to' = @to ORDER BY created_at DESC, id DESC LIMIT 1",
            ("to", email));

    public static Task<long> OutboxCountAsync(this Lt2Factory factory, string email) =>
        factory.Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @to",
            ("to", email));

    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        var problem = await response.ProblemAsync();
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
    }
}
