using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// The browser script's work in C# (design 6.4): fetches a challenge and finds its number the way <c>lt2-botcheck.js</c> does,
/// with nothing taken from the production code, so a test that registers or asks for a code carries a real solution.
/// </summary>
public static class BotCheckSteps
{
    /// <summary>A solved challenge from the server, as the request's <c>botCheck</c> field.</summary>
    public static async Task<string> SolveAsync(this TestBrowser browser)
    {
        var challenge = await browser.Client.GetFromJsonAsync<BotChallengeResponse>(ApiRoutes.BotCheck.Challenge);
        return Solve(challenge!);
    }

    public static string Solve(BotChallengeResponse challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var number = Find(challenge);
        return Payload(challenge, number);
    }

    public static int Find(BotChallengeResponse challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        for (var n = 0; n <= challenge.MaxNumber; n++)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Salt + n.ToString(CultureInfo.InvariantCulture))));
            if (hash == challenge.Challenge)
            {
                return n;
            }
        }

        throw new InvalidOperationException("The challenge has no solution up to its maximum.");
    }

    public static string Payload(BotChallengeResponse challenge, int number)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var json = JsonSerializer.Serialize(new
        {
            algorithm = challenge.Algorithm,
            challenge = challenge.Challenge,
            number,
            salt = challenge.Salt,
            signature = challenge.Signature,
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The body with a solved challenge when it is a registration or a code request that carries none yet (<c>null</c>, not empty).</summary>
    public static async Task<object?> WithBotCheckAsync(this TestBrowser browser, object? body) => body switch
    {
        RegisterRequest { BotCheck: null } request => request with { BotCheck = await browser.SolveAsync() },
        RequestCodeRequest { BotCheck: null } request => request with { BotCheck = await browser.SolveAsync() },
        _ => body,
    };
}
