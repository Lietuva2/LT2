using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Analytics;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Lt2.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.26: the analytics page and its two answers. A closed bill is made by the adoption-stage vote the import would
/// store (a copy of the bill's own vote with its ballots and faction positions) and the closing job; the test removes both.
/// The kept copies (8.26.6) are emptied before each read that must see a change the data version does not cover.
/// </summary>
public sealed class AnalyticsTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const long BillVote = 99100102;
    private const long FinalVote = 99199626;

    /// <summary>Bills of the test's own closed records that are not in the catalogue (a member's 20 common votes).</summary>
    private const string ExtraBills = "XVP-T%";

    private void FreshCopies() => Factory.Services.GetRequiredService<AnalyticsCopies>().Clear();

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

            // The same answer for a visitor and a member: nothing about the caller.
            (await member.Client.GetStringAsync(AnalyticsRoutes.Analytics)).ShouldBe(await visitor.Client.GetStringAsync(AnalyticsRoutes.Analytics));

            // The year of the closing vote holds it; another year of the term leaves it out; a year outside the term is the whole term.
            var year = VilniusTime.DateOf(new DateTimeOffset(votedAt, TimeSpan.Zero)).Year;
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Of(year, false))).Bills.ShouldContain(b => b.Number == Bill);
            var otherYear = answer.Years.First(y => y != year);
            var other = await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Of(otherYear, false));
            other.Year.ShouldBe(otherYear);
            other.Bills.ShouldNotContain(b => b.Number == Bill);
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics + "?year=1990")).Year.ShouldBeNull();

            // Only accounts older than 30 days: both members are new, so the bill has no positions to compare.
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Of(null, true))).Bills.ShouldNotContain(b => b.Number == Bill);

            // The page: "Statistika" in the menu, the row in the table with its counts and "maža imtis", no share for the members.
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/statistika"));
            html.ShouldContain("id=\"nav-statistics\" aria-current=\"page\"");
            html.ShouldNotContain("id=\"project-tabs\"", customMessage: "owner, 2026-11-17: a menu item of its own, no tab row");
            var tableRow = Regex.Match(html, $"<tr id=\"row-{Bill}\".*?</tr>", RegexOptions.Singleline).Value;
            tableRow.ShouldContain("1 už, 1 prieš");
            tableRow.ShouldContain("maža imtis");
            html.ShouldContain("Tai nėra reprezentatyvi apklausa");
            html.ShouldContain("id=\"analytics-counted-at\"");
            html.ShouldContain("id=\"needs-script\"");
            html.ShouldNotContain($"id=\"dot-{Bill}\"", customMessage: "no dot under twenty positions");

            // A record the moderators hid is left out (design 8.12.3: its page shows no taking part).
            var record = await moderator.Browser.RecordAsync(Bill);
            (await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, record.Id!.Value), new StatementRequest { Statement = "Bandymas: paslėpta." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics)).Bills.ShouldNotContain(b => b.Number == Bill);
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
    public async Task The_four_figures_count_exactly_what_the_design_says()
    {
        using var moderator = await ModeratorAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var visitor = NewBrowser();
        await Database.PrepareBillAsync(member, moderator.Browser, Bill, "for");
        try
        {
            FreshCopies();
            var figures = (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics)).Figures;

            // Bills on the agenda of a sitting of the current term, and their counts as their pages show them.
            const string Discussed =
                """
                SELECT DISTINCT b.number FROM parliament.agenda_item i JOIN parliament.sitting s ON s.id = i.sitting_id
                JOIN parliament.session ses ON ses.id = s.session_id JOIN parliament.bill b ON b.id = i.bill_id
                WHERE ses.term_id = (SELECT max(term_id) FROM parliament.mandate)
                """;
            figures.BillsDiscussed.ShouldBe((int)(await Database.ScalarAsync<long>($"SELECT count(*) FROM ({Discussed}) d"))!);
            const string Shown =
                $"""
                SELECT r.bill_number, CASE WHEN r.status = 'closed' THEN coalesce(r.closed_for, 0) + coalesce(r.closed_against, 0)
                       ELSE (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id) END AS n
                FROM appeals.bill_record r WHERE r.status IN ('open', 'closed') AND r.bill_number IN ({Discussed})
                """;
            figures.Positions.ShouldBe((int)(await Database.ScalarAsync<long>($"SELECT coalesce(sum(n), 0)::bigint FROM ({Shown}) x"))!);
            figures.BillsTakenPart.ShouldBe((int)(await Database.ScalarAsync<long>($"SELECT count(*) FROM ({Shown}) x WHERE n > 0"))!);
            figures.Positions.ShouldBeGreaterThanOrEqualTo(1, "the member's own side is counted");

            // Sitting MPs of the current term, and those of them whose profile is taken over (design 8.5.2).
            const string Sitting =
                """
                SELECT p.seimas_person_id FROM parliament.mandate m JOIN parliament.person p ON p.id = m.person_id
                WHERE m.term_id = (SELECT max(term_id) FROM parliament.mandate) AND (m."to" IS NULL OR m."to" >= current_date)
                """;
            figures.MpsSitting.ShouldBe((int)(await Database.ScalarAsync<long>($"SELECT count(*) FROM ({Sitting}) s"))!);
            figures.MpsTakenOver.ShouldBe((int)(await Database.ScalarAsync<long>(
                $"SELECT count(DISTINCT seimas_person_id) FROM identity.politician_link WHERE part <> 'office' AND ended_at IS NULL AND seimas_person_id IN ({Sitting})"))!);

            // Faction unity, faction by faction, from the stored positions: counts always, no share under 20, no mixed group.
            var unity = (await visitor.GetJsonAsync<AnalyticsResponse>(AnalyticsRoutes.Analytics)).Factions;
            var expected = await FactionCountsAsync();
            unity.Select(f => f.Slug).ShouldBe(expected.Keys, ignoreOrder: true, "every faction with a recorded vote, and no mixed group");
            foreach (var faction in unity)
            {
                (faction.VotesWithPosition, faction.FreeVotes).ShouldBe(expected[faction.Slug], faction.Slug);
                faction.SmallSample.ShouldBe(faction.VotesWithPosition < 20, faction.Slug);
                if (faction.SmallSample)
                {
                    faction.Share.ShouldBeNull($"{faction.Slug}: no share under twenty votes with a position");
                }
            }

            unity.Select(f => f.Seats).ShouldBeInOrder(SortDirection.Descending, "by seats, not by unity");
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/statistika"));
            foreach (var faction in unity.Where(f => f.Share is null))
            {
                Regex.Match(page, $"<li[^>]*id=\"unity-{faction.Slug}\".*?</li>", RegexOptions.Singleline).Value.ShouldNotContain("<svg class=\"meter\"", customMessage: faction.Slug);
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
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

            (await visitor.Client.GetStringAsync("/statistika")).ShouldContain($"id=\"dot-{Bill}\"");
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
        await Database.PrepareBillAsync(member, moderator.Browser, Bill, "against");
        try
        {
            // Before the bill closes there is nothing to compare.
            var before = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (before.Bills, before.Mps.Count, before.Factions.Count).ShouldBe((0, 0, 0));

            await CloseAsync();
            (await visitor.Client.GetAsync(AnalyticsRoutes.MyAgreement)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var mine = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (mine.Bills, mine.Consensus, mine.SmallSample).ShouldBe((1, 0, 20), "the copy is made again once the bill closes");
            var ballots = await BallotsAsync(FinalVote);
            var voted = ballots.Where(b => b.Choice is "For" or "Against" or "Abstain").ToList();
            voted.ShouldNotBeEmpty("the invented vote has ballots");
            mine.Mps.Select(m => m.Slug).ShouldBe(voted.Select(b => b.Slug), ignoreOrder: true, "every MP who voted, and nobody who did not");
            foreach (var mp in mine.Mps)
            {
                var choice = voted.Single(b => b.Slug == mp.Slug).Choice;
                (mp.Matched, mp.Common, mp.Share).ShouldBe((choice == "Against" ? 1 : 0, 1, (decimal?)null), $"{mp.Slug} voted {choice}, the member asked against");
            }

            // One common vote is a small sample for everyone: listed by last name, then first name, as every list of MPs.
            var byName = StringComparer.Create(CultureInfo.GetCultureInfo("lt-LT"), ignoreCase: true);
            var names = await MpNamesAsync(mine.Mps.Select(m => m.Slug));
            mine.Mps.Select(m => m.Slug).ShouldBe(mine.Mps.Select(m => m.Slug).OrderBy(s => names[s].Last, byName).ThenBy(s => names[s].First, byName));

            // Factions: matched when the faction's position (two thirds) is the member's side; a free vote is apart.
            var positions = await FactionPositionsAsync(FinalVote);
            mine.Factions.Select(f => f.Slug).ShouldBe(
                positions.Where(p => p.Value is "For" or "Against" or "Abstain").Select(p => p.Key), ignoreOrder: true, "a faction that voted freely has no common vote and is not listed");
            foreach (var faction in mine.Factions)
            {
                var position = positions[faction.Slug];
                var common = position is "For" or "Against" or "Abstain" ? 1 : 0;
                (faction.Matched, faction.Common, faction.Free).ShouldBe((position == "Against" ? 1 : 0, common, position == "Free" ? 1 : 0), $"{faction.Slug}: {position}");
                faction.Seats.ShouldNotBeNull();
            }

            // Another member, with no side on the closed bill, gets nothing: the answer is the caller's own.
            var theirs = await other.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (theirs.Bills, theirs.Mps.Count, theirs.Factions.Count).ShouldBe((0, 0, 0));

            // Never cached by HTTP, and no page the server renders holds any of it, the island's place included.
            using var response = await member.Client.GetAsync(AnalyticsRoutes.MyAgreement);
            response.Headers.CacheControl?.NoStore.ShouldBe(true);
            mine.Mps.ShouldNotBeEmpty("the answer the page is checked against holds rows");
            var page = await member.Client.GetStringAsync("/statistika");
            page.ShouldContain("id=\"kas-balsuoja-kaip-jus\"");
            foreach (var marker in new[] { "mine-mp-", "mine-faction-", "mine-basis", "id=\"mine\"", "mine-sign-in" })
            {
                page.ShouldNotContain(marker, customMessage: "the member's part is filled only in the browser");
            }

            foreach (var mp in mine.Mps)
            {
                page.ShouldNotContain("/p/" + mp.Slug, customMessage: "no MP of the member's comparison is in the server's HTML");
            }

            // A visitor gets the sign-in link in the server's HTML, so it works without JavaScript.
            (await visitor.Client.GetStringAsync("/statistika")).ShouldContain("id=\"mine-sign-in\"");

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
    public async Task From_twenty_common_votes_the_member_sees_a_share_highest_first_and_a_former_mp_is_marked()
    {
        using var moderator = await ModeratorAsync();
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        await Database.PrepareBillAsync(member, moderator.Browser, Bill, "for");
        var memberId = await Database.UserIdAsync(email);
        var mandate = await Database.ScalarAsync<DateOnly?>(
            "SELECT \"to\" FROM parliament.mandate WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("p", MpSteps.Eglaite.SeimasPersonId));
        try
        {
            await CloseAsync();

            // Nineteen more closed records closed by the same vote, each with the member's side "for": twenty common votes
            // with every MP who voted (records stand for bills; nothing else reads these invented numbers).
            await Database.ExecuteAsync(
                """
                WITH made AS (
                  INSERT INTO appeals.bill_record (id, bill_number, description, status, opened_at, published_at, closed_at, changed_at, sending,
                                                   threshold_set_by, closing_vote_id, closed_for, closed_for_older_than30days, closed_against, closed_against_older_than30days)
                  SELECT gen_random_uuid(), 'XVP-T' || lpad(n::text, 3, '0'), '', 'closed', now(), now(), now(), now(), 'automatic', 'default', @vote, 1, 0, 0, 0
                  FROM generate_series(1, 19) AS n RETURNING id)
                INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since)
                SELECT id, @u, 'for', now(), now(), now() FROM made
                """,
                ("vote", FinalVote), ("u", memberId));

            // A former MP: the mandate of one who voted ended yesterday (the copy is emptied: the import is not what changed it).
            await Database.ExecuteAsync(
                "UPDATE parliament.mandate SET \"to\" = current_date - 1 WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("p", MpSteps.Eglaite.SeimasPersonId));
            FreshCopies();

            var mine = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            mine.Bills.ShouldBe(20);
            var withShare = mine.Mps.Where(m => m.Share is not null).ToList();
            withShare.ShouldNotBeEmpty("every MP who voted has twenty common votes");
            withShare.ShouldAllBe(m => m.Common == 20);
            withShare.Select(m => m.Share).ShouldBeInOrder(SortDirection.Descending, "highest agreement first");
            mine.Mps.Take(withShare.Count).ShouldBe(withShare, "those with a share come before the small samples");
            var eglaite = (await MpNamesAsync(mine.Mps.Select(m => m.Slug))).Single(n => n.Value.Last == "Eglaitė").Key;
            mine.Mps.Single(m => m.Slug == eglaite).Former.ShouldBeTrue();
            mine.Mps.Where(m => m.Slug != eglaite).ShouldAllBe(m => !m.Former);
        }
        finally
        {
            await Database.ExecuteAsync(
                "UPDATE parliament.mandate SET \"to\" = @to WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)",
                ("to", (object?)mandate ?? DBNull.Value), ("p", MpSteps.Eglaite.SeimasPersonId));
            await Database.ExecuteAsync("DELETE FROM appeals.bill_record WHERE bill_number LIKE @b", ("b", ExtraBills));
            await Database.FreeBillAsync(Bill);
            await RemoveFinalVoteAsync();
            await member.DeleteAccountAsync();
            FreshCopies();
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
            WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/statistika")).ShouldContain("pritarta bendru sutarimu");

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

    [Fact]
    public async Task The_members_kept_comparison_is_dropped_when_they_withdraw_the_positions_consent()
    {
        using var moderator = await ModeratorAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await Database.PrepareBillAsync(member, moderator.Browser, Bill, "for");
        try
        {
            await CloseAsync();
            (await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement)).Bills.ShouldBe(1);

            (await member.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.ApiRoutes.Me.PositionConsent)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var after = await member.GetJsonAsync<MyAgreementResponse>(AnalyticsRoutes.MyAgreement);
            (after.Bills, after.Mps.Count).ShouldBe((0, 0), "the sides are gone, and so is the kept copy made from them");
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
    private async Task<IReadOnlyList<(string Slug, string Choice)>> BallotsAsync(long vote) =>
        await PairsAsync("SELECT coalesce(json_agg(json_build_array(p.slug, b.choice)), '[]')::text FROM parliament.ballot b JOIN parliament.person p ON p.id = b.person_id WHERE b.vote_id = @v", vote);

    /// <summary>Each faction's position at a vote, by the faction's slug, labels of one faction added up as the rule does.</summary>
    private async Task<IReadOnlyDictionary<string, string>> FactionPositionsAsync(long vote)
    {
        var rows = await PairsAsync(
            """
            SELECT coalesce(json_agg(json_build_array(b.slug, x.f || ',' || x.a || ',' || x.ab)), '[]')::text
            FROM (SELECT faction_id, sum("for") AS f, sum(against) AS a, sum(abstain) AS ab FROM parliament.faction_position WHERE vote_id = @v GROUP BY faction_id) x
            JOIN parliament.body b ON b.id = x.faction_id WHERE b.kind = 'Faction'
            """,
            vote);
        return rows.ToDictionary(r => r.A, r =>
        {
            var n = r.B.Split(',').Select(int.Parse).ToArray();
            var voted = n[0] + n[1] + n[2];
            return voted == 0 ? "NobodyVoted" : 3 * n[0] >= 2 * voted ? "For" : 3 * n[1] >= 2 * voted ? "Against" : 3 * n[2] >= 2 * voted ? "Abstain" : "Free";
        });
    }

    /// <summary>Per faction of the current term: recorded votes with a position and free votes, labels of one faction added up.</summary>
    private async Task<IReadOnlyDictionary<string, (int WithPosition, int Free)>> FactionCountsAsync()
    {
        var rows = await PairsAsync(
            """
            SELECT coalesce(json_agg(json_build_array(b.slug, x.f || ',' || x.a || ',' || x.ab)), '[]')::text
            FROM (SELECT fp.faction_id, fp.vote_id, sum(fp."for") AS f, sum(fp.against) AS a, sum(fp.abstain) AS ab
                  FROM parliament.faction_position fp JOIN parliament.vote v ON v.id = fp.vote_id
                  JOIN parliament.agenda_item i ON i.id = v.agenda_item_id JOIN parliament.sitting s ON s.id = i.sitting_id
                  JOIN parliament.session ses ON ses.id = s.session_id
                  WHERE v.has_result AND NOT v.is_consensus AND ses.term_id = (SELECT max(term_id) FROM parliament.mandate) AND v.id <> @v
                  GROUP BY fp.faction_id, fp.vote_id) x
            JOIN parliament.body b ON b.id = x.faction_id WHERE b.kind = 'Faction'
            """,
            FinalVote);
        return rows.GroupBy(r => r.A).ToDictionary(g => g.Key, g =>
        {
            int with = 0, free = 0;
            foreach (var (_, counts) in g)
            {
                var n = counts.Split(',').Select(int.Parse).ToArray();
                var voted = n[0] + n[1] + n[2];
                if (voted == 0)
                {
                    continue;
                }

                if (3 * n[0] >= 2 * voted || 3 * n[1] >= 2 * voted || 3 * n[2] >= 2 * voted)
                {
                    with++;
                }
                else
                {
                    free++;
                }
            }

            return (with, free);
        });
    }

    private async Task<IReadOnlyDictionary<string, (string First, string Last)>> MpNamesAsync(IEnumerable<string> slugs)
    {
        var wanted = slugs.ToHashSet(StringComparer.Ordinal);
        var rows = await PairsAsync("SELECT coalesce(json_agg(json_build_array(slug, first_name || '|' || last_name)), '[]')::text FROM parliament.person WHERE @v = @v", 0);
        return rows.Where(r => wanted.Contains(r.A)).ToDictionary(r => r.A, r => (r.B.Split('|')[0], r.B.Split('|')[1]), StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<(string A, string B)>> PairsAsync(string sql, long vote)
    {
        var json = await Database.ScalarAsync<string>(sql, ("v", vote));
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.EnumerateArray().Select(e => (e[0].GetString()!, e[1].GetString()!)).ToList();
    }
}
