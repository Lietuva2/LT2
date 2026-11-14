using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Steps with organisations and their statements as a browser takes them (design 8.14.14). All names invented.</summary>
public static class OrganisationSteps
{
    /// <summary>A unique invented name, so tests never collide on "no other organisation has the same name".</summary>
    public static string NewName(string what = "Pavyzdinė asociacija") => $"{what} {Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Withdraws every statement on a bill, so a test starts from a bill no organisation spoke on.</summary>
    public static Task ClearStatementsAsync(this DatabaseFixture database, string bill) =>
        database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE bill_number = @b", ("b", bill));

    /// <summary>A unique invented host under <c>pavyzdys.test</c> for an organisation's website.</summary>
    public static string NewHost() => $"org-{Guid.NewGuid().ToString("N")[..10]}.pavyzdys.test";

    public static async Task<OrganisationCreatedResponse> RegisterOrganisationAsync(this TestBrowser browser, string? name = null, string? website = null, string? description = null)
    {
        using var response = await browser.PostAsync(OrganisationRoutes.Organisations, new OrganisationRequest
        {
            Name = name ?? NewName(), Website = website, Description = description ?? "Išgalvota organizacija bandymams.",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrganisationCreatedResponse>();
    }

    public static Task<HttpResponseMessage> PutStatementAsync(this TestBrowser browser, string bill, string slug, string stance, string text, string? link = null, int? goal = null) =>
        browser.SendAsync(HttpMethod.Put, OrganisationRoutes.StatementOf(bill, slug), new OrganisationStatementRequest { Stance = stance, Text = text, Link = link, Goal = goal });

    public static async Task<StatementSavedResponse> PublishStatementAsync(this TestBrowser browser, string bill, string slug, string stance, string text, string? link = null, int? goal = null)
    {
        using var response = await browser.PutStatementAsync(bill, slug, stance, text, link, goal);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<StatementSavedResponse>();
    }

    public static Task<BillStatementsResponse> StatementsAsync(this TestBrowser browser, string bill) =>
        browser.GetJsonAsync<BillStatementsResponse>(OrganisationRoutes.OnBillOf(bill));

    public static Task<OrganisationProfileResponse> ProfileAsync(this TestBrowser browser, string slug) =>
        browser.GetJsonAsync<OrganisationProfileResponse>(OrganisationRoutes.Of(slug));

    /// <summary>A moderator's action on an organisation or statement; asserts it succeeded.</summary>
    public static async Task ActAsync(this TestBrowser moderator, string route, Guid id, string? statement = "Bandymas.")
    {
        using var response = await moderator.PostAsync(OrganisationRoutes.With(route, id), new StatementRequest { Statement = statement ?? "" });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The newest confirmation code written to the outbox for <paramref name="to"/>.</summary>
    public static Task<string?> LatestOrganisationCodeAsync(this DatabaseFixture database, string to) =>
        database.ScalarAsync<string>(
            "SELECT payload->>'code' FROM notify.outbox WHERE kind = 'email.organisation-code' AND payload->>'to' = @to ORDER BY created_at DESC, id DESC LIMIT 1",
            ("to", to));

    /// <summary>The organisation notices written to the outbox for <paramref name="to"/>, newest first: their notice codes.</summary>
    public static async Task<List<string>> OrganisationNoticesAsync(this DatabaseFixture database, string to)
    {
        var json = await database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(payload->>'notice' ORDER BY created_at DESC, id DESC)::text, '[]') FROM notify.outbox WHERE kind = 'email.organisation-notice' AND payload->>'to' = @to",
            ("to", to));
        return JsonSerializer.Deserialize<List<string>>(json ?? "[]")!;
    }

    /// <summary>Confirms the organisation by a code sent to <c>info@</c> its host; returns the host.</summary>
    public static async Task ConfirmByDomainAsync(this TestBrowser representative, DatabaseFixture database, string slug, string host)
    {
        var address = "info@" + host;
        using (var sent = await representative.PostAsync(OrganisationRoutes.ConfirmationCodeOf(slug), new ConfirmationCodeRequest { Address = address }))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync());
        }

        var code = await database.LatestOrganisationCodeAsync(address) ?? throw new InvalidOperationException("No code in the outbox.");
        using var confirmed = await representative.PostAsync(OrganisationRoutes.ConfirmationOf(slug), new ConfirmationRequest { Code = code });
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
    }
}
