using System.Net;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Lt2.IntegrationTests;

/// <summary>
/// The migrator's <c>--demo-activity</c> (design 8.26.5): invented members' activity on the trial's real Seimas data, refused
/// unless the configuration allows it and every email is caught, and unless the parliament data is real only; a bill a real
/// account took part on is left as it is, and every page says that members' activity is invented.
/// </summary>
public sealed class DemoActivityTests(DatabaseFixture database) : ApiTest(database)
{
    private const string MembersNotice = "Naudotojų pozicijos, argumentai, komentarai, organizacijos ir suvestinės čia išgalvoti";

    private const string ParliamentNotice = "Seimo narių ir balsavimų duomenys čia išgalvoti";

    private static IConfiguration Configuration(bool allowed, bool caught) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Allowed"] = allowed ? "true" : null,
            ["Email:Caught"] = caught ? "true" : null,
        }).Build();

    [Fact]
    public void The_trials_bills_keep_the_rules_and_the_same_pool_gives_the_same_activity()
    {
        var now = new DateTimeOffset(2026, 11, 26, 6, 0, 0, TimeSpan.Zero);
        var members = DemoActivity.Pool(now);
        members.Count.ShouldBe(DemoActivity.Members);
        members.Select(m => m.Id).ShouldBeUnique();
        members.Select(m => m.Email).ShouldBeUnique();
        members.ShouldAllBe(m => m.Email.StartsWith("demo.bandymas.", StringComparison.Ordinal) && m.Email.EndsWith("@pavyzdys.test", StringComparison.Ordinal));
        members.ShouldAllBe(m => m.Id.ToString().StartsWith(InventedActivity.IdPrefix, StringComparison.Ordinal) && m.MemberSince < now);
        members.Count(m => m.MemberSince > now.AddDays(-30)).ShouldBeInRange(250, 550, "about one in eight joined in the last 30 days");

        DemoActivityBills.Open.Select(b => b.Number).ShouldBeUnique();
        DemoActivityBills.Open.ShouldAllBe(b => !DemoActivityBills.Excluded.Contains(b.Number));
        DemoActivityBills.Excluded.ShouldContain("XVP-1322");
        DemoActivityBills.Organisations(DemoActivity.MemberId).ShouldAllBe(o => DemoActivityBills.Open.Any(b => b.Number == o.Statement.BillNumber));

        var named = members.Where(m => m.DisplayName is not null).ToDictionary(m => m.DisplayName!, m => m.Id);
        var pool = members.Select(m => KeyValuePair.Create(m.Id, m.MemberSince)).ToList();
        foreach (var bill in DemoActivityBills.Open)
        {
            var spec = DemoActivity.BuildOpen(bill, pool, named, now);
            spec.Positions.Count.ShouldBe(bill.Positions, bill.Number);
            spec.Positions.Select(p => p.UserId).ShouldBeUnique();
            spec.Positions.Count(p => p.Side == Sides.For).ShouldBe((int)Math.Round(bill.Positions * bill.ForShare), bill.Number);
            spec.Positions.ShouldAllBe(p => p.TakenAt >= spec.OpenedAt && p.TakenAt >= p.MemberSince && p.TakenAt <= now);
            spec.OpenedAt.ShouldBeInRange(now.AddDays(-76), now.AddDays(-17));

            var sideOf = spec.Positions.ToDictionary(p => p.UserId, p => p.Side);
            var takenAt = spec.Positions.ToDictionary(p => p.UserId, p => p.TakenAt!.Value);
            foreach (var argument in spec.Arguments)
            {
                sideOf[argument.AuthorUserId].ShouldBe(argument.Side, "an author writes for their own side");
                argument.At.ShouldBeGreaterThan(takenAt[argument.AuthorUserId]);
                argument.At.ShouldBeLessThan(now);
                argument.Text.Length.ShouldBeLessThanOrEqualTo(SettingKeys.TextsMaxLength.Default);
                argument.Marks.ShouldAllBe(m => sideOf[m.UserId] == argument.Side && m.UserId != argument.AuthorUserId && m.At >= argument.At && m.At <= now,
                    "Man svarbu only on one's own side, never one's own argument, after it was written");
            }

            spec.Comments.ShouldAllBe(c => c.Argument < spec.Arguments.Count && c.At <= now && c.At > spec.Arguments[c.Argument].At);
            DemoActivity.BuildOpen(bill, pool, named, now).Positions.ShouldBe(spec.Positions, "the same pool gives the same sides");
        }
    }

    [Fact]
    public void A_finished_bills_sides_come_before_its_vote_and_most_agree_with_the_seimas()
    {
        var now = new DateTimeOffset(2026, 11, 26, 6, 0, 0, TimeSpan.Zero);
        var pool = DemoActivity.Pool(now).Select(m => KeyValuePair.Create(m.Id, m.MemberSince)).ToList();
        var votes = Enumerable.Range(1, 120)
            .Select(n => new Lt2.Parliament.AdoptionVote($"XVP-{1000 + n}", "Bandomasis", n, now.AddDays(-n * 2), new SeimasVoteNumbers(false, 80, n % 4 == 0 ? 20 : 1, 5)))
            .ToList();
        var picked = DemoActivity.PickClosed(votes);
        picked.Count.ShouldBe(DemoActivity.ClosedBills);
        picked.Count(v => v.Numbers.Against >= 5).ShouldBeGreaterThanOrEqualTo(DemoActivity.ClosedBills / 3);
        DemoActivity.PickClosed(votes).ShouldBe(picked, "the same votes pick the same bills");

        var agreeing = 0;
        foreach (var vote in picked)
        {
            var spec = DemoActivity.BuildClosed(vote, seimasFor: true, pool);
            spec.Positions.Count.ShouldBeInRange(7, 2200);
            spec.Positions.Select(p => p.UserId).ShouldBeUnique();
            spec.Positions.ShouldAllBe(p => p.TakenAt < vote.HeldAt.AddHours(-1) && p.TakenAt >= p.MemberSince && p.TakenAt >= spec.OpenedAt);
            var forCount = spec.Positions.Count(p => p.Side == Sides.For);
            var membersFor = AnalyticsRules.MembersMajorityFor(forCount, spec.Positions.Count - forCount);
            membersFor.ShouldNotBeNull("no tie");
            agreeing += membersFor == true ? 1 : 0;
            spec.Arguments.ShouldBeEmpty();
        }

        agreeing.ShouldBeInRange(DemoActivity.ClosedBills / 2, DemoActivity.ClosedBills - 2, "most agree with the Seimas, some do not");
    }

    [Fact]
    public async Task It_refuses_unless_the_configuration_allows_it_and_every_email_is_caught()
    {
        foreach (var (allowed, caught) in new[] { (false, false), (true, false), (false, true) })
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                DemoActivity.RunAsync(Configuration(allowed, caught), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None));
            error.Message.ShouldContain("Email:Caught");
        }
    }

    [Fact]
    public async Task It_refuses_on_the_stand_ins_invented_data()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            DemoActivity.RunAsync(Configuration(true, true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None));
        error.Message.ShouldContain("--demo-appeal");
    }

    [Fact]
    public async Task On_real_data_it_writes_once_leaves_a_bill_a_real_account_took_part_on_and_every_page_says_so()
    {
        const string Free = AppealSteps.TaxBill;
        const string Taken = DemoAppeal.AiBill;
        var bills = new List<DemoActivityBills.Bill>
        {
            new(Free, 150, 0.6,
            [
                new(Sides.For, "Bandomasis argumentas už.", 0.3, "Bandomoji Autorė"),
                new(Sides.Against, "Bandomasis argumentas prieš.", 0.4, "Bandomasis Autorius"),
            ],
            [new(0, "Bandomasis Komentatorius", "Bandomasis komentaras.", 3)]),
            new(Taken, 120, 0.5, []),
            new("XVP-9999", 50, 0.5, []),
        };
        var scope = new DemoActivity.Scope(bills, ClosedBills: 0, PastSummaries: 0);
        await Database.FreeBillAsync(Free);
        await Database.FreeBillAsync(Taken);

        // A real account's side on the second bill: its record is left as it is.
        var realRecord = Guid.NewGuid();
        await Database.ExecuteAsync(
            """
            INSERT INTO appeals.bill_record (id, bill_number, description, status, opened_at, published_at, sending, threshold_set_by, changed_at)
            VALUES (@id, @bill, '', 'open', now(), now(), 'automatic', 'default', now());
            INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since) VALUES (@id, @user, 'for', now(), now(), now() - interval '90 days');
            """,
            ("id", realRecord), ("bill", Taken), ("user", Guid.NewGuid()));
        await RealDataAsync(true);
        try
        {
            var first = await DemoActivity.RunAsync(Configuration(true, true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None, scope);
            first.AccountsCreated.ShouldBe(DemoActivity.Members + 3);
            (first.OpenWritten, first.OpenAlreadyThere, first.OpenLeft, first.OpenNotFound).ShouldBe((1, 0, 1, 1));
            first.DailyRows.ShouldBeGreaterThan(17, "a row for every day since the record opened");

            var again = await DemoActivity.RunAsync(Configuration(true, true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None, scope);
            (again.AccountsCreated, again.OpenWritten, again.OpenAlreadyThere, again.OpenLeft).ShouldBe((0, 0, 1, 1), "the second run writes nothing twice");

            using var stranger = NewBrowser();
            var record = await stranger.RecordAsync(Free);
            record.Counts.For.ShouldBe(90);
            record.Counts.Against.ShouldBe(60);
            record.Description.ShouldBeNullOrEmpty("a real bill's description is never invented");
            record.Reasons.Select(r => r.AuthorName).ShouldBe(["Bandomoji Autorė", "Bandomasis Autorius"]);
            record.Reasons.ShouldAllBe(r => r.Endorsements > 0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE bill_number = @b", ("b", Free))).ShouldBe(1);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE record_id = @r", ("r", realRecord))).ShouldBe(1, "the real account's bill is untouched");

            // Every page says that members' activity is invented, read from the accounts (a host of its own: the answer is cached).
            await using var factory = new Lt2Factory(Database);
            using var browser = factory.NewBrowser();
            foreach (var path in new[] { "/", "/prisijungti", "/seimas/" + Free })
            {
                using var response = await browser.Client.GetAsync(path);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                html.ShouldContain(MembersNotice, customMessage: path);
                html.ShouldNotContain(ParliamentNotice, customMessage: path);
            }
        }
        finally
        {
            await RealDataAsync(false);
            await Database.FreeBillAsync(Free);
            await Database.FreeBillAsync(Taken);
            await Database.ExecuteAsync("DELETE FROM identity.\"user\" WHERE email LIKE 'demo.bandymas.%@pavyzdys.test'");
        }
    }

    [Fact]
    public async Task An_invented_past_summary_is_a_dry_run_with_a_delivery_per_mp_and_is_written_once_per_week()
    {
        var monday = new DateOnly(2020, 1, 6);
        var at = VilniusTime.StartOf(monday).AddHours(7);
        var run = new InventedDigestRun(Guid.NewGuid(), monday, at,
        [
            new InventedDigestItem(Guid.NewGuid(), "XVP-0412", "Bandomasis projektas", 120, 100, 80, 70, 3, 1,
                [new DigestReason(Sides.For, "Bandomasis argumentas.", 12)]),
        ]);
        try
        {
            (await InventedDigests.WriteDryRunsAsync(Database.MigratorConnectionString, [run], [("jonas-petraitis", 1), ("ona-jonaitiene", 2)], CancellationToken.None)).ShouldBe(1);
            (await InventedDigests.WriteDryRunsAsync(Database.MigratorConnectionString, [run with { Id = Guid.NewGuid() }], [("jonas-petraitis", 1)], CancellationToken.None))
                .ShouldBe(0, "a week with a summary is left as it is");
            (await Database.ScalarAsync<string>("SELECT status FROM notify.digest_run WHERE id = @id", ("id", run.Id))).ShouldBe("dry-run");
            (await Database.ScalarAsync<int>("SELECT mp_count FROM notify.digest_run WHERE id = @id", ("id", run.Id))).ShouldBe(2);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE run_id = @id AND status = 'dry-run'", ("id", run.Id))).ShouldBe(2);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item WHERE run_id = @id", ("id", run.Id))).ShouldBe(1);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE created_at >= @at AND created_at < @to", ("at", at.AddDays(-1)), ("to", at.AddDays(1))))
                .ShouldBe(0, "nothing to send");

            // Its public copy shows it as any dry run, with the bill and the argument as frozen.
            using var stranger = NewBrowser();
            using var page = await stranger.Client.GetAsync("/suvestines/2020-01-06");
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            html.ShouldContain("Bandomasis projektas");
            html.ShouldContain("Bandomasis argumentas.");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM notify.digest_run WHERE run_date = @d", ("d", monday));
        }
    }

    private Task RealDataAsync(bool real) => Database.ExecuteAsync(
        $"""
        UPDATE parliament.import_run SET synthetic = {(!real ? "true" : "false")};
        UPDATE parliament.raw_document SET synthetic = {(!real ? "true" : "false")};
        UPDATE parliament.agenda_snapshot SET synthetic = {(!real ? "true" : "false")};
        """);
}
