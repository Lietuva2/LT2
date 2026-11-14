using System.Reflection;
using Lt2.SharedKernel;

namespace Lt2.Appeals.Ai;

/// <summary>
/// The <see cref="IAiAssistant"/> while no provider is configured (design 8.11.4): no <c>Ai:Provider</c> or no key. The
/// draft button is not shown and every request is refused.
/// </summary>
internal sealed class NotConfiguredAiAssistant : IAiAssistant
{
    public bool IsConfigured => false;

    public Task<Result<string>> DraftDescriptionAsync(DraftSource request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(Error.Conflict("draft.not-configured"));
}

/// <summary>
/// The prompts sent to an AI provider, kept as files in the repository so they can be published (design 3, 8.11.4, 8.13):
/// <c>src/Modules/Lt2.Appeals/Ai/description-draft.prompt.md</c> for the moderator's draft and
/// <c>src/Modules/Lt2.Appeals/Ai/description-summary.prompt.md</c> for the summary job. The Claude adapter sends exactly
/// one of them as its system prompt.
/// </summary>
internal static class AiPrompts
{
    /// <summary>The prompt for the moderator's draft of a bill's neutral description (8.11.4).</summary>
    public static string DescriptionDraft { get; } = Read("description-draft.prompt.md");

    /// <summary>The prompt for the summary job's description, shown without a person reading it first (8.13).</summary>
    public static string DescriptionSummary { get; } = Read("description-summary.prompt.md");

    /// <summary>The prompt a request with this purpose is sent with.</summary>
    public static string For(DraftPurpose purpose) => purpose == DraftPurpose.Summary ? DescriptionSummary : DescriptionDraft;

    private static string Read(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
