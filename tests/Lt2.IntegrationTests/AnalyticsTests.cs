using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Analytics;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.26: the analytics page and its two answers. A closed bill is made by the adoption-stage vote the import would
/// store (a copy of the bill's own vote with its ballots and faction positions) and the closing job; the test removes both.
/// </summary>
public sealed class AnalyticsTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const long BillVote = 99100102;
    private const long FinalVote = 99199626;

    [Fact]
    public async Task A_closed_bill_is_compared_with_its_frozen_counts_and_under_twenty_shows_counts_without_a_share()
    {
        using var moderator = await ModeratorAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var visitor = NewBrowser();
        await Database.PrepareBillAsync(initiator, moderator.Browser, Bill, "against");
        (await member.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        try
        {
            await CloseAsync();
            var votedAt = await Database.ScalarAsync<DateTime>("SELECT held_at FROM parliament.vote WHERE id = @v", ("v", FinalVote));
            var totals = await VoteTotalsAsync(FinalVote);

            var answer = await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics);
            answer.SmallSample.ShouldBe(20);
            answer.Year.ShouldBeNull("the whole term by default");
            var row = answer.Bills.Single(b => b.Number == Bill);
            (row.VoteId, row.IsConsensus, row.MembersFor, row.MembersAgainst).ShouldBe((FinalVote, false, 1, 1));
            row.SeimasShareFor.ShouldBe((decimal)totals.For / (totals.For + totals.Against + totals.Abstain));
            row.SeimasMajorityFor.ShouldBe(2 * totals.For > totals.For + totals.Against + totals.Abstain);
            (row.MembersShareFor, row.Compared, row.Agreed, row.Dot).ShouldBe((null, false, null, false), "two positions are a small sample");
            answer.Figures.MpsSitting.ShouldBeGreaterThan(0);
            answer.Figures.BillsDiscussed.ShouldBeGreaterThan(0);
            answer.Figures.BillsTakenPart.ShouldBeGreaterThanOrEqualTo(1);
            answer.Figures.Positions.ShouldBeGreaterThanOrEqualTo(2);
            answer.Factions.ShouldNotBeEmpty();
            answer.Factions.ShouldAllBe(f => f.MostSplit.Count <= 3);
            answer.Factions.Select(f => f.Seats).ShouldBeInOrder(SortDirection.Descending, "by seats, not by unity");

            // The same answer for a visitor and a member: nothing about the caller.
            (await member.Client.GetStringAsync(AnalyticsRoutes.Analytics)).ShouldBe(await visitor.Client.GetStringAsync(AnalyticsRoutes.Analytics));

            // The year of the closing vote holds it; a year outside the term is the whole term.
            var year = VilniusTime.DateOf(new DateTimeOffset(votedAt, TimeSpan.Zero)).Year;
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Of(year, false))).Bills.ShouldContain(b => b.Number == Bill);
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics + "?year=1990")).Year.ShouldBeNull();

            // Only accounts older than 30 days: both members are new, so the bill has no positions to compare.
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Of(null, true))).Bills.ShouldNotContain(b => b.Number == Bill);

            // The page: the fourth tab, the row in the table with its counts and "maža imtis", no share for the members.
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/analitika"));
            html.ShouldContain("id=\"tab-seimas-analitika\" aria-current=\"page\"");
            var tableRow = System.Text.RegularExpressions.Regex.Match(html, $"<tr id=\"row-{Bill}\".*?</tr>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
            tableRow.ShouldContain("1 už, 1 prieš");
            tableRow.ShouldContain("maža imtis");
            html.ShouldContain("ne reprezentatyvi apklausa");
            html.ShouldNotContain($"id=\"dot-{Bill}\"", customMessage: "no dot under twenty positions");
            html.ShouldContain("id=\"frakciju-vieningumas\"");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await RemoveFinalVoteAsync();
            await initiator.DeleteAccountAsync();
            await member.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task From_twenty_positions_a_closed_bill_has_a_dot_a_share_and_counts_towards_majorities_agreed()
    {
        using var moderator = await ModeratorAsync();
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var record = await Database.PrepareBillAsync(initiator, moderator.Browser, Bill, "for");
        try
        {
            // Nineteen more sides "for" and five "against", stored as the module stores them (the counts are aggregates).
            await Database.ExecuteAsync(
                """
                INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since)
                SELECT @r, gen_random_uuid(), CASE WHEN n <= 19 THEN 'for' ELSE 'against' END, now(), now(), now() - interval '60 days'
                FROM generate_series(1, 24) AS n
                """,
                ("r", record.Id));
            await CloseAsync();
            var totals = await VoteTotalsAsync(FinalVote);
            var seimasFor = 2 * totals.For > totals.For + totals.Against + totals.Abstain;

            using var visitor = NewBrowser();
            var answer = await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics);
            var row = answer.Bills.Single(b => b.Number == Bill);
            (row.MembersFor, row.MembersAgainst).ShouldBe((20, 5));
            (row.MembersShareFor, row.Compared, row.Dot).ShouldBe((0.8m, true, true));
            row.Agreed.ShouldBe(seimasFor);
            answer.Figures.Compared.ShouldBeGreaterThanOrEqualTo(1);

            // The older accounts' filter counts the nineteen and five aged ones, not the new initiator: still twenty-four.
            var older = (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Of(null, true))).Bills.Single(b => b.Number == Bill);
            (older.MembersFor, older.MembersAgainst, older.Compared).ShouldBe((19, 5, true));

            (await visitor.Client.GetStringAsync("/seimas/analitika")).ShouldContain($"id=\"dot-{Bill}\"");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await RemoveFinalVoteAsync();
            await initiator.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task Who_votes_like_you_answers_only_the_caller_from_their_own_sides_and_the_mps_ballots()
    {
        using var moderator = await ModeratorAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        using var visitor = NewBrowser();
        await Database.PrepareBillAsync(member, moderator.Browser, Bill, "for");
        try
        {
            // Before the bill closes there is nothing to compare.
            var before = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (before.Bills, before.Mps.Count, before.Factions.Count).ShouldBe((0, 0, 0));

            await CloseAsync();
            (await visitor.Client.GetAsync(AnalyticsRoutes.MyAgreement)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var mine = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (mine.Bills, mine.Consensus, mine.SmallSample).ShouldBe((1, 0, 20));
            var ballots = await BallotsAsync(FinalVote);
            var voted = ballots.Where(b => b.Choice is "For" or "Against" or "Abstain").ToList();
            voted.ShouldNotBeEmpty("the invented vote has ballots");
            mine.Mps.Select(m => m.Slug).ShouldBe(voted.Select(b => b.Slug), ignoreOrder: true, "every MP who voted, and nobody who did not");
            foreach (var mp in mine.Mps)
            {
                var choice = voted.Single(b => b.Slug == mp.Slug).Choice;
                (mp.Matched, mp.Common, mp.Share).ShouldBe((choice == "For" ? 1 : 0, 1, (decimal?)null), mp.Slug);
            }

            // One common vote is a small sample for everyone: listed by name.
            var byName = StringComparer.Create(CultureInfo.GetCultureInfo("lt-LT"), ignoreCase: true);
            mine.Mps.Select(m => m.Name).ShouldBe(mine.Mps.Select(m => m.Name).Order(byName));
            mine.Factions.ShouldNotBeEmpty();
            mine.Factions.ShouldAllBe(f => f.Common + f.Free <= 1 && f.Seats != null);

            // Another member, with no side on the closed bill, gets nothing: the answer is the caller's own.
            var theirs = await other.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (theirs.Bills, theirs.Mps.Count, theirs.Factions.Count).ShouldBe((0, 0, 0));

            // Never stored, never cached: the answer says no-store, and the page's HTML holds nothing of it.
            using var response = await member.Client.GetAsync(AnalyticsRoutes.MyAgreement);
            response.Headers.CacheControl?.NoStore.ShouldBe(true);
            var page = MpResponseSteps.WithoutIslands(await member.Client.GetStringAsync("/seimas/analitika"));
            page.ShouldNotContain("mine-mp-");
            page.ShouldNotContain("mine-faction-");

            // The answer has exactly these fields: no other person, no address, no side but the caller's own counts.
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["bills", "consensus", "smallSample", "factions", "mps"], ignoreOrder: true);
            document.RootElement.GetProperty("mps")[0].EnumerateObject().Select(p => p.Name)
                .ShouldBe(["slug", "name", "faction", "seats", "former", "matched", "common", "free", "share"], ignoreOrder: true);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await RemoveFinalVoteAsync();
            await member.DeleteAccountAsync();
            await other.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task A_bill_adopted_by_consensus_counts_as_a_majority_for_without_a_share_and_without_ballots_to_compare()
    {
        using var moderator = await ModeratorAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await Database.PrepareBillAsync(member, moderator.Browser, Bill, "against");
        try
        {
            await CloseAsync(consensus: true);
            using var visitor = NewBrowser();
            var row = (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics)).Bills.Single(b => b.Number == Bill);
            (row.IsConsensus, row.SeimasShareFor, row.SeimasMajorityFor, row.Dot).ShouldBe((true, null, true, false));
            WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/analitika")).ShouldContain("bendru sutarimu");

            var mine = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (mine.Bills, mine.Consensus, mine.Mps.Count, mine.Factions.Count).ShouldBe((1, 1, 0, 0));
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await RemoveFinalVoteAsync();
            await member.DeleteAccountAsync();
        }
    }

    /// <summary>The adoption-stage vote, as the import would store it, and the closing job: the record closes and its counts freeze.</summary>
    private async Task CloseAsync(bool consensus = false)
    {
        await RemoveFinalVoteAsync();
        await Database.AddAdoptionVoteAsync(FinalVote, BillVote, consensus);
        if (!consensus)
        {
            await Database.ExecuteAsync(
                "INSERT INTO parliament.ballot (vote_id, person_id, faction_label, faction_id, choice) SELECT @id, person_id, faction_label, faction_id, choice FROM parliament.ballot WHERE vote_id = @from",
                ("id", FinalVote), ("from", BillVote));
            await Database.ExecuteAsync(
                "INSERT INTO parliament.faction_position (vote_id, faction_label, faction_id, position, \"for\", against, abstain, did_not_vote) SELECT @id, faction_label, faction_id, position, \"for\", against, abstain, did_not_vote FROM parliament.faction_position WHERE vote_id = @from",
                ("id", FinalVote), ("from", BillVote));
        }

        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>().CloseFinishedAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
    }

    private async Task RemoveFinalVoteAsync()
    {
        await Database.ExecuteAsync("DELETE FROM parliament.faction_position WHERE vote_id = @v", ("v", FinalVote));
        await Database.RemoveVoteAsync(FinalVote);
    }

    private async Task<(int For, int Against, int Abstain)> VoteTotalsAsync(long vote) =>
        ((int)(await Database.ScalarAsync<int>("SELECT \"for\" FROM parliament.vote WHERE id = @v", ("v", vote))),
         (int)(await Database.ScalarAsync<int>("SELECT against FROM parliament.vote WHERE id = @v", ("v", vote))),
         (int)(await Database.ScalarAsync<int>("SELECT abstain FROM parliament.vote WHERE id = @v", ("v", vote))));

    /// <summary>Every ballot of a vote, by the MP's slug, with the choice as stored (<c>For</c>, <c>Against</c>, …).</summary>
    private async Task<IReadOnlyList<(string Slug, string Choice)>> BallotsAsync(long vote)
    {
        var json = await Database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(json_build_array(p.slug, b.choice)), '[]')::text FROM parliament.ballot b JOIN parliament.person p ON p.id = b.person_id WHERE b.vote_id = @v",
            ("v", vote));
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.EnumerateArray().Select(e => (e[0].GetString()!, e[1].GetString()!)).ToList();
    }
}
