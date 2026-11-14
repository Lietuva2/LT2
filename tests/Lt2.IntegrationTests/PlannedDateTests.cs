using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, 8.4: planned dates on the appeal ("Planuojama …"), flags from participants, a moderator's correction that
/// takes precedence over the agenda pages, and the appeal under its threshold whose bill reached the agenda in the queue.
/// </summary>
public sealed class PlannedDateTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_planned_date_is_shown_flagged_by_participants_and_corrected_by_a_moderator()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 500);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6));
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source_url, source, first_seen, last_seen)
            VALUES (gen_random_uuid(), 'XVP-0618', @d, 'adoption', 'https://e-seimas.lrs.lt/pavyzdys', 'agenda', now(), now())
            """,
            ("d", date));
        try
        {
            using var stranger = NewBrowser();
            (await stranger.AppealAsync(AppealSteps.ThirdBill)).Planned.ShouldBe(new PlannedDateResponse(date, "adoption", "https://e-seimas.lrs.lt/pavyzdys", false));
            using (var page = await stranger.Client.GetAsync("/seimas/XVP-0618"))
            {
                var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
                html.ShouldContain("Planuojama ");
                html.ShouldContain(">Priėmimas</span>");
                System.Text.RegularExpressions.Regex.IsMatch(html, @"(?i)\b(liko|po)\s+\d+\s*(d\.|dien|val)").ShouldBeFalse("never a countdown");
            }

            // Under its threshold with the bill on the agenda: a moderator looks at it (design 8.3).
            var queued = (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.Single(a => a.Id == appeal.Id);
            queued.UnderThresholdOnAgenda.ShouldBeTrue();
            queued.Participants.ShouldBe(1, "the note says how many take part");
            queued.Threshold.ShouldBe(500);
            queued.Planned!.Date.ShouldBe(date);

            var (outsider, _) = await Factory.MemberAsync();
            using var _o = outsider;
            var flag = new FlagRequest { Kind = "sooner", Link = "https://www.lrs.lt/pavyzdys", Note = "Įtraukta į antradienio darbotvarkę." };
            using (var refused = await outsider.PostAsync(AppealRoutes.FlagsOf(AppealSteps.ThirdBill), flag))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "flag.participants-only");
            }

            using (var badLink = await initiator.PostAsync(AppealRoutes.FlagsOf(AppealSteps.ThirdBill), flag with { Link = "javascript:alert(1)" }))
            {
                await badLink.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
            }

            (await initiator.PostAsync(AppealRoutes.FlagsOf(AppealSteps.ThirdBill), flag)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var flags = (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.Single(a => a.Id == appeal.Id).DateFlags!;
            flags.Single().Kind.ShouldBe("sooner");
            flags.Single().Link.ShouldBe("https://www.lrs.lt/pavyzdys");

            var corrected = date.AddDays(-3);
            using (var set = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealPlanned, appeal.Id), new ModeratorPlannedRequest
            {
                Date = corrected,
                Stage = "consideration",
                Statement = "Darbotvarkė pakeista.",
            }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
            }

            (await stranger.AppealAsync(AppealSteps.ThirdBill)).Planned.ShouldBe(new PlannedDateResponse(corrected, "consideration", null, true));
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.flag WHERE appeal_id = @a AND status = 'open'", ("a", appeal.Id))).ShouldBe(0);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'moderation.appeal.planned' AND target = @t AND actor_role = 'moderator'", ("t", "appeal:" + appeal.Id))).ShouldBe(1);

            // A new agenda row for the bill does not override the moderator's date.
            await Database.ExecuteAsync(
                "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), 'XVP-0618', @d, 'adoption', 'agenda', now(), now())",
                ("d", date.AddDays(1)));
            (await stranger.AppealAsync(AppealSteps.ThirdBill)).Planned!.Date.ShouldBe(corrected);
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = 'XVP-0618'");
        }
    }
}
