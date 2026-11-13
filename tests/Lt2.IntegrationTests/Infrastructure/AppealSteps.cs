using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Appeal steps as a browser takes them (design 8.1). Bills come from the invented parliament.</summary>
public static class AppealSteps
{
    /// <summary>The invented parliament's bills; tests free the one they use first (only one appeal per bill).</summary>
    public const string TaxBill = "XVP-0412";
    public const string SecondBill = "XVP-0533";
    public const string ThirdBill = "XVP-0618";

    public const string Description = "Įvedamas trečias pajamų mokesčio tarifas: metinėms pajamoms, viršijančioms 60 vidutinių darbo užmokesčių, būtų taikomas 32 % tarifas vietoj 20 %.";
    public const string AgainstReason = "Akcijų pardavimo įskaičiavimas paliečia vienkartinius įvykius, pvz., šeimos verslo pardavimą.";
    public const string ForReason = "Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.";

    /// <summary>Leaves the bill without an appeal: earlier tests' appeals on it are marked rejected, as a hide does.</summary>
    public static Task FreeBillAsync(this DatabaseFixture database, string number) =>
        database.ExecuteAsync("UPDATE appeals.appeal SET status = 'rejected' WHERE bill_number = @n AND status <> 'rejected'", ("n", number));

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

    /// <summary>Opens the appeal (design 8.1): a neutral description and a threshold, nothing else.</summary>
    public static async Task<HttpResponseMessage> OpenAppealAsync(this TestBrowser browser, string number, int threshold = 100, string? description = null) =>
        await browser.PostAsync(AppealRoutes.AppealOf(number), new OpenAppealRequest { Description = description ?? Description, Threshold = threshold });

    public static Task<HttpResponseMessage> TakeSideAsync(this TestBrowser browser, string number, string side) =>
        browser.SendAsync(HttpMethod.Put, AppealRoutes.PositionOf(number), new PositionRequest { Side = side });

    public static Task<HttpResponseMessage> WriteReasonAsync(this TestBrowser browser, string number, string side, string text) =>
        browser.PostAsync(AppealRoutes.ReasonsOf(number), new ReasonRequest { Side = side, Text = text });

    public static Task<HttpResponseMessage> PublishAsync(this TestBrowser moderator, Guid appealId, string? statement = null) =>
        moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishAppeal, appealId), new ModerationRequest { Statement = statement });

    /// <summary>
    /// Opens the appeal on a freed bill as <paramref name="initiator"/> and has <paramref name="moderator"/> publish it.
    /// Then, unless <paramref name="side"/> is null, the initiator takes that side and writes the standard argument for it
    /// (<see cref="ForReason"/> or <see cref="AgainstReason"/>) like anyone else, and the moderator marks it seen: the
    /// state tests used to get from a side and a first argument at opening, before 2026-11-13.
    /// </summary>
    public static async Task<AppealResponse> OpenPublishedAsync(this DatabaseFixture database, TestBrowser initiator, TestBrowser moderator, string number, string? side = "against", int threshold = 100)
    {
        await database.FreeBillAsync(number);
        using var opened = await initiator.OpenAppealAsync(number, threshold);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var appeal = await opened.ReadAsync<AppealResponse>();
        using var published = await moderator.PublishAsync(appeal.Id);
        published.StatusCode.ShouldBe(HttpStatusCode.NoContent, await published.Content.ReadAsStringAsync());
        if (side is null)
        {
            return appeal;
        }

        using var took = await initiator.TakeSideAsync(number, side);
        took.StatusCode.ShouldBe(HttpStatusCode.NoContent, await took.Content.ReadAsStringAsync());
        using var wrote = await initiator.WriteReasonAsync(number, side, side == "for" ? ForReason : AgainstReason);
        wrote.StatusCode.ShouldBe(HttpStatusCode.Created, await wrote.Content.ReadAsStringAsync());
        var reason = await wrote.ReadAsync<ReasonCreatedResponse>();
        using var seen = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, reason.Id), new ModerationRequest());
        seen.StatusCode.ShouldBe(HttpStatusCode.NoContent, await seen.Content.ReadAsStringAsync());
        return appeal;
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

    public static Task<AppealResponse> AppealAsync(this TestBrowser browser, string number) => browser.GetJsonAsync<AppealResponse>(AppealRoutes.AppealOf(number));

    public static Task<MyAppealResponse> MineAsync(this TestBrowser browser, string number) => browser.GetJsonAsync<MyAppealResponse>(AppealRoutes.MineOf(number));

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
