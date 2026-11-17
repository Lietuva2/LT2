using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Mps;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.19.3 (owner, 2026-11-17): an MP's office members get copies of the MP's summaries at their own address, by their own
/// choice, independent of the MP's; each stops their own by one click; ending the link ends the copies and deletes the choice;
/// nothing public says an office exists.
/// </summary>
public sealed class OfficeCopyTests(DatabaseFixture database) : ApiTest(database)
{
    private static int Copies(IEnumerable<OutboxMpMail> mails) => mails.Count(m => m.Payload.GetProperty("recipient").GetString() == "office");

    [Fact]
    public async Task An_office_member_gets_copies_by_their_own_choice_stops_them_by_one_click_and_loses_them_with_the_link()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin) = await StaffAsync();
        await admin.SetFreshAsync("Digest:ThresholdMinimum", 1);
        var weeks = new[] { ReasonsToComeSteps.MondayInWeeks(50), ReasonsToComeSteps.MondayInWeeks(51), ReasonsToComeSteps.MondayInWeeks(52) };
        using var mp = await Factory.MpAccountAsync(MpSteps.Uosyte);
        var (office, _, officeEmail) = await Factory.OfficeMemberAsync(mp, "Biuro Narė");
        using var _o = office;
        try
        {
            var (member, _) = await Factory.MemberAsync();
            using (member)
            {
                await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            }

            await admin.IntroduceAsync();
            await admin.SetFreshAsync("Digest:Sending", "automatic");

            // The office member's own choice: the default, on "Dalyvavimas", in any session.
            (await office.GetJsonAsync<MpOfficeCopiesResponse>(MpMailRoutes.OfficeCopies)).Frequency.ShouldBe("default");
            (await mp.Browser.Client.GetAsync(MpMailRoutes.OfficeCopies)).StatusCode.ShouldBe(HttpStatusCode.NotFound, "the MP's own account has no copies: it gets the summary");

            // Week 1: the MP stopped their own summaries; the office still gets its copy.
            var preferences = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            using var page = NewBrowser();
            (await page.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(preferences), new MpMailFrequencyRequest { Frequency = "stopped" }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE kind = ANY(@k)", ("k", MpSendingSteps.MpMailKinds));
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[0].AddDays(3));
            var w1 = (await Factory.RunDueAsync(MpSendingSteps.At(weeks[0], 7, 5))).Of("weekly");
            w1.Status.ShouldBe("sent");
            (await Database.DeliveryAsync(w1.Id, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("skipped-preference");
            var copy = (await Database.MpMailAsync(officeEmail, "email.mp-digest")).ShouldHaveSingleItem();
            copy.Payload.GetProperty("recipient").GetString().ShouldBe("office");
            copy.Payload.GetProperty("mpName").GetString().ShouldBe("Laima Uosytė");
            copy.Payload.GetProperty("preferencesPath").ValueKind.ShouldBe(JsonValueKind.Null, "a copy never carries the MP's link");
            var oneClick = copy.Payload.GetProperty("unsubscribePath").GetString()!;
            oneClick.ShouldEndWith("/stop");
            (await Database.MpMailAsync(MpSteps.Uosyte.Address, "email.mp-digest")).ShouldBeEmpty();
            w1.SentCount.ShouldBe(w1.MpCount, "office copies are not counted as MPs");

            // Nothing public or on the screen tells of the office.
            using var stranger = NewBrowser();
            var date = weeks[0].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var publicCopy = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + date)) + await stranger.Client.GetStringAsync(DigestRoutes.DigestOn(weeks[0]));
            publicCopy.ShouldNotContain(officeEmail, Case.Insensitive);
            publicCopy.ShouldNotContain("biur", Case.Insensitive);
            JsonSerializer.Serialize(await moderator.Browser.GetJsonAsync<DigestModerationResponse>(DigestRoutes.Moderation)).ShouldNotContain(officeEmail, Case.Insensitive);

            // The one-click stop of the copy stops this member's copies only; the MP takes their summaries back.
            using (var mailProgram = NewBrowser())
            {
                using var stopped = await mailProgram.Client.PostAsync(oneClick, new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }));
                stopped.StatusCode.ShouldBe(HttpStatusCode.NoContent, await stopped.Content.ReadAsStringAsync());
            }

            (await office.GetJsonAsync<MpOfficeCopiesResponse>(MpMailRoutes.OfficeCopies)).Frequency.ShouldBe("stopped");
            (await page.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(preferences), new MpMailFrequencyRequest { Frequency = "default" }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE kind = ANY(@k)", ("k", MpSendingSteps.MpMailKinds));
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[1].AddDays(3));
            var w2 = (await Factory.RunDueAsync(MpSendingSteps.At(weeks[1], 7, 5))).Of("weekly");
            (await Database.DeliveryAsync(w2.Id, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("queued");
            (await Database.DeliveryAsync(w2.Id, MpSteps.Uosyte.SeimasPersonId, "office")).ShouldBe("skipped-preference");
            Copies(await Database.MpMailAsync(officeEmail)).ShouldBe(0);

            // Its own frequency: weekly only; the before-vote summary that week goes to the MP and not to the office.
            using (var set = await office.SendAsync(HttpMethod.Put, MpMailRoutes.OfficeCopies, new MpMailFrequencyRequest { Frequency = "weekly" }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
            }

            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[1].AddDays(4));
            var friday = (await Factory.RunDueAsync(MpSendingSteps.At(weeks[1].AddDays(3), 12, 5))).Of("before-vote");
            (await Database.DeliveryAsync(friday.Id, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("queued");
            (await Database.DeliveryAsync(friday.Id, MpSteps.Uosyte.SeimasPersonId, "office")).ShouldBe("skipped-preference", "weekly copies only");

            // Leaving the office ends the copies and deletes the choice.
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.office_copy WHERE seimas_person_id = @p", ("p", MpSteps.Uosyte.SeimasPersonId))).ShouldBe(1);
            using (var left = await office.SendAsync(HttpMethod.Delete, MpRoutes.Mine))
            {
                left.StatusCode.ShouldBe(HttpStatusCode.NoContent, await left.Content.ReadAsStringAsync());
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.office_copy WHERE seimas_person_id = @p", ("p", MpSteps.Uosyte.SeimasPersonId))).ShouldBe(0);
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[2].AddDays(3));
            var w3 = (await Factory.RunDueAsync(MpSendingSteps.At(weeks[2], 7, 5))).Of("weekly");
            (await Database.DeliveryAsync(w3.Id, MpSteps.Uosyte.SeimasPersonId, "office")).ShouldBeNull("no longer in the office: no copy");
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await admin.ResetFreshAsync("Digest:Sending");
            await admin.ResetFreshAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = 'XVP-0618'");
            // Deleting the MP's account tells the official address the profile was given up (design 8.5.2, the account's last
            // email): left in the outbox, it would be one more email to an address the MP mail tests share.
            await Database.ExecuteAsync(
                "DELETE FROM notify.outbox WHERE lower(payload->>'to') = ANY(@a)",
                ("a", new[] { officeEmail.ToLowerInvariant(), MpSteps.Uosyte.Address.ToLowerInvariant() }));
            await Database.ResetMpMailAsync();
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}
