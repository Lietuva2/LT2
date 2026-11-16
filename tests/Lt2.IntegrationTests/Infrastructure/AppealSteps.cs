using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>A bill a test prepared: its record and number.</summary>
public sealed record PreparedBill(Guid Id, string Number);

/// <summary>Steps on a bill as a browser takes them (design 8.12). Bills come from the invented parliament.</summary>
public static class AppealSteps
{
    /// <summary>The invented parliament's bills, all active; tests free the one they use first (one record per bill).</summary>
    public const string TaxBill = "XVP-0412";
    public const string SecondBill = "XVP-0533";
    public const string ThirdBill = "XVP-0618";

    public const string Description = "Įvedamas trečias pajamų mokesčio tarifas: metinėms pajamoms, viršijančioms 60 vidutinių darbo užmokesčių, būtų taikomas 32 % tarifas vietoj 20 %.";
    public const string AgainstReason = "Akcijų pardavimo įskaičiavimas paliečia vienkartinius įvykius, pvz., šeimos verslo pardavimą.";
    public const string ForReason = "Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.";

    /// <summary>
    /// Leaves the bill without a record, as if nobody had taken part: earlier tests' records on it are deleted with their
    /// sides, arguments and marks (the foreign keys cascade), and organisations' statements on it (design 8.14.14), which
    /// refer to the bill by number, are deleted too, as are MPs' responses on it (design 8.5.5) with their versions and reports.
    /// </summary>
    public static async Task FreeBillAsync(this DatabaseFixture database, string number)
    {
        // Comments on the bill (design 8.23) refer to it by number: they go with their "Sutinku" and the reports on them.
        await database.ExecuteAsync(
            """
            DELETE FROM appeals.report WHERE target_kind = 'comment' AND target_id IN (SELECT id FROM appeals.comment WHERE bill_number = @n);
            DELETE FROM appeals.comment WHERE bill_number = @n;
            """,
            ("n", number));
        await database.ExecuteAsync(
            """
            DELETE FROM appeals.report WHERE target_kind = 'response' AND target_id IN (SELECT id FROM appeals.mp_response WHERE bill_number = @n);
            DELETE FROM appeals.revision WHERE entity_kind = 'response' AND entity_id IN (SELECT id FROM appeals.mp_response WHERE bill_number = @n);
            DELETE FROM appeals.mp_response WHERE bill_number = @n;
            """,
            ("n", number));
        await database.ExecuteAsync("DELETE FROM appeals.organisation_statement WHERE bill_number = @n", ("n", number));
        await database.ExecuteAsync("DELETE FROM appeals.bill_record WHERE bill_number = @n", ("n", number));
    }

