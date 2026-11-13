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
            AppealRoutes.HistoryOf(AppealSteps.ThirdBill), Lt2.Api.Contracts.Digests.DigestRoutes.Digests, Lt2.Api.Contracts.Digests.DigestRoutes.Rules,
            ApiRoutes.Me.PartySupport, ApiRoutes.Me.Export, "/kreipimaisi", "/seimas/XVP-0618/kreipimasis", "/suvestines", "/moderavimas",
            ApiRoutes.Me.Invitation, ApiRoutes.Admin.Invitations, "/sveiki", "/administravimas/kvietimai",
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

    /// <summary>
    /// Design 8.2 (decided 2026-11-13): an argument may be written for either side, so an argument must not tell its
    /// author's side. Four arguments: in the "for" column one by a person asking against (the other side) and one by a
    /// person asking for (their own); in the "against" column the initiator's (their own) and one by a person with no
    /// side. Every public answer, and the staff screens, must show each pair alike apart from the text, the name, the
    /// id, the time and the count.
    /// </summary>
    [Fact]
    public async Task No_answer_tells_whether_an_argument_is_on_its_authors_side()
    {
        var (moderator, administrator) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync("Iniciatorė Prieš");
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var (other, _) = await Factory.MemberAsync("Ona Kitapusė");
        using var _o = other;
        (await other.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await other.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Kitos pusės argumentas apie biudžetą.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var (own, _) = await Factory.MemberAsync("Petras Savapusis");
        using var _w = own;
        (await own.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await own.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Savos pusės argumentas apie gynybą.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var (none, _) = await Factory.MemberAsync("Jurga Bepusė", consent: false);
        using var _n = none;
        (await none.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Argumentas be pozicijos apie įmones.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var stranger = NewBrowser();
        var reasons = (await stranger.AppealAsync(AppealSteps.ThirdBill)).Reasons;
        reasons.Count.ShouldBe(4);

        // Design 8.3: a writing restriction is not public either. The author of the other-side argument is restricted;
        // every comparison below must still find the pairs alike.
        (await moderator.Browser.PostAsync(
                AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasons.Single(r => r.AuthorName == "Ona Kitapusė").Id),
                new RestrictionRequest { Statement = "Bandomasis apribojimas." }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var pairs = new[]
        {
            (Side: "for", Ids: reasons.Where(r => r.Side == "for").Select(r => r.Id).ToList(), Why: "other side vs own side"),
            (Side: "against", Ids: reasons.Where(r => r.Side == "against").Select(r => r.Id).ToList(), Why: "own side (initiator) vs no side"),
        };
        var varying = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "text", "authorName", "createdAt", "endorsements" };
        var texts = reasons.Select(r => r.Text).Concat(reasons.Select(r => r.AuthorName!)).ToList();

        // A cached public answer is the same for an author and a stranger: nothing personal rides in it.
        using (var asAuthor = await other.Client.GetAsync(AppealRoutes.AppealOf(AppealSteps.ThirdBill)))
        using (var asStranger = await stranger.Client.GetAsync(AppealRoutes.AppealOf(AppealSteps.ThirdBill)))
        {
            (await asAuthor.Content.ReadAsStringAsync()).ShouldBe(await asStranger.Content.ReadAsStringAsync());
            asAuthor.Headers.ETag.ShouldBe(asStranger.Headers.ETag);
        }

        using (var mine = await other.Client.GetAsync(AppealRoutes.MineOf(AppealSteps.ThirdBill)))
        {
            mine.Headers.CacheControl?.NoStore.ShouldBe(true, "the author's own side comes only from the never-cached answer");
        }

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var viewers = new (string Who, TestBrowser Browser)[]
        {
            ("stranger", stranger), ("member", member), ("moderator", moderator.Browser), ("administrator", administrator.Browser),
        };
        var jsonReads = new[]
        {
            AppealRoutes.Appeals, AppealRoutes.AppealOf(AppealSteps.ThirdBill), AppealRoutes.HistoryOf(AppealSteps.ThirdBill),
            Lt2.Api.Contracts.Digests.DigestRoutes.Digests, AppealRoutes.ModerationQueue,
        };
        var pages = new[] { "/seimas/XVP-0618/kreipimasis", "/kreipimaisi", "/moderavimas" };

        foreach (var (who, browser) in viewers)
        {
            foreach (var uri in jsonReads)
            {
                using var response = await browser.Client.GetAsync(uri);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                // An argument can appear in several kinds of object (a queue item, a merge candidate): compare like with like.
                var found = new Dictionary<(Guid Id, string Kind), string>();
                Collect(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement, reasons.Select(r => r.Id).ToHashSet(), varying, found);
                foreach (var (side, ids, why) in pairs)
                {
                    foreach (var kind in found.Keys.Select(k => k.Kind).Distinct())
                    {
                        var shapes = ids.Where(id => found.ContainsKey((id, kind))).Select(id => found[(id, kind)]).Distinct().ToList();
                        shapes.Count.ShouldBeLessThanOrEqualTo(1, $"{who} {uri}, {side}: {why} differ: {string.Join(" | ", shapes)}");
                    }
                }
            }

            foreach (var uri in pages)
            {
                using var response = await browser.Client.GetAsync(uri);
                var html = await response.Content.ReadAsStringAsync();
                foreach (var (side, ids, why) in pairs)
                {
                    var blocks = ids.Select(id => Block(html, id, texts)).OfType<string>().Distinct().ToList();
                    blocks.Count.ShouldBeLessThanOrEqualTo(1, $"{who} {uri}, {side}: {why} differ:\n{string.Join("\n---\n", blocks)}");
                }
            }
        }

        // The page really shows all four, so the comparison above was made.
        var page = await stranger.Client.GetStringAsync("/seimas/XVP-0618/kreipimasis");
        reasons.ShouldAllBe(r => Block(page, r.Id, texts) != null);
    }

    /// <summary>
    /// Design 8.1 (decided 2026-11-13): opening asks for no side, so nothing ties the initiator to one. The same appeal is
    /// opened three times by an initiator with no side, asking for, and asking against, each time with other members
    /// making the totals equal (one for, one against). Every public answer and page must then be the same, once ids,
    /// times and the bill's own answer counts are blanked.
    /// </summary>
    [Fact]
    public async Task No_answer_tells_the_initiators_side_or_whether_they_have_one()
    {
        var (moderator, _) = await StaffAsync();
        var (forMember, _) = await Factory.MemberAsync("Narys Už");
        using var _f = forMember;
        var (againstMember, _) = await Factory.MemberAsync("Narė Prieš");
        using var _a = againstMember;
        using var stranger = NewBrowser();
        var shapes = new Dictionary<string, List<string>>();

        foreach (var initiatorSide in new string?[] { null, "for", "against" })
        {
            var (initiator, _) = await Factory.MemberAsync("Iniciatorė Bet Kuri");
            using var _i = initiator;
            await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, side: null);
            if (initiatorSide is not null)
            {
                (await initiator.TakeSideAsync(AppealSteps.ThirdBill, initiatorSide)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            if (initiatorSide != "for")
            {
                (await forMember.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            if (initiatorSide != "against")
            {
                (await againstMember.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            (await stranger.AppealAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(1, 0, 1, 0));
            var answers = new List<string>();
            foreach (var uri in new[] { AppealRoutes.AppealOf(AppealSteps.ThirdBill), "/seimas/XVP-0618/kreipimasis", AppealRoutes.Appeals, "/kreipimaisi" })
            {
                foreach (var viewer in new[] { stranger, moderator.Browser })
                {
                    var body = await viewer.Client.GetStringAsync(uri);
                    answers.Add(Blank(body));
                }
            }

            shapes[initiatorSide ?? "none"] = answers;
        }

        for (var i = 0; i < shapes["none"].Count; i++)
        {
            shapes["for"][i].ShouldBe(shapes["none"][i], $"answer {i}: an initiator asking for vs one with no side");
            shapes["against"][i].ShouldBe(shapes["none"][i], $"answer {i}: an initiator asking against vs one with no side");
        }

        // Nothing stored ties an initiator to a side: no column next to the initiator, and no position made by opening.
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'appeals' AND table_name = 'appeal' AND column_name LIKE '%side%'"))
            .ShouldBe(0);
    }

    /// <summary>An answer with ids, times, Blazor's comments and persisted state blanked; what is left must not depend on anyone's side.</summary>
    private static string Blank(string body)
    {
        var text = Regex.Replace(body, "<!--.*?-->", "", RegexOptions.Singleline);
        text = Regex.Replace(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "ID");
        text = Regex.Replace(text, @"\d{4}-\d{2}-\d{2}T[0-9:.+\-Z]+", "TIME");
        return Regex.Replace(text, "name=\"__RequestVerificationToken\"[^>]*", "TOKEN");
    }

    /// <summary>Every JSON object whose id is one of <paramref name="ids"/>, with the fields that may differ removed.</summary>
    private static void Collect(JsonElement element, HashSet<Guid> ids, HashSet<string> varying, Dictionary<(Guid, string), string> found)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var guid) && ids.Contains(guid))
            {
                var kind = string.Join(",", element.EnumerateObject().Select(p => p.Name));
                found[(guid, kind)] = Shape(element, varying);
            }

            foreach (var property in element.EnumerateObject())
            {
                Collect(property.Value, ids, varying, found);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, ids, varying, found);
            }
        }
    }

    /// <summary>A JSON value with the fields that may differ removed at every depth.</summary>
    private static string Shape(JsonElement element, HashSet<string> varying) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject().Where(p => !varying.Contains(p.Name)).Select(p => p.Name + "=" + Shape(p.Value, varying))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(e => Shape(e, varying))) + "]",
        _ => element.GetRawText(),
    };

    /// <summary>The markup of one argument on a page (its element and everything inside), with text, names, id and counts blanked.</summary>
    private static string? Block(string html, Guid id, IEnumerable<string> texts)
    {
        var start = html.IndexOf($"id=\"reason-{id}\"", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start = html.LastIndexOf("<div", start, StringComparison.Ordinal);
        var depth = 0;
        var at = start;
        do
        {
            var open = html.IndexOf("<div", at, StringComparison.Ordinal);
            var close = html.IndexOf("</div>", at, StringComparison.Ordinal);
            if (open >= 0 && open < close)
            {
                depth++;
                at = open + 4;
            }
            else
            {
                depth--;
                at = close + 6;
            }
        }
        while (depth > 0);

        var block = WebUtility.HtmlDecode(html[start..at]).Replace(id.ToString(), "ID", StringComparison.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            block = block.Replace(text, "TEXT", StringComparison.Ordinal);
        }

        return Regex.Replace(Regex.Replace(block, @"\d[\d\s ]*", "N"), "<!--.*?-->", "", RegexOptions.Singleline);
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
            "/api/v1/bills/{number}/appeal/reasons/similar", "/api/v1/reasons/{id}/endorsement", "/api/v1/reasons/{id}", "/api/v1/reports", "/api/v1/me/writing-restriction", "/api/v1/moderation/reasons/{id}/author", "/api/v1/moderation/appeals/{id}/initiator", "/api/v1/moderation/reasons/{id}/restrict-author", "/api/v1/moderation/appeals/{id}/restrict-initiator", "/api/v1/moderation/restrictions", "/api/v1/moderation/restrictions/{id}/lift", "/api/v1/me/invitation", "/api/v1/me/invitation/replace", "/api/v1/admin/invitations", "/api/v1/admin/invitations/{id}/switch-off", "/api/v1/admin/invitations/{id}/switch-on", "/api/v1/moderation/queue",
            "/api/v1/moderation/appeals/{id}/publish", "/api/v1/moderation/appeals/{id}/return", "/api/v1/moderation/appeals/{id}/hide",
            "/api/v1/moderation/appeals/{id}/threshold", "/api/v1/moderation/reasons/{id}/publish", "/api/v1/moderation/reasons/{id}/hide",
            "/api/v1/moderation/reasons/{id}/merge", "/api/v1/me/consents/positions", "/api/v1/bills/{number}/appeal/flags",
            "/api/v1/bills/{number}/appeal/history", "/api/v1/moderation/appeals/{id}/planned", "/api/v1/moderation/appeals/{id}/hold",
            "/api/v1/moderation/appeals/{id}/release", "/api/v1/moderation/appeals/{id}/urgent", "/api/v1/digests", "/api/v1/digests/{date}",
            "/api/v1/digests/rules", "/api/v1/moderation/digests/{id}/mps", "/api/v1/moderation/digests/{id}/mps/{slug}", "/api/v1/me/party-support",
            "/api/v1/me/export", "/api/v1/me",
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
        var opened = await (await initiator.OpenAppealAsync(AppealSteps.ThirdBill)).ReadAsync<AppealResponse>();
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
    internal sealed class LoggingFactory(DatabaseFixture database, CollectingSink sink) : Lt2Factory(database)
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

    internal sealed class CollectingSink : ILogEventSink
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
