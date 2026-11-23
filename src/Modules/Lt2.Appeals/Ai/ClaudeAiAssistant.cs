using System.Globalization;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Lt2.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Lt2.Appeals.Ai;

/// <summary>
/// Configuration section <c>Ai</c> (design 8.11.4, decided by the owner on 2026-11-13): <c>Ai:Provider</c> = <c>claude</c>
/// and <c>Ai:ApiKey</c>, the latter only ever from a secret file (<c>deploy/secrets/ai_api_key</c>), never from the repository
/// or an image, never logged. Without both the assistant is "not configured".
/// </summary>
internal static class AiConfiguration
{
    public const string Provider = "Ai:Provider";
    public const string ApiKey = "Ai:ApiKey";
    public const string Claude = "claude";

    /// <summary>The one host the provider is reached at; added to the outbound allowlist only when configured.</summary>
    public const string ClaudeHost = "api.anthropic.com";

    public const string HttpClientName = "anthropic";

    public static bool IsClaude(string? provider, string? apiKey) =>
        string.Equals(provider?.Trim(), Claude, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(apiKey);
}

/// <summary>
/// The request and the reading of the answer for the description draft, as pure functions (unit-tested without the
/// network). The system prompt is the public prompt file of the request's purpose (the moderator's draft or the summary
/// job's, design 8.13) with the bill's number and title filled in; the pasted source
/// goes in the user message, whole: an over-long source is refused before it gets here, never cut.
/// </summary>
internal static class ClaudeDraftMapping
{
    /// <summary>A short summary: low effort, room for thinking and the answer, no streaming needed.</summary>
    public const int MaxTokens = 16000;

    /// <summary>Server-side refusal fallback, on by default for this model family (the default fallback chain).</summary>
    public const string FallbackBeta = "server-side-fallback-2026-07-01";

    public static string SystemPrompt(DraftSource source)
    {
        var prompt = AiPrompts.For(source.Purpose);

        // The file's leading comment is for readers of the repository, not for the model.
        var end = prompt.IndexOf("-->", StringComparison.Ordinal);
        if (prompt.StartsWith("<!--", StringComparison.Ordinal) && end >= 0)
        {
            prompt = prompt[(end + 3)..];
        }

        return prompt.Replace("{{number}}", source.BillNumber, StringComparison.Ordinal)
            .Replace("{{title}}", source.BillTitle, StringComparison.Ordinal)
            .Trim();
    }

    /// <summary>
    /// The request for Claude Sonnet 5.5 and its family: no <c>thinking</c> setting (disabled would be a 400), no sampling
    /// parameters, no forced tool choice, no assistant prefill; effort set explicitly; the server-side fallback on.
    /// </summary>
    public static MessageCreateParams Request(DraftSource source, string model) => new()
    {
        Model = model,
        MaxTokens = MaxTokens,
        Betas = [FallbackBeta],
        Fallbacks = new Default(),
        OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
        System = SystemPrompt(source),
        Messages = [new BetaUserMessageParam { Content = source.SourceText }],
    };

    /// <summary>
    /// The draft, or a failure the moderator is told about ("juodraščio parengti nepavyko"): a refusal or a cut-off answer
    /// is never shown as a half text.
    /// </summary>
    public static Result<string> Read(string? stopReason, IEnumerable<string> texts)
    {
        if (stopReason is "refusal" or "max_tokens" or "pause_turn")
        {
            return Error.Conflict("draft.failed");
        }

        var draft = string.Join("\n", texts.Select(t => t.Trim()).Where(t => t.Length > 0)).Trim();
        return draft.Length == 0 ? Error.Conflict("draft.failed") : draft;
    }
}

/// <summary>
/// <see cref="IAiAssistant"/> through the Claude API (design 8.11.4; provider decided by the owner on 2026-11-13, for the
/// description draft only). Sends only the bill's number, its title and the pasted official text. The model is the runtime
/// setting <c>Ai:DraftModel</c>. Failures become a plain refusal for the moderator and a log line without the text or the key.
/// </summary>
internal sealed class ClaudeAiAssistant(
    IHttpClientFactory http, string apiKey, ISettings settings, ILogger<ClaudeAiAssistant> logger) : IAiAssistant
{
    public bool IsConfigured => true;

    public async Task<Result<string>> DraftDescriptionAsync(DraftSource request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = await settings.GetAsync(SettingKeys.AiDraftModel, cancellationToken);
        var client = new AnthropicClient { ApiKey = apiKey, HttpClient = http.CreateClient(AiConfiguration.HttpClientName) };
        try
        {
            var message = await client.Beta.Messages.Create(ClaudeDraftMapping.Request(request, model), cancellationToken);
            var texts = message.Content.Select(b => b.TryPickText(out var text) ? text.Text : null).OfType<string>();
            var result = ClaudeDraftMapping.Read(message.StopReason?.Raw(), texts);
            if (!result.IsSuccess)
            {
                logger.LogWarning("AI draft for bill {Bill} not usable: stop reason {StopReason}", request.BillNumber, message.StopReason?.Raw());
            }

            return result;
        }
        catch (AnthropicRateLimitException)
        {
            logger.LogWarning("AI draft for bill {Bill} refused by the provider: rate limited", request.BillNumber);
        }
        catch (AnthropicUnauthorizedException)
        {
            logger.LogError("AI draft for bill {Bill} refused by the provider: the API key was not accepted", request.BillNumber);
        }
        catch (AnthropicBadRequestException ex)
        {
            logger.LogError("AI draft for bill {Bill} refused by the provider as a bad request (model {Model}): {Status}", request.BillNumber, model, ex.GetType().Name);
        }
        catch (AnthropicIOException)
        {
            logger.LogWarning("AI draft for bill {Bill} failed: the provider could not be reached", request.BillNumber);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogWarning("AI draft for bill {Bill} failed at the provider: {Kind}", request.BillNumber, ex.GetType().Name);
        }
        catch (Lt2.SharedKernel.Http.OutboundHostNotAllowedException)
        {
            logger.LogError("AI draft for bill {Bill} failed: the provider's host is not on the outbound allowlist", request.BillNumber);
        }

        return Error.Conflict("draft.failed");
    }
}
