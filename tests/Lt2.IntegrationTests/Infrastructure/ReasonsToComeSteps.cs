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

    public static Task<long> EmailsAsync(this DatabaseFixture database, string kind, string to) =>
        database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = @k AND payload->>'to' = @to", ("k", kind), ("to", to));

    public static Task<string?> LatestEmailAsync(this DatabaseFixture database, string kind, string to) =>
        database.ScalarAsync<string>(
            "SELECT payload::text FROM notify.outbox WHERE kind = @k AND payload->>'to' = @to ORDER BY created_at DESC, id DESC LIMIT 1", ("k", kind), ("to", to));

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
