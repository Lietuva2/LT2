using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Organisations;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>A bill a comments test prepared: its argument, an organisation's statement and the organisation's representative.</summary>
public sealed record CommentedBill(string Number, Guid ReasonId, Guid StatementId, string OrganisationSlug, string OrganisationName, TestBrowser ArgumentAuthor, string ArgumentAuthorEmail, TestBrowser Representative);

/// <summary>Steps with comments (design 8.23) as a browser takes them. Texts and names invented.</summary>
public static class CommentSteps
{
    public static Task<HttpResponseMessage> CommentAsync(this TestBrowser browser, string kind, Guid targetId, string text) =>
        browser.PostAsync(CommentRoutes.ThreadOf(kind, targetId), new CommentRequest { Text = text });

    /// <summary>Writes a comment and asserts it was published; its id.</summary>
    public static async Task<Guid> CommentOkAsync(this TestBrowser browser, string kind, Guid targetId, string text)
    {
        using var response = await browser.CommentAsync(kind, targetId, text);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<CommentCreatedResponse>()).Id;
    }

    public static Task<CommentThreadResponse> ThreadAsync(this TestBrowser browser, string kind, Guid targetId, int? shown = null) =>
        browser.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf(kind, targetId, shown));

    public static Task<MyCommentsResponse> MyCommentsAsync(this TestBrowser browser, string bill) =>
        browser.GetJsonAsync<MyCommentsResponse>(CommentRoutes.MineOf(bill));

    public static Task<HttpResponseMessage> AgreeAsync(this TestBrowser browser, Guid commentId) =>
        browser.SendAsync(HttpMethod.Put, CommentRoutes.AgreementOf(commentId));

    public static Task<HttpResponseMessage> DisagreeAsync(this TestBrowser browser, Guid commentId) =>
        browser.SendAsync(HttpMethod.Delete, CommentRoutes.AgreementOf(commentId));

    public static Task<HttpResponseMessage> DeleteCommentAsync(this TestBrowser browser, Guid commentId) =>
        browser.SendAsync(HttpMethod.Delete, CommentRoutes.OneOf(commentId));

    /// <summary>A moderator's action on a comment; asserts it succeeded.</summary>
    public static async Task ActOnCommentAsync(this TestBrowser moderator, string route, Guid commentId, string? statement = "Bandymas: komentaras pažeidžia taisykles.")
    {
        using var response = await moderator.PostAsync(CommentRoutes.With(route, commentId), new StatementRequest { Statement = statement ?? "" });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    public static async Task ReportCommentAsync(this TestBrowser browser, Guid commentId, string ground = "insult", string? text = "Įžeidžia kitus.")
    {
        using var response = await browser.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "comment", TargetId = commentId, Ground = ground, Text = text });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A freed bill with a published, seen argument (by <c>ArgumentAuthor</c>, side "against") and an organisation's published
    /// statement on it (by a new representative): the two of the three targets that need no MP.
    /// </summary>
    public static async Task<CommentedBill> PrepareCommentedBillAsync(this Lt2Factory factory, DatabaseFixture database, TestBrowser moderator, string bill)
    {
        var (author, authorEmail) = await factory.MemberAsync("Rūta Mockutė");
        await database.PrepareBillAsync(author, moderator, bill, "against");
        var reasonId = (await author.RecordAsync(bill)).Reasons.ShouldHaveSingleItem().Id;
        var (representative, _) = await factory.MemberAsync("Tomas Atstovas");
        var name = OrganisationSteps.NewName("Pavyzdinė asociacija (komentarai)");
        var organisation = await representative.RegisterOrganisationAsync(name);
        var statement = await representative.PublishStatementAsync(bill, organisation.Slug, "for", "Asociacija pritaria pataisai, nes ji mažina naštą šeimoms.");
        return new CommentedBill(bill, reasonId, statement.Id, organisation.Slug, name, author, authorEmail, representative);
    }

    /// <summary>The comment notices (design 8.23.9) in the outbox to <paramref name="to"/>, oldest first.</summary>
    public static async Task<List<JsonElement>> CommentNoticesAsync(this DatabaseFixture database, string to)
    {
        var rows = await database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(payload ORDER BY created_at, id), '[]'::json)::text FROM notify.outbox WHERE kind = 'email.comment-notice' AND lower(payload->>'to') = lower(@to)",
            ("to", to));
        using var document = JsonDocument.Parse(rows!);
        return document.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
    }

    /// <summary>The moderation notices about comments (<c>comment-hidden</c>, <c>comment-restored</c>) in the outbox to <paramref name="to"/>.</summary>
    public static async Task<List<string>> CommentModerationNoticesAsync(this DatabaseFixture database, string to)
    {
        var rows = await database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(payload->>'notice' ORDER BY created_at, id), '[]'::json)::text FROM notify.outbox WHERE payload->>'notice' LIKE 'comment-%' AND lower(payload->>'to') = lower(@to)",
            ("to", to));
        return JsonSerializer.Deserialize<List<string>>(rows!)!;
    }

    /// <summary>The fields of an object in a JSON answer, for a check that compares an answer's exact set of fields.</summary>
    public static IReadOnlyList<string> Fields(JsonElement element) => element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
}