    /// <summary>A completed member with a display name and, unless told otherwise, the consent to store positions.</summary>
    public static async Task<(TestBrowser Browser, string Email)> MemberAsync(this Lt2Factory factory, string? displayName = "Rūta Mockutė", bool consent = true)
    {
        var browser = factory.NewBrowser();
        var email = await browser.RegisterAndSignInAsync(complete: false);
        await browser.CompleteRegistrationAsync(displayName);
        if (consent)
        {
            using var given = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion });
            given.StatusCode.ShouldBe(HttpStatusCode.NoContent, await given.Content.ReadAsStringAsync());
        }

        return (browser, email);
    }

    public static Task<HttpResponseMessage> TakeSideAsync(this TestBrowser browser, string number, string side) =>
        browser.SendAsync(HttpMethod.Put, AppealRoutes.PositionOf(number), new PositionRequest { Side = side });

    public static Task<HttpResponseMessage> WriteReasonAsync(this TestBrowser browser, string number, string side, string text) =>
        browser.PostAsync(AppealRoutes.ReasonsOf(number), new ReasonRequest { Side = side, Text = text });

    /// <summary>
    /// A freed bill with its record (design 8.12.1), the state most tests start from. The moderator writes the standard
    /// description (which creates the record) and, when <paramref name="threshold"/> is not the default 100, sets the bill's
    /// own number. Then, unless <paramref name="side"/> is null, <paramref name="member"/> takes that side and writes the
    /// standard argument for it (<see cref="ForReason"/> or <see cref="AgainstReason"/>), and the moderator marks it seen.
    /// </summary>
    public static async Task<PreparedBill> PrepareBillAsync(this DatabaseFixture database, TestBrowser member, TestBrowser moderator, string number, string? side = "against", int threshold = 100)
    {
        await database.FreeBillAsync(number);
        using (var described = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(number), new ModeratorDescriptionRequest { Description = Description, Statement = "Bandymas." }))
        {
            described.StatusCode.ShouldBe(HttpStatusCode.NoContent, await described.Content.ReadAsStringAsync());
        }

        var record = await moderator.RecordAsync(number);
        if (threshold != 100)
        {
            using var set = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record.Id!.Value), new ModeratorThresholdRequest { Threshold = threshold, Statement = "Bandymas." });
            set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
        }

        var prepared = new PreparedBill(record.Id!.Value, number);
        if (side is null)
        {
            return prepared;
        }

        using var took = await member.TakeSideAsync(number, side);
        took.StatusCode.ShouldBe(HttpStatusCode.NoContent, await took.Content.ReadAsStringAsync());
        using var wrote = await member.WriteReasonAsync(number, side, side == "for" ? ForReason : AgainstReason);
        wrote.StatusCode.ShouldBe(HttpStatusCode.Created, await wrote.Content.ReadAsStringAsync());
        var reason = await wrote.ReadAsync<ReasonCreatedResponse>();
        using var seen = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, reason.Id), new ModerationRequest());
        seen.StatusCode.ShouldBe(HttpStatusCode.NoContent, await seen.Content.ReadAsStringAsync());
        return prepared;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty body.");

    public static async Task<T> GetJsonAsync<T>(this TestBrowser browser, string uri)
    {
        using var response = await browser.Client.GetAsync(uri);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{uri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web))!;
    }

    /// <summary>A POST whose answer is read like a GET's (a token in the body instead of the address, design 6.8).</summary>
    public static async Task<T> PostJsonAsync<T>(this TestBrowser browser, string uri, object body)
    {
        using var response = await browser.PostAsync(uri, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{uri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web))!;
    }

    public static Task<BillRecordResponse> RecordAsync(this TestBrowser browser, string number) => browser.GetJsonAsync<BillRecordResponse>(AppealRoutes.RecordOf(number));

    /// <summary>The moderators' state of a bill (design 8.12.3).</summary>
    public static Task<ModerationBillResponse> ModerationBillAsync(this TestBrowser moderator, string number) =>
        moderator.GetJsonAsync<ModerationBillResponse>(AppealRoutes.ModerationBillOf(number));

    /// <summary>The bill's record id, created by a moderator if the bill has none (design 8.12.1).</summary>
    public static async Task<Guid> RecordIdAsync(this TestBrowser moderator, string number)
    {
        using var response = await moderator.PostAsync(AppealRoutes.BillRecordOf(number), new { });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<BillRecordIdResponse>()).Id;
    }

    public static Task<MyBillRecordResponse> MineAsync(this TestBrowser browser, string number) => browser.GetJsonAsync<MyBillRecordResponse>(AppealRoutes.MineOf(number));

    /// <summary>Makes the account look as if it became a member <paramref name="days"/> days ago (and its positions with it).</summary>
    public static async Task AgeAccountAsync(this DatabaseFixture database, string email, int days)
    {
        await database.ExecuteAsync(
            "UPDATE identity.profile SET registered_at = registered_at - make_interval(days => @d), created_at = created_at - make_interval(days => @d) WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("d", days), ("e", email));
        await database.ExecuteAsync(
            "UPDATE appeals.position SET member_since = member_since - make_interval(days => @d) WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("d", days), ("e", email));
    }

    public static Task<Guid> UserIdAsync(this DatabaseFixture database, string email) =>
        database.ScalarAsync<Guid>("SELECT id FROM identity.\"user\" WHERE email = @e", ("e", email));
}
