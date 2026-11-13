using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.10: feedback from members.</summary>
public sealed class FeedbackRuleTests
{
    [Fact]
    public void Feedback_is_one_text_of_up_to_2000_characters()
    {
        FeedbackRules.CheckText("Neveikia mygtukas.").ShouldBeNull();
        FeedbackRules.CheckText(new string('a', 2000)).ShouldBeNull();
        FeedbackRules.CheckText(new string('a', 2001)).ShouldBe("feedback.too-long");
        FeedbackRules.CheckText("   ").ShouldBe("feedback.empty");
    }

    [Fact]
    public void The_kind_is_optional_and_one_of_four()
    {
        FeedbackRules.CheckKind(null).ShouldBeNull();
        foreach (var kind in new[] { "bug", "idea", "unclear", "other" })
        {
            FeedbackRules.CheckKind(kind).ShouldBeNull();
        }

        FeedbackRules.CheckKind("complaint").ShouldBe("feedback.kind");
    }

    [Theory]
    [InlineData("/seimas/XVP-0412/kreipimasis?kodas=ABCD#reasons", "/seimas/XVP-0412/kreipimasis")]
    [InlineData("/", "/")]
    [InlineData("//evil.example/x", null)]
    [InlineData("https://lt2.localhost/x", null)]
    [InlineData("", null)]
    public void Only_the_pages_path_is_kept_never_its_query_or_another_host(string? path, string? kept) =>
        FeedbackRules.PagePath(path).ShouldBe(kept);

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.2903.86", "Edge 131")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", "Chrome 130")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; rv:132.0) Gecko/20100101 Firefox/132.0", "Firefox 132")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1", "Safari 17")]
    [InlineData("curl/8.4.0", null)]
    public void The_browser_is_its_name_and_major_version_only(string userAgent, string? shown) =>
        FeedbackRules.Browser(userAgent).ShouldBe(shown);

    [Fact]
    public void A_browser_sent_back_by_the_form_must_look_like_one() =>
        (FeedbackRules.BrowserAsSent("Edge 131"), FeedbackRules.BrowserAsSent("<script> 1")).ShouldBe(("Edge 131", (string?)null));

    [Fact]
    public void Items_older_than_the_setting_go_in_the_daily_clean_up() =>
        FeedbackRules.KeepFrom(new DateOnly(2026, 11, 13), 12).ShouldBe(new DateOnly(2025, 11, 13));
}
