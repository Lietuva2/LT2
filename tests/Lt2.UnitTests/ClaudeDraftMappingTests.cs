using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Lt2.Appeals.Ai;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.4 with the Claude API (provider decided by the owner on 2026-11-13): the request sent and how the answer is
/// read, without the network. No test here calls the real API.
/// </summary>
public sealed class ClaudeDraftMappingTests
{
    private static readonly DraftSource Source = new("XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "Aiškinamasis raštas. Siūloma 32 % tarifą.");

    /// <summary>The wire body the SDK sends: the request's raw body data.</summary>
    private static JsonElement Body(string model = "claude-sonnet-5-5") =>
        JsonSerializer.SerializeToElement(ClaudeDraftMapping.Request(Source, model)).GetProperty("RawBodyData");

    [Fact]
    public void The_system_prompt_is_the_public_prompt_with_the_bill_filled_in_and_without_the_readers_comment()
    {
        var prompt = ClaudeDraftMapping.SystemPrompt(Source);
        prompt.ShouldStartWith("You are helping");
        prompt.ShouldContain("Bill number: XVP-0412");
        prompt.ShouldContain("Bill title: Gyventojų pajamų mokesčio įstatymo pataisa");
        prompt.ShouldNotContain("<!--");
        prompt.ShouldNotContain("{{");
        prompt.ShouldNotContain(Source.SourceText);
    }

    [Fact]
    public void The_moderators_draft_is_sent_with_the_draft_prompt_and_the_summary_job_with_its_own()
    {
        var draft = ClaudeDraftMapping.SystemPrompt(Source);
        draft.ShouldStartWith("You are helping a Lietuva 2.0 moderator prepare a neutral description of a Seimas bill.");
        draft.ShouldNotContain("If you are unsure");

        var summary = ClaudeDraftMapping.SystemPrompt(Source with { Purpose = DraftPurpose.Summary });
        summary.ShouldStartWith(
            "You are preparing a brief, neutral description of a Seimas bill for Lietuva 2.0. Visitors will see the description immediately without human review.");
        summary.ShouldContain("- If you are unsure whether the source makes clear what the bill changes, write only that same sentence. An inaccurate description is worse than no description.");
        summary.ShouldContain("Bill number: XVP-0412");
        summary.ShouldNotContain("{{");
        summary.ShouldNotContain("<!--");
        JsonSerializer.SerializeToElement(ClaudeDraftMapping.Request(Source with { Purpose = DraftPurpose.Summary }, "claude-sonnet-5-5"))
            .GetProperty("RawBodyData").GetProperty("system").GetString().ShouldBe(summary);
    }

    [Fact]
    public void The_summary_prompt_differs_from_the_draft_prompt_only_in_the_opening_and_one_more_rule()
    {
        static string[] Lines(DraftPurpose purpose) => ClaudeDraftMapping.SystemPrompt(Source with { Purpose = purpose }).Split('\n');
        var draft = Lines(DraftPurpose.ModeratorDraft);
        var summary = Lines(DraftPurpose.Summary);

        // Everything after the opening line, placeholders and the "not enough" rule included, is the same.
        summary.Skip(1).Where(l => !l.StartsWith("- If you are unsure", StringComparison.Ordinal)).ShouldBe(draft.Skip(1));
        summary.Count(l => l.StartsWith("- If you are unsure", StringComparison.Ordinal)).ShouldBe(1);
        AiPrompts.DescriptionSummary.ShouldContain("{{number}}");
        AiPrompts.DescriptionSummary.ShouldContain("{{title}}");
    }

    [Fact]
    public void The_summary_prompts_not_enough_answer_is_the_one_the_job_recognises()
    {
        var prompt = ClaudeDraftMapping.SystemPrompt(Source with { Purpose = DraftPurpose.Summary });
        prompt.ShouldContain($"„{Lt2.Appeals.Domain.SummaryRules.NotEnoughAnswer}“");
        Lt2.Appeals.Domain.SummaryRules.IsNotEnough($"„{Lt2.Appeals.Domain.SummaryRules.NotEnoughAnswer}“").ShouldBeTrue();
    }

    [Fact]
    public void The_request_sends_the_source_whole_as_the_user_message_and_nothing_else_about_anyone()
    {
        var body = Body();
        var messages = body.GetProperty("messages");
        messages.GetArrayLength().ShouldBe(1);
        messages[0].GetProperty("role").GetString().ShouldBe("user");
        messages[0].GetProperty("content").GetString().ShouldBe(Source.SourceText);
        body.GetProperty("system").GetString().ShouldBe(ClaudeDraftMapping.SystemPrompt(Source));
    }

    [Fact]
    public void The_request_sets_effort_and_the_fallback_and_none_of_what_this_model_refuses()
    {
        var body = Body().GetRawText();
        body.ShouldContain("\"max_tokens\":16000");
        body.ShouldContain("\"effort\":\"low\"");
        body.ShouldContain("\"fallbacks\":\"default\"");
        body.ShouldContain("claude-sonnet-5-5");
        body.ShouldNotContain("\"thinking\"");
        body.ShouldNotContain("budget_tokens");
        body.ShouldNotContain("temperature");
        body.ShouldNotContain("top_p");
        body.ShouldNotContain("top_k");
        body.ShouldNotContain("tool_choice");
        body.ShouldNotContain("\"assistant\"");
        ClaudeDraftMapping.Request(Source, "claude-sonnet-5-5").Betas!.Select(b => b.Raw()).ShouldContain("server-side-fallback-2026-07-01");
    }

    [Fact]
    public void The_model_comes_from_the_setting()
    {
        Body().GetProperty("model").GetString().ShouldBe("claude-sonnet-5-5");
        Body("claude-opus-5-5").GetProperty("model").GetString().ShouldBe("claude-opus-5-5");
        SettingKeys.AiDraftModel.Default.ShouldBe("claude-sonnet-5-5");
        SettingKeys.AiDraftModel.IsValid(JsonSerializer.SerializeToElement(" ")).ShouldBeFalse();
    }

    [Fact]
    public void A_finished_answer_is_the_draft()
    {
        var draft = ClaudeDraftMapping.Read("end_turn", ["  Projektas įveda trečią tarifą. ", ""]);
        draft.IsSuccess.ShouldBeTrue();
        draft.Value.ShouldBe("Projektas įveda trečią tarifą.");
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    [InlineData("pause_turn")]
    public void A_refusal_or_a_cut_off_answer_is_a_failure_never_half_a_text(string stopReason)
    {
        var draft = ClaudeDraftMapping.Read(stopReason, ["Projektas įveda"]);
        draft.IsSuccess.ShouldBeFalse();
        draft.Error!.Code.ShouldBe("draft.failed");
    }

    [Fact]
    public void An_empty_answer_is_a_failure() => ClaudeDraftMapping.Read("end_turn", [" "]).Error!.Code.ShouldBe("draft.failed");

    [Fact]
    public void The_Claude_assistant_and_its_host_exist_only_with_a_provider_and_a_key()
    {
        var providerOnly = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Provider"] = "claude", ["Ai:ApiKey"] = "" }).Build();
        Lt2.Appeals.AppealsModule.AiProviderHost(providerOnly).ShouldBeNull();
        Lt2.Appeals.AppealsModule.AddAiAssistant(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), providerOnly)
            .Any(d => d.ServiceType == typeof(IAiAssistant)).ShouldBeFalse();

        var both = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Provider"] = "claude", ["Ai:ApiKey"] = "sk-ant-pavyzdys" }).Build();
        Lt2.Appeals.AppealsModule.AiProviderHost(both).ShouldBe("api.anthropic.com");
        Lt2.Appeals.AppealsModule.AddAiAssistant(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), both)
            .Any(d => d.ServiceType == typeof(IAiAssistant)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("claude", "sk-ant-pavyzdys", true)]
    [InlineData("Claude", "sk-ant-pavyzdys", true)]
    [InlineData("claude", "", false)]
    [InlineData("claude", null, false)]
    [InlineData(null, "sk-ant-pavyzdys", false)]
    [InlineData("kitas", "sk-ant-pavyzdys", false)]
    public void Only_a_provider_and_a_key_together_configure_the_assistant(string? provider, string? key, bool configured) =>
        AiConfiguration.IsClaude(provider, key).ShouldBe(configured);
}
