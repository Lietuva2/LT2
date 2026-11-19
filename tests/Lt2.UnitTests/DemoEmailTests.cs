using System.Net;
using System.Text.RegularExpressions;
using Lt2.Notifications;
using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests;

/// <summary>
/// <c>--demo-emails</c>: one message of every outbox kind, from invented data, to <c>NAME@pavyzdys.test</c> in Mailpit; refused
/// where mail could go out. These tests also render every one through the real renderers and check what a look at each would
/// find first: an address printed bare, an HTML link the plain part lacks, a placeholder or resource key left in the text.
/// </summary>
public sealed class DemoEmailTests
{
    private const string Site = "https://lietuva2.test";

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static Dictionary<string, IEmailRenderer> Renderers()
    {
        var configuration = Configuration(("Site:Address", Site));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddNotificationsModule(configuration);
        services.AddNotificationsDispatcher(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IEmailRenderer>().ToDictionary(r => r.Kind, StringComparer.Ordinal);
    }

    private static IEnumerable<(DemoEmail Demo, EmailMessage Email)> Rendered()
    {
        var renderers = Renderers();
        foreach (var demo in DemoEmailSet.Build(Now))
        {
            renderers.ShouldContainKey(demo.Message.Kind);
            yield return (demo, renderers[demo.Message.Kind].Render(demo.Message.Payload));
        }
    }

    private static string Written
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir?.FullName ?? Path.GetTempPath(), "output", "email-samples", "demo");
        }
    }

    [Fact]
    public void Every_demonstration_is_written_for_a_look_at_it_without_a_stack()
    {
        Directory.CreateDirectory(Written);
        foreach (var (demo, email) in Rendered())
        {
            File.WriteAllText(Path.Combine(Written, demo.Name + ".html"), email.HtmlBody!);
            File.WriteAllText(Path.Combine(Written, demo.Name + ".txt"), email.Subject + "\n\n" + email.TextBody);
        }

        Directory.GetFiles(Written, "*.html").Length.ShouldBe(DemoEmailSet.Build(Now).Count);
    }

    [Fact]
    public void The_demonstration_covers_every_outbox_kind_in_the_stream_table_so_a_new_kind_cannot_be_added_without_one()
    {
        var covered = DemoEmailSet.CoveredKinds(DemoEmailSet.Build(Now));

        EmailStreams.Kinds.Except(covered).ShouldBeEmpty("a kind with no demonstration email");
        covered.Except(EmailStreams.Kinds).ShouldBeEmpty("a demonstration of a kind that does not exist");
    }

    [Fact]
    public void Every_demonstration_has_its_own_invented_address_under_the_test_domain()
    {
        var emails = DemoEmailSet.Build(Now);

        emails.Select(e => e.Address).Distinct(StringComparer.Ordinal).Count().ShouldBe(emails.Count);
        emails.ShouldAllBe(e => e.Address.EndsWith("@pavyzdys.test", StringComparison.Ordinal));
        emails.ShouldAllBe(e => e.Message.Payload.GetProperty("to").GetString() == e.Address);
    }

    [Fact]
    public void It_runs_only_where_demo_data_is_allowed_and_every_email_is_caught()
    {
        Should.Throw<InvalidOperationException>(() => DemoEmails.Require(Configuration())).Message.ShouldContain("Demo:Allowed");
        Should.Throw<InvalidOperationException>(() => DemoEmails.Require(Configuration(("Email:Caught", "true")))).Message.ShouldContain("Demo:Allowed");
        Should.Throw<InvalidOperationException>(() => DemoEmails.Require(Configuration(("Demo:Allowed", "true")))).Message.ShouldContain("Email:Caught");
        Should.Throw<InvalidOperationException>(() => DemoEmails.Require(Configuration(("Demo:Allowed", "true"), ("Email:Caught", "false")))).Message.ShouldContain("Email:Caught");
        DemoEmails.Require(Configuration(("Demo:Allowed", "true"), ("Email:Caught", "true")));
    }

    [Fact]
    public async Task A_refused_run_touches_nothing_not_even_the_database()
    {
        // The refusal comes before the connection is opened: a connection string that cannot work is never used.
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            DemoEmails.EnqueueAsync(Configuration(("Demo:Allowed", "true")), "Host=unused;Timeout=1", TimeProvider.System, CancellationToken.None));
        error.Message.ShouldContain("Email:Caught");
    }

    [Fact]
    public void Every_demonstration_renders_through_its_kinds_renderer_to_a_complete_email()
    {
        foreach (var (demo, email) in Rendered())
        {
            email.To.ShouldBe(demo.Address, demo.Name);
            email.Subject.ShouldNotBeNullOrWhiteSpace(demo.Name);
            email.TextBody.ShouldNotBeNullOrWhiteSpace(demo.Name);
            email.HtmlBody.ShouldNotBeNullOrWhiteSpace(demo.Name);
            Regex.IsMatch(email.Subject + email.TextBody, @"\{\d\}").ShouldBeFalse(demo.Name + ": a placeholder is left in the text");
            Regex.IsMatch(email.Subject + email.TextBody + email.HtmlBody, @"\b(Email|Digest|MpMail|Intro|Report|Follow|Org|Account|Moderation)\.[A-Z]\w+(\.\w+)+").ShouldBeFalse(demo.Name + ": a resource key is shown instead of its text");
            email.TextBody.ShouldNotContain("\n\n\n", Case.Sensitive, demo.Name + ": an empty paragraph");
            email.TextBody.ShouldNotContain("null", Case.Insensitive, demo.Name);
            email.HtmlBody!.ShouldNotContain("<p style=\"margin:0 0 16px\"></p>", Case.Sensitive, demo.Name + ": an empty paragraph");
            email.HtmlBody.ShouldNotContain("<br><br>", Case.Sensitive, demo.Name + ": an empty line");
        }
    }

    /// <summary>
    /// The pages a letter says to open and type a code on (design 6.8, owner 2026-11-17: "the way without a link"): the one address
    /// that is printed in full, on purpose, because the person types it.
    /// </summary>
    private static readonly string[] TypedPages = [AccountScreens.EmailRestore, "/seimo-narys/perimti", "/seimo-narys/biuras"];

    [Fact]
    public void No_demonstration_prints_a_long_address_and_the_plain_part_has_every_address_the_html_links_to()
    {
        var problems = new List<string>();
        foreach (var (demo, email) in Rendered())
        {
            var html = email.HtmlBody!;
            var printed = Regex.Replace(html, "<[^>]*>", " ");
            problems.AddRange(Regex.Matches(WebUtility.HtmlDecode(printed), @"https?://\S+")
                .Select(a => a.Value)
                .Where(a => a.Length > 40 && !TypedPages.Any(p => a == Site + p))
                .Select(a => $"{demo.Name}: a bare address is printed: {a}"));

            problems.AddRange(Regex.Matches(html, "href=\"([^\"]+)\"")
                .Select(link => WebUtility.HtmlDecode(link.Groups[1].Value))
                .Where(address => !email.TextBody.Contains(address, StringComparison.Ordinal))
                .Select(address => $"{demo.Name}: the HTML links to an address the plain part lacks: {address}"));
        }

        problems.ShouldBeEmpty();
    }
}
