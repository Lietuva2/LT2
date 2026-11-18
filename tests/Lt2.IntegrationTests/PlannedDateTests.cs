using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, 8.4: planned dates on the record ("Planuojama …"), flags from participants, a moderator's correction that
/// takes precedence over the agenda pages, and the record under its threshold whose bill reached the agenda in the queue.
/// </summary>
public sealed class PlannedDateTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_planned_date_is_shown_flagged_by_participants_and_corrected_by_a_moderator()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 500);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6));
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source_url, source, first_seen, last_seen)
            VALUES (gen_random_uuid(), 'XVP-0618', @d, 'adoption', 'https://e-seimas.lrs.lt/pavyzdys', 'agenda', now(), now())
            """,
            ("d", date));

        // The agenda page the date was read from (the watcher keeps one snapshot per read); removed again below.
        var snapshot = Guid.NewGuid();
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.agenda_snapshot (id, url, sha256, body, synthetic, fetched_at, failure, rows)
            VALUES (@id, 'https://www.lrs.lt/pavyzdys/darbotvarke', '', '\x'::bytea, true, now() + interval '1 day', NULL, 1)
            """,
            ("id", snapshot));
        try
        {
            using var stranger = NewBrowser();
            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Planned.ShouldBe(new PlannedDateResponse(date, "adoption", "https://e-seimas.lrs.lt/pavyzdys", false));
            using (var page = await stranger.Client.GetAsync("/seimas/XVP-0618"))
            {
                var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
                html.ShouldContain("Planuojama ");
                html.ShouldContain(">Priėmimas</span>");
                System.Text.RegularExpressions.Regex.IsMatch(html, @"(?i)\b(liko|po)\s+\d+\s*(d\.|dien|val)").ShouldBeFalse("never a countdown");

                // Design 8.4, 8.15 (owner, 2026-11-16): where the date comes from, near it; the agenda page it is published
                // on, never the bill's e-seimas document; the planned stage in "Stadijos Seime", the side column's first sheet.
                var hint = AppealPageTests.HintOf(html, "planned-from");
                hint.Trigger.ShouldBe("Iš kur ši data?");
                hint.Answer.ShouldBe("Tai Seimo posėdžio data iš darbotvarkės projekto, kurį lrs.lt skelbia maždaug 10–12 dienų į priekį. Tokios datos dažnai keičiasi.");
                hint.More.ShouldBe("/metodika#planuojamos-datos");
                html.ShouldContain("<a href=\"https://www.lrs.lt/pavyzdys/darbotvarke\" rel=\"external\" id=\"planned-agenda\">Darbotvarkė lrs.lt</a>");
                html.ShouldNotContain("href=\"https://e-seimas.lrs.lt/pavyzdys\"", Case.Sensitive, "the row's document link shows no date");
                html.IndexOf("<aside", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"stages\"", StringComparison.Ordinal));
                html.IndexOf("id=\"stages\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"digests\"", StringComparison.Ordinal));
                System.Text.RegularExpressions.Regex.IsMatch(html, "id=\"stage-adoption\">\\s*<span class=\"label\">Priėmimas</span>\\s*<span class=\"small num\" id=\"stage-planned\">Planuojama ")
                    .ShouldBeTrue("the planned stage shows the date");

                // Owner, 2026-11-18 (option A): the date's source left the header for "Stadijos Seime", right under the
                // stages and their date; the header keeps the stage and "Planuojama …" tags. The header stands one step
                // above the page's first part (.stack--snug).
                var header = html[html.IndexOf("id=\"bill-planned\"", StringComparison.Ordinal)..];
                header = header[..header.IndexOf("</p>", StringComparison.Ordinal)];
                header.ShouldContain("id=\"planned\"");
                header.ShouldNotContain("planned-from");
                header.ShouldNotContain("planned-agenda");
                var stages = html[html.IndexOf("id=\"stages\"", StringComparison.Ordinal)..html.IndexOf("id=\"digests\"", StringComparison.Ordinal)];
                stages.IndexOf("</ol>", StringComparison.Ordinal).ShouldBeLessThan(stages.IndexOf("id=\"planned-source\"", StringComparison.Ordinal));
                stages.IndexOf("id=\"planned-source\"", StringComparison.Ordinal).ShouldBeLessThan(stages.IndexOf("id=\"planned-from\"", StringComparison.Ordinal));
                stages.ShouldContain("id=\"planned-agenda\"");

                // Owner, 2026-11-18: the bill's e-seimas address once, in "Apie ką projektas"; the side column's stages repeat it not.
                var eSeimas = (await stranger.GetJsonAsync<Lt2.Api.Contracts.Parliament.BillResponse>(ParliamentRoutes.BillByNumber(AppealSteps.ThirdBill))).ESeimasUrl.ShouldNotBeNull();
                html.Split($"href=\"{eSeimas}\"").Length.ShouldBe(2, "the e-seimas address once on the page");
                html[html.IndexOf("id=\"about\"", StringComparison.Ordinal)..html.IndexOf("id=\"sides\"", StringComparison.Ordinal)].ShouldContain($"href=\"{eSeimas}\"");
                stages.ShouldNotContain("Projektas e-seime");
                html.IndexOf("class=\"stack stack--snug\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"bill-planned\"", StringComparison.Ordinal));
            }

            // Under its threshold with the bill on the agenda: a moderator looks at it (design 8.3).
            var queued = (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.Single(a => a.Id == record.Id);
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

            // Owner, 2026-11-17: "Tikroji data (jei žinote)" is a date or nothing; anything else is refused and stores nothing.
            const string NotADate = """{"kind":"sooner","link":"https://www.lrs.lt/pavyzdys","date":"2026-13-45"}""";
            using (var badDate = await initiator.PostAsync(AppealRoutes.FlagsOf(AppealSteps.ThirdBill), NotADate))
            {
                badDate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.flag WHERE record_id = @a", ("a", record.Id))).ShouldBe(0);

            var rightDate = date.AddDays(-2);
            (await initiator.PostAsync(AppealRoutes.FlagsOf(AppealSteps.ThirdBill), flag with { Date = rightDate })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var flags = (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.Single(a => a.Id == record.Id).DateFlags!;
            flags.Single().Kind.ShouldBe("sooner");
            flags.Single().Link.ShouldBe("https://www.lrs.lt/pavyzdys");
            flags.Single().Date.ShouldBe(rightDate, "the date the participant says is right reaches the moderators with the flag");

            var corrected = date.AddDays(-3);
            using (var set = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordPlanned, record.Id), new ModeratorPlannedRequest
            {
                Date = corrected,
                Stage = "consideration",
                Statement = "Darbotvarkė pakeista.",
            }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
            }

            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Planned.ShouldBe(new PlannedDateResponse(corrected, "consideration", null, true));
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.flag WHERE record_id = @a AND status = 'open'", ("a", record.Id))).ShouldBe(0);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'moderation.record.planned' AND target = @t AND actor_role = 'moderator'", ("t", "record:" + record.Id))).ShouldBe(1);

            // A new agenda row for the bill does not override the moderator's date.
            await Database.ExecuteAsync(
                "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), 'XVP-0618', @d, 'adoption', 'agenda', now(), now())",
                ("d", date.AddDays(1)));
            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Planned!.Date.ShouldBe(corrected);
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = 'XVP-0618'");
            await Database.ExecuteAsync("DELETE FROM parliament.agenda_snapshot WHERE id = @id", ("id", snapshot));
        }
    }
}
