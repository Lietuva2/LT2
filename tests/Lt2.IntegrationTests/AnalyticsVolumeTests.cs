using System.Diagnostics;
using System.Globalization;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.26.6: how long one full count of the analytics takes at a real term's size, for the owner's question whether
/// a periodic job should make the copy. Not a check: a measurement, run only with <c>LT2_ANALYTICS_VOLUME=1</c>. It adds a
/// term's worth of invented rows to the test database (141 MPs in 7 factions, 500 sittings, 5 000 votes with every MP's
/// ballot, 800 bills of which 300 closed, 100 times the trial's 11 080 positions) and deletes them all at the end. The
/// timings go to the test's output and to <c>analytics-volume.txt</c> beside the test assembly.
/// </summary>
public sealed class AnalyticsVolumeTests(DatabaseFixture database) : ApiTest(database)
{
    private const int Positions = 1_108_000;
    private static readonly Guid Member = new("0193b000-9999-7000-8000-000000000001");

    [Fact]
    public async Task One_full_count_at_a_real_terms_size_takes()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LT2_ANALYTICS_VOLUME") == "1", "A measurement: set LT2_ANALYTICS_VOLUME=1 to run it.");
        var lines = new List<string>();
        try
        {
            lines.Add("trial-size data (the test database as it is): " + await TimeAsync());
            var generated = Stopwatch.StartNew();
            await GenerateAsync();
            lines.Add($"generated in {generated.Elapsed.TotalSeconds:0.0} s: " + await Database.ScalarAsync<string>(
                """
                SELECT format('%s positions, %s ballots, %s faction positions, %s votes, %s records (%s closed)',
                  (SELECT count(*) FROM appeals.position), (SELECT count(*) FROM parliament.ballot), (SELECT count(*) FROM parliament.faction_position),
                  (SELECT count(*) FROM parliament.vote), (SELECT count(*) FROM appeals.bill_record), (SELECT count(*) FROM appeals.bill_record WHERE status = 'closed'))
                """));
            await Database.ExecuteAsync("ANALYZE");
            lines.Add("a real term's size: " + await TimeAsync());
        }
        finally
        {
            await RemoveAsync();
            var text = string.Join(Environment.NewLine, lines);
            TestContext.Current.TestOutputHelper?.WriteLine(text);
            await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "analytics-volume.txt"), text);
        }
    }

    /// <summary>The public count for the whole term, a year and the older accounts, and a member's comparison; each three times.</summary>
    private async Task<string> TimeAsync()
    {
        var copies = Factory.Services.GetRequiredService<AnalyticsCopies>();
        var results = new List<string>();
        foreach (var (name, year, older) in new (string, int?, bool)[] { ("term", null, false), ("2026", 2026, false), ("term, older", null, true) })
        {
            var times = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var watch = Stopwatch.StartNew();
                await scope.ServiceProvider.GetRequiredService<AnalyticsReads>().BuildAsync(year, older, CancellationToken.None);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }

            results.Add($"public {name}: {string.Join(" / ", times.Select(t => t.ToString("0", CultureInfo.InvariantCulture)))} ms");
        }

        var mine = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            copies.Clear();
            await using var scope = Factory.Services.CreateAsyncScope();
            var watch = Stopwatch.StartNew();
            var answer = await scope.ServiceProvider.GetRequiredService<MyAgreementReads>().ReadAsync(Member, CancellationToken.None);
            mine.Add(watch.Elapsed.TotalMilliseconds);
            if (i == 0)
            {
                results.Add($"member with sides on {answer.Bills} closed bills ({answer.Mps.Count} MPs listed)");
            }
        }

        results.Add($"member's comparison: {string.Join(" / ", mine.Select(t => t.ToString("0", CultureInfo.InvariantCulture)))} ms");
        return string.Join("; ", results);
    }

    private async Task GenerateAsync()
    {
        // The current term, its first day, and 127 invented MPs beside the stand-in's, all sitting, in 7 factions.
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.person (id, seimas_person_id, first_name, last_name, slug)
            SELECT gen_random_uuid(), 98000000 + n, 'Vardas' || n, 'Pavarde' || n, 'vol-mp-' || n FROM generate_series(1, 127) n;
            INSERT INTO parliament.mandate (id, person_id, term_id, "from")
            SELECT gen_random_uuid(), p.id, (SELECT max(term_id) FROM parliament.mandate), (SELECT "from" FROM parliament.term WHERE id = (SELECT max(term_id) FROM parliament.mandate)) FROM parliament.person p WHERE p.seimas_person_id BETWEEN 98000001 AND 98000127;
            INSERT INTO parliament.body (id, seimas_id, term_id, kind, name, slug)
            SELECT gen_random_uuid(), 98100000 + n, (SELECT max(term_id) FROM parliament.mandate), 'Faction', 'Matavimo frakcija ' || n, 'vol-frakcija-' || n FROM generate_series(1, 7) n;
            INSERT INTO parliament.membership (id, person_id, body_id, role, "from")
            SELECT gen_random_uuid(), p.id, (SELECT id FROM parliament.body WHERE seimas_id = 98100001 + (p.seimas_person_id % 7)), 'narys', (SELECT "from" FROM parliament.term WHERE id = (SELECT max(term_id) FROM parliament.mandate))
            FROM parliament.person p WHERE p.seimas_person_id BETWEEN 98000001 AND 98000127;
            INSERT INTO parliament.session (id, term_id, name, "from") SELECT 98000001, id, 'Matavimo sesija', "from" FROM parliament.term WHERE id = (SELECT max(term_id) FROM parliament.mandate);
            """);

        // 500 sittings from the term's start to yesterday, ten agenda items each on 800 bills, one vote per item.
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.sitting (id, session_id, starts_at)
            SELECT 98010000 + n, 98000001, (SELECT "from" FROM parliament.term WHERE id = (SELECT max(term_id) FROM parliament.mandate))::timestamptz + ((current_date - (SELECT "from" FROM parliament.term WHERE id = (SELECT max(term_id) FROM parliament.mandate)) - 1) * n / 500.0) * interval '1 day' + interval '8 hours'
            FROM generate_series(1, 500) n;
            INSERT INTO parliament.bill (id, number, title) SELECT gen_random_uuid(), 'XVP-V' || lpad(n::text, 4, '0'), 'Matavimo projektas ' || n FROM generate_series(1, 800) n;
            INSERT INTO parliament.agenda_item (id, sitting_id, bill_id, number, title)
            SELECT 98100000 + n, 98010001 + (n - 1) / 10, (SELECT id FROM parliament.bill WHERE number = 'XVP-V' || lpad((1 + (n - 1) % 800)::text, 4, '0')), (1 + (n - 1) % 10)::text, 'Matavimo projektas'
            FROM generate_series(1, 5000) n;
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT 98200000 + i.id - 98100000, i.id, i.bill_id, s.starts_at + interval '1 hour', 'Pritarti', 'Atviras', 'StageResult',
                   CASE WHEN i.id - 98100000 > 4200 THEN 'Adoption' ELSE 'Consideration' END, false, true, 60 + (i.id % 30), 20 + (i.id % 25), 5 + (i.id % 10), 110, 141
            FROM parliament.agenda_item i JOIN parliament.sitting s ON s.id = i.sitting_id WHERE i.id BETWEEN 98100001 AND 98105000;
            """);

        // Every MP's ballot at every vote, and every faction's position by the two-thirds rule.
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.ballot (vote_id, person_id, faction_label, faction_id, choice)
            SELECT v.id, p.id, NULL, NULL, (ARRAY['For','For','Against','Abstain','DidNotVote'])[1 + ((v.id + p.seimas_person_id) % 5)]
            FROM parliament.vote v CROSS JOIN (SELECT id, seimas_person_id FROM parliament.person ORDER BY seimas_person_id LIMIT 141) p WHERE v.id BETWEEN 98200001 AND 98205000;
            INSERT INTO parliament.faction_position (vote_id, faction_label, faction_id, position, "for", against, abstain, did_not_vote)
            SELECT v.id, b.slug, b.id,
                   CASE WHEN 3 * f >= 2 * (f + a + ab) THEN 'For' WHEN 3 * a >= 2 * (f + a + ab) THEN 'Against' ELSE 'Free' END, f, a, ab, 2
            FROM parliament.vote v CROSS JOIN parliament.body b
            CROSS JOIN LATERAL (SELECT 10 + ((v.id + b.seimas_id) % 9) AS f, (v.id * b.seimas_id) % 7 AS a, 1 AS ab) c
            WHERE v.id BETWEEN 98200001 AND 98205000 AND b.seimas_id BETWEEN 98100001 AND 98100007;
            """);

        // 800 records: the last 300 bills closed by their adoption vote with frozen counts; 100 times the trial's positions.
        await Database.ExecuteAsync(
            $"""
            INSERT INTO appeals.bill_record (id, bill_number, description, status, opened_at, published_at, changed_at, sending, threshold_set_by)
            SELECT gen_random_uuid(), 'XVP-V' || lpad(n::text, 4, '0'), '', 'open', now() - interval '60 days', now() - interval '60 days', now(), 'automatic', 'default'
            FROM generate_series(1, 800) n;
            INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since)
            SELECT r.id, gen_random_uuid(), CASE WHEN g % 3 = 0 THEN 'against' ELSE 'for' END, now() - interval '10 days', now() - interval '10 days',
                   now() - (g % 90) * interval '1 day'
            FROM generate_series(1, {Positions}) g
            JOIN (SELECT id, row_number() OVER (ORDER BY bill_number) AS k FROM appeals.bill_record WHERE bill_number LIKE 'XVP-V%') r ON r.k = 1 + (g % 800);
            INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since)
            SELECT id, @m, CASE WHEN bill_number < 'XVP-V0650' THEN 'for' ELSE 'against' END, now(), now(), now() FROM appeals.bill_record WHERE bill_number > 'XVP-V0500';
            UPDATE appeals.bill_record r SET status = 'closed', closed_at = now(),
              closing_vote_id = (SELECT max(v.id) FROM parliament.vote v JOIN parliament.bill b ON b.id = v.bill_id WHERE b.number = r.bill_number AND v.stage = 'Adoption'),
              closed_for = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'for'),
              closed_for_older_than30days = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'for' AND p.member_since < now() - interval '30 days'),
              closed_against = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'against'),
              closed_against_older_than30days = (SELECT count(*) FROM appeals.position p WHERE p.record_id = r.id AND p.side = 'against' AND p.member_since < now() - interval '30 days')
            WHERE r.bill_number > 'XVP-V0500';
            """,
            ("m", Member));
    }

    private async Task RemoveAsync()
    {
        await Database.ExecuteAsync(
            """
            DELETE FROM appeals.bill_record WHERE bill_number LIKE 'XVP-V%';
            DELETE FROM parliament.faction_position WHERE vote_id BETWEEN 98200001 AND 98205000;
            DELETE FROM parliament.ballot WHERE vote_id BETWEEN 98200001 AND 98205000;
            DELETE FROM parliament.vote WHERE id BETWEEN 98200001 AND 98205000;
            DELETE FROM parliament.agenda_item WHERE id BETWEEN 98100001 AND 98105000;
            DELETE FROM appeals.bill_card WHERE bill_number LIKE 'XVP-V%';
            DELETE FROM parliament.bill WHERE number LIKE 'XVP-V%';
            DELETE FROM parliament.sitting WHERE session_id = 98000001;
            DELETE FROM parliament.session WHERE id = 98000001;
            DELETE FROM parliament.membership WHERE body_id IN (SELECT id FROM parliament.body WHERE seimas_id BETWEEN 98100001 AND 98100007);
            DELETE FROM parliament.body WHERE seimas_id BETWEEN 98100001 AND 98100007;
            DELETE FROM parliament.mandate WHERE person_id IN (SELECT id FROM parliament.person WHERE seimas_person_id BETWEEN 98000001 AND 98000127);
            DELETE FROM parliament.person WHERE seimas_person_id BETWEEN 98000001 AND 98000127;
            """);
        Factory.Services.GetRequiredService<AnalyticsCopies>().Clear();
    }
}
