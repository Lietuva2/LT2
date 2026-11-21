using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Steps for design 8.11: planned items and recent votes in the invented parliament, and follows.</summary>
public static class ReasonsToComeSteps
{
    /// <summary>The invented XVP-0412 consideration vote: per-MP results, Jonas Petraitis votes against (README of the stand-in).</summary>
    public const long TaxConsiderationVote = 99100006;

    public const string Petraitis = "jonas-petraitis";

    /// <summary>A current agenda row for the bill, as the agenda watcher would write it; earlier ones of the bill are gone.</summary>
    public static async Task PlanAsync(this DatabaseFixture database, string bill, DateOnly date, string stage = "consideration")
    {
        await database.UnplanAsync(bill);
        await database.ExecuteAsync(
            """
            INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source_url, source, first_seen, last_seen)
            VALUES (gen_random_uuid(), @b, @d, @s, 'https://www.lrs.lt/pavyzdys', 'agenda', now(), now())
            """,
            ("b", bill), ("d", date), ("s", stage));
    }

    /// <summary>
    /// The heading row (design 8.16.5: <c>div.row.list-head</c>, kept at the top of the screen) that holds the heading with this
    /// id, from its start to its end: the heading and the link beside it.
    /// </summary>
    public static string HeadRow(string html, string headingId)
    {
        var heading = html.IndexOf($"id=\"{headingId}\"", StringComparison.Ordinal);
        heading.ShouldBeGreaterThan(0, $"the page has the heading {headingId}");
        var start = html.LastIndexOf("<div class=\"row list-head\">", heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, $"{headingId} stands in a heading row");
        return html[start..html.IndexOf("</div>", heading, StringComparison.Ordinal)];
    }

    /// <summary>The bill has no current planned date any more.</summary>
    public static Task UnplanAsync(this DatabaseFixture database, string bill) =>
        database.ExecuteAsync("UPDATE parliament.planned_item SET gone_at = now() WHERE bill_number = @b AND gone_at IS NULL", ("b", bill));

    /// <summary>Moves a vote's time, returning the old one so the test can put it back.</summary>
    public static async Task<DateTime> MoveVoteAsync(this DatabaseFixture database, long voteId, DateTimeOffset heldAt)
    {
        var old = await database.ScalarAsync<DateTime>("SELECT held_at FROM parliament.vote WHERE id = @v", ("v", voteId));
        await database.ExecuteAsync("UPDATE parliament.vote SET held_at = @h WHERE id = @v", ("h", heldAt.UtcDateTime), ("v", voteId));
        return old;
    }

    public static Task RestoreVoteAsync(this DatabaseFixture database, long voteId, DateTime heldAt) =>
        database.ExecuteAsync("UPDATE parliament.vote SET held_at = @h WHERE id = @v", ("h", heldAt), ("v", voteId));

    /// <summary>Forgets the follow events of a bill, so a test sees its own events as new.</summary>
    /// <summary>
    /// Forgets which daily and weekly emails were made (design 8.24: one a day, one a week). The runs of one test at chosen
    /// moments make a period's pass, which another test choosing the same moment must not find made.
    /// </summary>
    public static Task ForgetRhythmPassesAsync(this DatabaseFixture database) => database.ExecuteAsync("DELETE FROM notify.rhythm_pass");

    public static Task ForgetEventsAsync(this DatabaseFixture database, string bill) =>
        database.ExecuteAsync("DELETE FROM notify.bill_event WHERE bill_number = @b", ("b", bill));

    /// <summary>Makes the person's follows look older, as if they had followed long ago.</summary>
    public static async Task AgeFollowsAsync(this DatabaseFixture database, string email, TimeSpan by) =>
        await database.ExecuteAsync(
            "UPDATE notify.follow SET created_at = created_at - @by WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("by", by), ("e", email));

    public static async Task FollowBillAsync(this TestBrowser browser, string number)
    {
        using var followed = await browser.SendAsync(HttpMethod.Put, FollowRoutes.BillOf(number));
        followed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await followed.Content.ReadAsStringAsync());
    }

