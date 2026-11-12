using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Lt2.IntegrationTests;

/// <summary>
/// The privacy rules of <c>AGENTS.md</c> for appeals, each with a test that tries to break it. A person's side on a bill is
/// special-category data (design 6.1).
/// </summary>
public sealed class AppealPrivacyTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static StaffMember? _moderator;
    private static StaffMember? _administrator;

    private async Task<(StaffMember Moderator, StaffMember Administrator)> StaffAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _moderator ??= await StaffMembers.ActiveAsync(Factory, Database, "moderator");
            _administrator ??= await StaffMembers.ActiveAsync(Factory, Database, "administrator");
            return (_moderator, _administrator);
        }
        finally
        {
            Gate.Release();
        }
    }

    [Fact]
    public async Task No_answer_to_anyone_reveals_another_persons_side()
    {
        var (moderator, administrator) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var (secret, secretEmail) = await Factory.MemberAsync("Slapta Pozicija");
        using var _s = secret;
        (await secret.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        var reasonId = (await reporter.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var secretId = (await Database.UserIdAsync(secretEmail)).ToString();

        var (otherMember, _) = await Factory.MemberAsync();
        using var _o = otherMember;
        using var stranger = NewBrowser();
        var viewers = new (string Who, TestBrowser Browser)[]
        {
            ("stranger", stranger), ("member", otherMember), ("moderator", moderator.Browser), ("administrator", administrator.Browser),
        };
        var reads = new[]
        {
            AppealRoutes.Appeals, AppealRoutes.AppealOf(AppealSteps.ThirdBill), AppealRoutes.MineOf(AppealSteps.ThirdBill), AppealRoutes.Rules,
            AppealRoutes.ModerationQueue, ApiRoutes.Admin.Settings, ApiRoutes.Me.Root, ApiRoutes.Me.Sessions,
        };

        foreach (var (who, browser) in viewers)
        {
            foreach (var uri in reads)
            {
                using var response = await browser.Client.GetAsync(uri);
                var body = await response.Content.ReadAsStringAsync();
                body.ShouldNotContain(secretId, Case.Insensitive, $"{who} {uri}");
                body.ShouldNotContain("Slapta Pozicija", Case.Insensitive, $"{who} {uri}: the person wrote nothing public");
                body.ShouldNotContain(secretEmail, Case.Insensitive, $"{who} {uri}");
                if (uri == AppealRoutes.MineOf(AppealSteps.ThirdBill) && response.IsSuccessStatusCode)
                {
                    JsonDocument.Parse(body).RootElement.GetProperty("side").ValueKind.ShouldBe(JsonValueKind.Null, $"{who} sees only their own side");
                }
            }
        }

        // The counts are the only trace: a total, not a person.
        (await stranger.AppealAsync(AppealSteps.ThirdBill)).Counts.For.ShouldBe(1);
    }

    [Fact]
    public async Task No_operation_takes_or_returns_a_person_identifier_next_to_a_side()
    {
        using var browser = NewBrowser();
        var document = await browser.Client.GetStringAsync("/openapi/v1.json");

        Regex.Matches(document, "\"[a-zA-Z]*[Uu]serId\"").ShouldBeEmpty("no schema carries an account id");
        document.ShouldNotContain("{userId}");
        document.ShouldNotContain("{user}");
        foreach (var path in new[]
        {
            "/api/v1/appeals", "/api/v1/appeals/rules", "/api/v1/bills/{number}/appeal", "/api/v1/bills/{number}/appeal/mine",
            "/api/v1/bills/{number}/appeal/threshold", "/api/v1/bills/{number}/appeal/position", "/api/v1/bills/{number}/appeal/reasons",
            "/api/v1/bills/{number}/appeal/reasons/similar", "/api/v1/reasons/{id}/endorsement", "/api/v1/reports", "/api/v1/moderation/queue",
            "/api/v1/moderation/appeals/{id}/publish", "/api/v1/moderation/appeals/{id}/return", "/api/v1/moderation/appeals/{id}/hide",
            "/api/v1/moderation/appeals/{id}/threshold", "/api/v1/moderation/reasons/{id}/publish", "/api/v1/moderation/reasons/{id}/hide",
            "/api/v1/moderation/reasons/{id}/merge", "/api/v1/me/consents/positions",
        })
        {
            document.ShouldContain($"\"{path}\"", customMessage: path);
        }
    }

    [Fact]
    public async Task Changing_a_side_overwrites_it_and_leaves_no_trace_in_revisions_audit_or_outbox()
    {
        var (moderator, _) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var userId = await Database.UserIdAsync(email);
        var auditBefore = await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event");
        var outboxBefore = await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox");
        var revisionsBefore = await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.revision");

        foreach (var side in new[] { "for", "against", "for", "against" })
        {
            (await member.TakeSideAsync(AppealSteps.ThirdBill, side)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE user_id = @u", ("u", userId))).ShouldBe(1);
        (await Database.ScalarAsync<string>("SELECT side FROM appeals.position WHERE user_id = @u", ("u", userId))).ShouldBe("against");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event")).ShouldBe(auditBefore);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox")).ShouldBe(outboxBefore);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.revision")).ShouldBe(revisionsBefore);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.daily_stat WHERE appeal_id = @a", ("a", appeal.Id))).ShouldBe(0);

        // Nothing else in the database holds this account next to a side: no table besides position and endorsement
        // has both a side and an account column.
        (await Database.ScalarAsync<long>(
            """
            SELECT count(*) FROM information_schema.columns s
            JOIN information_schema.columns u ON u.table_schema = s.table_schema AND u.table_name = s.table_name
            WHERE s.column_name = 'side' AND u.column_name = 'user_id'
              AND s.table_schema || '.' || s.table_name <> 'appeals.position'
            """)).ShouldBe(0);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.revision WHERE changed_by_user_id = @u OR old_value IN ('for','against') OR new_value IN ('for','against')",
            ("u", userId))).ShouldBe(0);
    }

    [Fact]
    public async Task Log_lines_carry_no_email_address_and_no_side()
    {
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        using var setup = logged.NewBrowser();

        var (moderator, _) = await StaffAsync();
        var (initiator, initiatorEmail) = await logged.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var opened = await (await initiator.OpenAppealAsync(AppealSteps.ThirdBill, "against")).ReadAsync<AppealResponse>();
        (await moderator.Browser.PublishAsync(opened.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (member, memberEmail) = await logged.MemberAsync("Giedrė L.");
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Kitas argumentas prieš šį projektą.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var reasonId = (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons[0].Id;
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("against");
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var lines = sink.Lines;
        lines.Count.ShouldBeGreaterThan(20, "the requests were logged");
        foreach (var line in lines)
        {
            line.ShouldNotContain(initiatorEmail, Case.Insensitive);
            line.ShouldNotContain(memberEmail, Case.Insensitive);
            line.ShouldNotContain("@pavyzdys.test", Case.Insensitive);
            Regex.IsMatch(line, "(?i)\"(for|against)\"|side\\W{0,3}(for|against)|=\\s*'?(for|against)\\b").ShouldBeFalse(line);
        }
    }

    /// <summary>The web application with every log line, from Debug up, collected (EF Core's SQL included).</summary>
    private sealed class LoggingFactory(DatabaseFixture database, CollectingSink sink) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Serilog:MinimumLevel:Default", "Debug");
            builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft", "Debug");
            builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore", "Debug");
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink));
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<string> _lines = [];

        public List<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            var properties = string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
            lock (_lines)
            {
                _lines.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + properties + " " + logEvent.Exception);
            }
        }
    }
}
