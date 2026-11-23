using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Mps;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// Steps of design 8.5.5 to 8.5.7 (MPs' responses) as a browser takes them, on top of <see cref="MpSteps"/>. A test that writes
/// responses removes them (<see cref="ClearResponsesAsync"/>, or <c>FreeBillAsync</c> for the bill); deleting the MP's
/// account leaves them, as the design says, so the test removes them itself.
/// </summary>
public static partial class MpResponseSteps
{
    public static Task<HttpResponseMessage> RespondAsync(this TestBrowser browser, string bill, string stance, string text) =>
        browser.SendAsync(HttpMethod.Put, MpResponseRoutes.MineOf(bill), new MpResponseRequest { Stance = stance, Text = text });

    /// <summary>Publishes or changes the response and asserts it worked.</summary>
    public static async Task<MpResponseSavedResponse> RespondOkAsync(this TestBrowser browser, string bill, string stance, string text)
    {
        using var response = await browser.RespondAsync(bill, stance, text);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MpResponseSavedResponse>();
    }

    public static Task<HttpResponseMessage> WithdrawResponseAsync(this TestBrowser browser, string bill) =>
        browser.SendAsync(HttpMethod.Delete, MpResponseRoutes.MineOf(bill));

    public static Task<BillMpResponsesResponse> ResponsesAsync(this TestBrowser browser, string bill) =>
        browser.GetJsonAsync<BillMpResponsesResponse>(MpResponseRoutes.OnBillOf(bill));

    /// <summary>Every response of a person, with its versions and reports, as if none was ever written.</summary>
    public static Task ClearResponsesAsync(this DatabaseFixture database, long seimasPersonId) =>
        database.ExecuteAsync(
            """
            DELETE FROM appeals.report WHERE target_kind = 'response' AND target_id IN (SELECT id FROM appeals.mp_response WHERE seimas_person_id = @p);
            DELETE FROM appeals.revision WHERE entity_kind = 'response' AND entity_id IN (SELECT id FROM appeals.mp_response WHERE seimas_person_id = @p);
            DELETE FROM appeals.mp_response WHERE seimas_person_id = @p;
            """,
            ("p", seimasPersonId));

    /// <summary>A member in a passkey session who joins the MP's office by the MP account's one-time link (design 8.5.3).</summary>
    public static async Task<(TestBrowser Browser, SoftwareAuthenticator Authenticator, string Email)> OfficeMemberAsync(this Lt2Factory factory, MpAccount mp, string displayName)
    {
        var (browser, authenticator, email) = await factory.PasskeyMemberAsync(displayName);
        var invitation = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new { });
        var token = Uri.UnescapeDataString(invitation.Path[(invitation.Path.IndexOf('#', StringComparison.Ordinal) + 1)..]);
        await browser.PostJsonAsync<MpLinkResponse>(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token });
        return (browser, authenticator, email);
    }

    /// <summary>A new browser signed in to the account by an emailed code (a code session).</summary>
    public static async Task<TestBrowser> CodeSessionAsync(this Lt2Factory factory, string email)
    {
        var browser = factory.NewBrowser();
        using var requested = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await browser.SignInWithCodeAsync(email);
        return browser;
    }

    /// <summary>An adoption vote on the bill, after its last vote; returns nothing, the caller removes it with <see cref="RemoveVoteAsync"/>.</summary>
    public static Task AddAdoptionVoteAsync(this DatabaseFixture database, long voteId, long fromVoteId, bool consensus = false) =>
        database.ExecuteAsync(
            """
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', @consensus, NOT @consensus, "for", against, abstain, voted, seats
            FROM parliament.vote WHERE id = @from
            """,
            ("id", voteId), ("from", fromVoteId), ("consensus", consensus));

    /// <summary>One MP's ballot in a vote (<c>For</c>, <c>Against</c>, <c>Abstain</c>, <c>DidNotVote</c>).</summary>
    public static Task AddBallotAsync(this DatabaseFixture database, long voteId, long seimasPersonId, string choice) =>
        database.ExecuteAsync(
            "INSERT INTO parliament.ballot (vote_id, person_id, faction_label, faction_id, choice) SELECT @v, id, NULL, NULL, @c FROM parliament.person WHERE seimas_person_id = @p",
            ("v", voteId), ("p", seimasPersonId), ("c", choice));

    public static async Task RemoveVoteAsync(this DatabaseFixture database, long voteId)
    {
        await database.ExecuteAsync("DELETE FROM parliament.ballot WHERE vote_id = @v", ("v", voteId));
        await database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @v", ("v", voteId));
    }

    /// <summary>The part "Seimo narių atsakymai" of a bill's page (or "Atsakymai" of a profile), up to the end of its sheet.</summary>
    public static string ResponsesPart(string html)
    {
        var start = html.IndexOf("id=\"responses\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the responses part is on the page");
        var open = html.LastIndexOf("<section", start, StringComparison.Ordinal);
        var depth = 0;
        for (var i = open; i < html.Length; i++)
        {
            if (string.CompareOrdinal(html, i, "<section", 0, 8) == 0)
            {
                depth++;
            }
            else if (string.CompareOrdinal(html, i, "</section>", 0, 10) == 0 && --depth == 0)
            {
                return html[open..(i + 10)];
            }
        }

        throw new InvalidOperationException("The responses part does not end.");
    }

    /// <summary>
    /// The markup without the islands' own content: each prerendered island (between its two Blazor markers) and every marker
    /// is removed, so what is left is what the server rendered for everyone.
    /// </summary>
    public static string WithoutIslands(string html) => Markers().Replace(Islands().Replace(html, ""), "");

    [GeneratedRegex("<!--Blazor:\\{[^>]*?\"prerenderId\":\"(?<id>[^\"]+)\"[^>]*?-->.*?<!--Blazor:\\{\"prerenderId\":\"\\k<id>\"\\}-->", RegexOptions.Singleline)]
    private static partial Regex Islands();

    [GeneratedRegex("<!--Blazor[^>]*-->")]
    private static partial Regex Markers();
}