    public static async Task FollowMpAsync(this TestBrowser browser, string slug)
    {
        using var followed = await browser.SendAsync(HttpMethod.Put, FollowRoutes.MpOf(slug));
        followed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await followed.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The things of one kind told to an address, in order, as a JSON array of their payloads: each email of that kind, and each
    /// item of that kind in a gathered email (design 8.24), so a test is not tripped by what else a run held for the person.
    /// </summary>
    public static async Task<string> NoticesAsync(this DatabaseFixture database, string kind, string to) =>
        (await database.ScalarAsync<string>(
            """
            SELECT coalesce(json_agg(p ORDER BY created_at, id, n), '[]'::json)::text FROM (
                SELECT payload AS p, created_at, id, 0 AS n FROM notify.outbox WHERE kind = @k AND payload->>'to' = @to
                UNION ALL
                SELECT item.value->'payload', o.created_at, o.id, item.ordinality
                FROM notify.outbox o, jsonb_array_elements(o.payload->'notices') WITH ORDINALITY AS item
                WHERE o.kind = 'email.gathered-notices' AND o.payload->>'to' = @to AND item.value->>'kind' = @k) told
            """,
            ("k", kind), ("to", to)))!;

    /// <summary>How many things of one kind were told to an address, alone or in a gathered email (design 8.24).</summary>
    public static async Task<long> EmailsAsync(this DatabaseFixture database, string kind, string to)
    {
        using var told = System.Text.Json.JsonDocument.Parse(await database.NoticesAsync(kind, to));
        return told.RootElement.GetArrayLength();
    }

    /// <summary>The latest thing of one kind told to an address, alone or in a gathered email, or null.</summary>
    public static async Task<string?> LatestEmailAsync(this DatabaseFixture database, string kind, string to)
    {
        using var told = System.Text.Json.JsonDocument.Parse(await database.NoticesAsync(kind, to));
        var length = told.RootElement.GetArrayLength();
        return length == 0 ? null : told.RootElement[length - 1].GetRawText();
    }

    /// <summary>The emails written to an address, of any kind, as (kind, payload) in order: what the person's inbox would hold.</summary>
    public static async Task<List<(string Kind, System.Text.Json.JsonElement Payload)>> InboxAsync(this DatabaseFixture database, string to)
    {
        var rows = await database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(json_build_object('kind', kind, 'payload', payload) ORDER BY created_at, id), '[]'::json)::text FROM notify.outbox WHERE payload->>'to' = @to",
            ("to", to));
        using var document = System.Text.Json.JsonDocument.Parse(rows!);
        return document.RootElement.EnumerateArray().Select(r => (r.GetProperty("kind").GetString()!, r.GetProperty("payload").Clone())).ToList();
    }

    /// <summary>Today in Vilnius, the day planned dates are compared with.</summary>
    public static DateOnly VilniusToday() => Lt2.SharedKernel.VilniusTime.DateOf(DateTimeOffset.UtcNow);

    /// <summary>
    /// The Monday <paramref name="weeks"/> weeks after this week's, for a sitting week of a test's own. Summaries and
    /// planned dates stay in the shared database, so each class keeps to its own weeks: DigestTests 2, 4, 6, 8 to 10 and 16,
    /// DescriptionSummaryTests 11, OrganisationCountTests 13, ReasonWithdrawalTests 14. Take an unused number. An urgent
    /// summary is always made in this real week (week 0), which every class making one shares: a test that counts the
    /// ceiling there starts from what the week already holds.
    /// </summary>
    public static DateOnly MondayInWeeks(int weeks)
    {
        var today = VilniusToday();
        return today.AddDays(7 * weeks - (((int)today.DayOfWeek + 6) % 7));
    }
}
