using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.20.1, "Seimo narių atsakymai": one email per bill per day, the morning after, to the members who followed the bill
/// before the answers became public; not to those who switched it off or followed later; nothing about their own MP to the MP's
/// account and office; hidden and withdrawn answers left out and an empty day sends nothing; a changed explanation with the same
/// stance is no event. Each test makes its own stand-in MPs' accounts and removes them, their responses and the bill's events.
/// The job is run at 01:00 of the Vilnius day the test started on, with <c>Follows:ResponsesAt</c> at 00:00 and the responses moved
/// to noon of the day before, so the test depends neither on the hour it runs at, nor on a clock change (a Vilnius day has 23 or
/// 25 hours twice a year), nor on midnight passing while it runs.
/// </summary>
public sealed class ResponseNoticeTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const string Kind = MessageKinds.FollowResponsesEmail;
    private const string Setting = "Follows:ResponsesAt";
    private const string Explanation = "Šio paaiškinimo laiške būti neturi: pataisa mažina naštą šeimoms.";

    /// <summary>The Vilnius day the test started on: the job runs early that day, whatever the time is when it runs.</summary>
    private readonly DateOnly _today = ReasonsToComeSteps.VilniusToday();

    private DateOnly Yesterday => _today.AddDays(-1);

    private async Task<FollowRunOutcome> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(VilniusTime.StartOf(_today).AddHours(1), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The bill's responses became public yesterday at noon (Vilnius), as if the MPs had answered then; their order is kept.
    /// Twenty-four hours back would be two days back on the Monday morning after the clocks go forward.
    /// </summary>
    private Task AnsweredYesterdayAsync() =>
        Database.ExecuteAsync(
            "UPDATE appeals.mp_response SET announced_at = @noon + (announced_at - (SELECT min(announced_at) FROM appeals.mp_response WHERE bill_number = @b)) WHERE bill_number = @b",
            ("b", Bill), ("noon", VilniusTime.StartOf(Yesterday).AddHours(12).ToUniversalTime()));

    /// <summary>A member who followed the bill two days ago.</summary>
    private async Task<(TestBrowser Browser, string Email)> FollowerAsync(string name)
    {
        var (browser, email) = await Factory.MemberAsync(name);
        await browser.FollowBillAsync(Bill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromDays(2));
        return (browser, email);
    }

    private async Task<List<JsonElement>> EmailsToAsync(string to)
    {
        var rows = await Database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(payload ORDER BY created_at, id), '[]'::json)::text FROM notify.outbox WHERE kind = @k AND payload->>'to' = @to", ("k", Kind), ("to", to));
        using var document = JsonDocument.Parse(rows!);
        return document.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static List<(string Name, string Stance)> Listed(JsonElement email) =>
        email.GetProperty("responses").EnumerateArray().Select(r => (r.GetProperty("name").GetString()!, r.GetProperty("stance").GetString()!)).ToList();

    private async Task StartAsync()
    {
        await Database.FreeBillAsync(Bill);
        await Database.ForgetEventsAsync(Bill);
    }

    private async Task EndAsync(params MpAccount[] mps)
    {
        foreach (var mp in mps)
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE lower(payload->>'to') = lower(@a)", ("a", mp.Email));
        }

        await Database.FreeBillAsync(Bill);
        await Database.ForgetEventsAsync(Bill);
    }

    [Fact]
    public async Task Followers_get_one_email_per_bill_per_day_with_every_answer_and_none_of_the_explanations_and_nobody_else_does()
    {
        await StartAsync();
        var administrator = await AdministratorAsync();
        using var one = await Factory.MpAccountAsync(MpSteps.Azuolaitis);
        using var two = await Factory.MpAccountAsync(MpSteps.Pusinskiene);
        var (office, officeKey, officeEmail) = await Factory.OfficeMemberAsync(one, "Biuro Narė");
        using var _o = office;
        using var _ok = officeKey;
        try
        {
            await administrator.SetFreshAsync(Setting, "00:00");
            var (reader, readerEmail) = await FollowerAsync("Sekėja Pavyzdytė");
            using var _r = reader;
            var (silent, silentEmail) = await FollowerAsync("Išjungusi Pavyzdytė");
            using var _s = silent;
            using (var off = await silent.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Responses = false }))
            {
                off.StatusCode.ShouldBe(HttpStatusCode.NoContent, await off.Content.ReadAsStringAsync());
            }

            (await silent.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(new NoticeSettingsResponse(true, true, true, false));
            (await reader.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).Responses.ShouldBeTrue("on at first");

            // The MP's own account and the office follow the bill too.
            await one.Browser.FollowBillAsync(Bill);
            await Database.AgeFollowsAsync(one.Email, TimeSpan.FromDays(2));
            await office.FollowBillAsync(Bill);
            await Database.AgeFollowsAsync(officeEmail, TimeSpan.FromDays(2));

            await one.Browser.RespondOkAsync(Bill, "for", Explanation);
            await two.Browser.RespondOkAsync(Bill, "against", "Antras paaiškinimas, irgi ne laiškui.");
            await AnsweredYesterdayAsync();

            // A member who follows after the answers hears nothing about them.
            var (late, lateEmail) = await Factory.MemberAsync("Vėlyva Pavyzdytė");
            using var _l = late;
            await late.FollowBillAsync(Bill);

            var run = await RunJobAsync();
            run.Emails.ShouldBeGreaterThanOrEqualTo(3);

            var emails = await EmailsToAsync(readerEmail);
            var email = emails.ShouldHaveSingleItem();
            email.EnumerateObject().Select(p => p.Name).ShouldBe(["to", "billNumber", "billTitle", "day", "responses", "billPath", "accountPath"], ignoreOrder: true);
            (email.GetProperty("billNumber").GetString(), email.GetProperty("day").GetString(), email.GetProperty("billPath").GetString(), email.GetProperty("accountPath").GetString())
                .ShouldBe((Bill, Yesterday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "/seimas/" + Bill + "#atsakymai", "/sveiki#sekami"));
            Listed(email).ShouldBe([(MpSteps.Azuolaitis.Name, "for"), (MpSteps.Pusinskiene.Name, "against")]);
            foreach (var item in email.GetProperty("responses").EnumerateArray())
            {
                item.EnumerateObject().Select(p => p.Name).ShouldBe(["name", "faction", "stance"], ignoreOrder: true);
            }

            // The faction is the one the bill's page shows beside the answer.
            using var visitor = NewBrowser();
            var shown = (await visitor.ResponsesAsync(Bill)).Items.ToDictionary(i => i.Mp.FirstName + " " + i.Mp.LastName, i => i.Mp.Faction);
            foreach (var item in email.GetProperty("responses").EnumerateArray())
            {
                item.GetProperty("faction").GetString().ShouldBe(shown[item.GetProperty("name").GetString()!]);
            }

            email.ToString().ShouldNotContain("paaiškinimas", Case.Insensitive, "never an explanation's text");

            // Switched off, followed later: nothing. The MP's account and the office: only the other MP's answer.
            (await EmailsToAsync(silentEmail)).ShouldBeEmpty();
            (await EmailsToAsync(lateEmail)).ShouldBeEmpty();
            Listed((await EmailsToAsync(one.Email)).ShouldHaveSingleItem()).ShouldBe([(MpSteps.Pusinskiene.Name, "against")]);
            Listed((await EmailsToAsync(officeEmail)).ShouldHaveSingleItem()).ShouldBe([(MpSteps.Pusinskiene.Name, "against")]);
            (await EmailsToAsync(two.Email)).ShouldBeEmpty("the other MP's account does not follow the bill");

            // Recorded once, under the bill, keyed by the day, with no person in it; a second run sends nothing more.
            var recorded = await Database.ScalarAsync<string>(
                "SELECT row_to_json(e)::text FROM notify.bill_event e WHERE bill_number = @b AND kind = 'response'", ("b", Bill));
            recorded.ShouldNotBeNull();
            recorded.ShouldContain(Yesterday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            recorded.ShouldContain("\"outcome\":\"sent\"");
            recorded.ShouldNotContain((await Database.UserIdAsync(readerEmail)).ToString());
            await RunJobAsync();
            (await EmailsToAsync(readerEmail)).Count.ShouldBe(1, "one email per bill per day");
        }
        finally
        {
            await administrator.ResetFreshAsync(Setting);
            await office.DeleteAccountAsync();
            await EndAsync(one, two);
        }
    }

    [Fact]
    public async Task A_changed_explanation_with_the_same_stance_is_no_event_and_a_changed_stance_is()
    {
        await StartAsync();
        using var mp = await Factory.MpAccountAsync(MpSteps.Azuolaitis);
        try
        {
            async Task<DateTime> AnnouncedAsync() => await Database.ScalarAsync<DateTime>(
                "SELECT announced_at FROM appeals.mp_response WHERE bill_number = @b AND seimas_person_id = @p", ("b", Bill), ("p", MpSteps.Azuolaitis.SeimasPersonId));

            await mp.Browser.RespondOkAsync(Bill, "for", "Pirmas paaiškinimas.");
            var first = await AnnouncedAsync();
            (await Database.ScalarAsync<DateTime>("SELECT published_at FROM appeals.mp_response WHERE bill_number = @b", ("b", Bill))).ShouldBe(first, "a first publication is an event");

            await mp.Browser.RespondOkAsync(Bill, "for", "Patikslintas paaiškinimas, ta pati pozicija.");
            (await AnnouncedAsync()).ShouldBe(first, "a changed explanation with the same stance is no event");

            await mp.Browser.RespondOkAsync(Bill, "against", "Pozicija pasikeitė.");
            var changed = await AnnouncedAsync();
            changed.ShouldBeGreaterThan(first, "a changed stance is");

            using (var withdrawn = await mp.Browser.WithdrawResponseAsync(Bill))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            (await AnnouncedAsync()).ShouldBe(changed, "a withdrawal is no event");
            await mp.Browser.RespondOkAsync(Bill, "against", "Vėl skelbiu, ta pati pozicija.");
            (await AnnouncedAsync()).ShouldBe(changed, "written again with the stance it had: no event");
            await mp.Browser.RespondOkAsync(Bill, "undecided", "Dar svarstau.");
            (await AnnouncedAsync()).ShouldBeGreaterThan(changed);
        }
        finally
        {
            await EndAsync(mp);
        }
    }

    [Fact]
    public async Task Hidden_and_withdrawn_answers_are_left_out_and_a_day_with_none_left_sends_nothing()
    {
        await StartAsync();
        var administrator = await AdministratorAsync();
        var moderator = await ModeratorAsync();
        using var one = await Factory.MpAccountAsync(MpSteps.Azuolaitis);
        using var two = await Factory.MpAccountAsync(MpSteps.Pusinskiene);
        try
        {
            await administrator.SetFreshAsync(Setting, "00:00");
            var (reader, readerEmail) = await FollowerAsync("Sekėja Pavyzdytė");
            using var _r = reader;
            var hidden = await one.Browser.RespondOkAsync(Bill, "for", "Bus paslėptas.");
            await two.Browser.RespondOkAsync(Bill, "against", "Liks matomas.");
            await AnsweredYesterdayAsync();
            using (var hide = await moderator.Browser.PostAsync(
                Lt2.Api.Contracts.Mps.MpResponseRoutes.With(Lt2.Api.Contracts.Mps.MpResponseRoutes.Hide, hidden.Id),
                new Lt2.Api.Contracts.Appeals.StatementRequest { Statement = "Atsakyme paskelbti asmens duomenys." }))
            {
                hide.StatusCode.ShouldBe(HttpStatusCode.NoContent, await hide.Content.ReadAsStringAsync());
            }

            await RunJobAsync();
            Listed((await EmailsToAsync(readerEmail)).ShouldHaveSingleItem()).ShouldBe([(MpSteps.Pusinskiene.Name, "against")], "the hidden answer is left out");

            // The next day's run: the remaining answer withdrawn as well, nothing is left and nothing goes or is recorded.
            await Database.ForgetEventsAsync(Bill);
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE kind = @k AND payload->>'to' = @to", ("k", Kind), ("to", readerEmail));
            using (var withdrawn = await two.Browser.WithdrawResponseAsync(Bill))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await RunJobAsync();
            (await EmailsToAsync(readerEmail)).ShouldBeEmpty();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.bill_event WHERE bill_number = @b AND kind = 'response'", ("b", Bill))).ShouldBe(0);
        }
        finally
        {
            await administrator.ResetFreshAsync(Setting);
            await EndAsync(one, two);
        }
    }
}