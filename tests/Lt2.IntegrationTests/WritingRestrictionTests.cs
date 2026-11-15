using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3 (decided 2026-11-13): a moderator restricts an account from writing, until a day or until lifted. It blocks
/// what the person writes for others to read and nothing else, is never shown to anyone else, is audited and emailed,
/// and goes with the account.
/// </summary>
public sealed class WritingRestrictionTests(DatabaseFixture database) : ApiTest(database)
{
    private static Task<HttpResponseMessage> RestrictAuthorAsync(TestBrowser staff, Guid reasonId, DateOnly? until = null, string statement = "Įžeidžiantys argumentai kitiems naudotojams.") =>
        staff.PostAsync(AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Until = until, Statement = statement });

    private Task<long> OutboxAsync(string email, string notice) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.writing-restriction' AND payload->>'to' = @to AND payload->>'notice' = @n", ("to", email), ("n", notice));

    [Fact]
    public async Task A_restriction_blocks_every_kind_of_writing_and_nothing_else_until_an_administrator_lifts_it()
    {
        var (moderator, administrator) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");

        // The person: a side and an argument.
        var (person, email) = await Factory.MemberAsync("Rašanti Asmenybė");
        using var _p = person;
        await Database.FreeBillAsync(AppealSteps.SecondBill);

        (await person.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Argumentas, dėl kurio rašymas bus apribotas.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var stranger = NewBrowser();
        var before = await stranger.AppealAsync(AppealSteps.ThirdBill);
        var reasonId = before.Reasons.Single(r => r.AuthorName == "Rašanti Asmenybė").Id;
        var publicBefore = await stranger.Client.GetStringAsync(AppealRoutes.AppealOf(AppealSteps.ThirdBill));

        using (var set = await RestrictAuthorAsync(moderator.Browser, reasonId))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.Created, await set.Content.ReadAsStringAsync());
        }

        // Nothing public changes: the same answer, byte for byte, and no count moved.
        (await stranger.Client.GetStringAsync(AppealRoutes.AppealOf(AppealSteps.ThirdBill))).ShouldBe(publicBefore);

        // The person's bill pages say so in place of the writing box, on a bill nobody took part in too; nobody else's do.
        foreach (var path in new[] { "/seimas/XVP-0618", "/seimas/XVP-0533" })
        {
            var own = WebUtility.HtmlDecode(await person.Client.GetStringAsync(path));
            own.ShouldContain("Moderatorius apribojo jūsų galimybę rašyti.", customMessage: path);
            own.ShouldContain("Priežastis: Įžeidžiantys argumentai kitiems naudotojams.", customMessage: path);
        }

        WebUtility.HtmlDecode(await initiator.Client.GetStringAsync("/seimas/XVP-0618")).ShouldNotContain("apribojo");
        WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/XVP-0618")).ShouldNotContain("apribojo");

        // Blocked: an argument (on a bill without a record too, which stays without one) and a date flag.
        using (var reason = await person.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Dar vienas argumentas."))
        {
            await reason.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        using (var fresh = await person.WriteReasonAsync(AppealSteps.SecondBill, "against", "Argumentas apie rinkliavas."))
        {
            await fresh.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.appeal WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe(0);

        using (var flag = await person.PostAsync(AppealRoutes.FlagsOf(AppealSteps.ThirdBill), new FlagRequest { Kind = "date-wrong", Link = "https://www.lrs.lt/darbotvarke" }))
        {
            await flag.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        // Untouched: a side, "Man svarbu", a report, the account, signing in again.
        (await person.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var initiatorsReason = before.Reasons.Single(r => r.Text == AppealSteps.AgainstReason).Id;
        (await person.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(initiatorsReason))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = initiatorsReason, Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.Client.GetAsync(ApiRoutes.Me.Export)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await person.Client.GetAsync(ApiRoutes.Me.Sessions)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var again = NewBrowser())
        {
            await again.PostAsync(ApiRoutes.Auth.Code, new Lt2.Api.Contracts.Auth.RequestCodeRequest { Email = email });
            await again.SignInWithCodeAsync(email);
            (await again.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.OK, "signing in is not touched");
        }

        // Only the person sees it, with the reason; and the email says so, with where to object, and no side.
        var mine = await person.GetJsonAsync<MyRestrictionResponse>(AppealRoutes.MyRestriction);
        mine.ShouldBe(new MyRestrictionResponse(true, null, "Įžeidžiantys argumentai kitiems naudotojams."));
        (await initiator.GetJsonAsync<MyRestrictionResponse>(AppealRoutes.MyRestriction)).Restricted.ShouldBeFalse();
        (await OutboxAsync(email, "restricted")).ShouldBe(1);
        var payload = await Database.ScalarAsync<string>(
            "SELECT payload::text FROM notify.outbox WHERE kind = 'email.writing-restriction' AND payload->>'to' = @to", ("to", email));
        payload.ShouldNotBeNull();
        payload.ShouldContain("moderatoriai@lt2.test");
        payload.ShouldNotContain("\"for\"");
        payload.ShouldNotContain("against");
        payload.ShouldNotContain("side", Case.Insensitive);

        // An administrator lifts it; the person is told and may write again.
        var holding = await administrator.Browser.GetJsonAsync<List<RestrictionResponse>>(AppealRoutes.Restrictions);
        var restriction = holding.Single(r => r.DisplayName == "Rašanti Asmenybė");
        restriction.Until.ShouldBeNull();
        (await administrator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.LiftRestriction, restriction.Id), new StatementRequest { Statement = "Pasitikslinome, apribojimas atšaukiamas." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OutboxAsync(email, "lifted")).ShouldBe(1);
        (await person.GetJsonAsync<MyRestrictionResponse>(AppealRoutes.MyRestriction)).Restricted.ShouldBeFalse();
        (await person.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Vėl galiu rašyti.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Audited like every moderation action, without anything about sides.
        foreach (var action in new[] { "moderation.restriction.restrict", "moderation.restriction.lift" })
        {
            var details = await Database.ScalarAsync<string>(
                "SELECT details::text FROM audit.event WHERE action = @a AND target = @t", ("a", action), ("t", "restriction:" + restriction.Id));
            details.ShouldNotBeNull(action);
            details.ShouldNotContain("\"for\"");
            details.ShouldNotContain("against");
        }
    }

    [Fact]
    public async Task Restricting_and_lifting_need_a_moderator_or_an_administrator_in_a_staff_passkey_session()
    {
        var (moderator, administrator) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var reasonId = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var stranger = NewBrowser();
        using var codeSession = NewBrowser();
        await codeSession.PostAsync(ApiRoutes.Auth.Code, new Lt2.Api.Contracts.Auth.RequestCodeRequest { Email = moderator.Email });
        await codeSession.SignInWithCodeAsync(moderator.Email);

        foreach (var (who, browser, expected) in new[]
        {
            ("stranger", stranger, HttpStatusCode.Unauthorized), ("member", member, HttpStatusCode.Forbidden),
            ("moderator signed in by code", codeSession, HttpStatusCode.Forbidden),
        })
        {
            (await RestrictAuthorAsync(browser, reasonId)).StatusCode.ShouldBe(expected, who);
        }

        foreach (var (who, browser) in new[] { ("member", member), ("moderator signed in by code", codeSession) })
        {
            (await browser.Client.GetAsync(AppealRoutes.Restrictions)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, who);
            (await browser.Client.GetAsync(AppealRoutes.With(AppealRoutes.ReasonAuthor, reasonId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, who);
        }

        // A past last day is refused; one restriction at a time.
        using (var past = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Until = new DateOnly(2020, 1, 1), Statement = "Per vėlu." }))
        {
            await past.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "restriction.until-past");
        }

        using (var empty = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Statement = " " }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a statement is required");
        }

        var today = ReasonsToComeSteps.VilniusToday();
        // An administrator sets it, like any moderation action (decided 2026-11-13).
        using (var set = await administrator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Until = today.AddDays(3), Statement = "Kartojasi tie patys įžeidimai." }))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.Created, await set.Content.ReadAsStringAsync());
        }

        using (var twice = await RestrictAuthorAsync(moderator.Browser, reasonId))
        {
            await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, "restriction.exists");
        }

        (await initiator.GetJsonAsync<MyRestrictionResponse>(AppealRoutes.MyRestriction)).Until.ShouldBe(today.AddDays(3));
        var id = (await moderator.Browser.GetJsonAsync<List<RestrictionResponse>>(AppealRoutes.Restrictions)).Single(r => r.Until == today.AddDays(3) && r.Statement.StartsWith("Kartojasi", StringComparison.Ordinal)).Id;
        (await member.PostAsync(AppealRoutes.With(AppealRoutes.LiftRestriction, id), new StatementRequest { Statement = "Ne." })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.LiftRestriction, id), new StatementRequest { Statement = "Atšaukta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var gone = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.LiftRestriction, id), new StatementRequest { Statement = "Dar kartą." }))
        {
            await gone.ShouldBeProblemAsync(HttpStatusCode.Conflict, "restriction.not-holding");
        }
    }

    [Fact]
    public async Task The_moderator_sees_the_name_writing_and_history_never_an_address_side_or_mark_and_a_dated_restriction_ends_by_itself()
    {
        var (moderator, _) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "for");
        var (person, email) = await Factory.MemberAsync("Peržiūrima Asmenybė");
        using var _p = person;
        (await person.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Argumentas prieš, nors pats prašo už.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var reasons = (await person.AppealAsync(AppealSteps.ThirdBill)).Reasons;
        var reasonId = reasons.Single(r => r.AuthorName == "Peržiūrima Asmenybė").Id;
        (await person.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons.Single(r => r.Side == "for").Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var today = ReasonsToComeSteps.VilniusToday();
        (await RestrictAuthorAsync(moderator.Browser, reasonId, today, "Vienos dienos apribojimas.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var view = await moderator.Browser.Client.GetAsync(AppealRoutes.With(AppealRoutes.ReasonAuthor, reasonId));
        var json = await view.Content.ReadAsStringAsync();
        var author = JsonSerializer.Deserialize<AuthorViewResponse>(json, JsonSerializerOptions.Web)!;
        author.DisplayName.ShouldBe("Peržiūrima Asmenybė");
        author.Reasons.Single().Side.ShouldBe("against", "the argument's own side, public anyway");
        author.Restrictions.Single().Holds.ShouldBeTrue();
        author.Actions.ShouldContain(a => a.TargetKind == "restriction" && a.Action == "restrict");
        json.ShouldNotContain(email, Case.Insensitive);
        json.ShouldNotContain("\"for\"", customMessage: "the person asks to vote for: that must not show");
        json.ShouldNotContain("endorse", Case.Insensitive);
        json.ShouldNotContain("position", Case.Insensitive);
        json.ShouldNotContain((await Database.UserIdAsync(email)).ToString(), Case.Insensitive);

        (await person.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Šiandien dar negaliu.")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The last day is over (moved into the past here): nothing runs, the next use finds it ended.
        await Database.ExecuteAsync("UPDATE appeals.writing_restriction SET ends_at = now() - interval '1 minute' WHERE user_id = @u", ("u", await Database.UserIdAsync(email)));
        (await person.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Dabar jau galiu.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await moderator.Browser.GetJsonAsync<List<RestrictionResponse>>(AppealRoutes.Restrictions)).ShouldNotContain(r => r.DisplayName == "Peržiūrima Asmenybė");
    }

    [Fact]
    public async Task Deleting_the_account_deletes_its_restriction_and_keeps_no_address()
    {
        var (moderator, _) = await StaffAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var (person, email) = await Factory.MemberAsync("Išeinanti Asmenybė");
        using var _p = person;
        (await person.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Argumentas prieš išeinant.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var reasonId = (await person.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single(r => r.AuthorName == "Išeinanti Asmenybė").Id;
        (await RestrictAuthorAsync(moderator.Browser, reasonId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = await Database.UserIdAsync(email);

        using (var deleted = await person.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.writing_restriction WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'appeals' AND table_name = 'writing_restriction' AND (column_name LIKE '%email%' OR column_name LIKE '%address%' OR column_name LIKE '%hash%')"))
            .ShouldBe(0, "no address and no hash of one is kept");
    }
}
