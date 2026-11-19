using Lt2.Api.Contracts.Appeals;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Lt2.IntegrationTests;

/// <summary>
/// The migrator's <c>--demo-appeal</c>: invented participants on XVP-0412 for the local stack, refused unless the
/// configuration allows it and the parliament data is the stand-in's.
/// </summary>
public sealed class DemoAppealTests(DatabaseFixture database) : ApiTest(database)
{
    private static IConfiguration Configuration(bool allowed) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Allowed"] = allowed ? "true" : null }).Build();

    [Fact]
    public void The_invented_record_has_the_prototypes_numbers_and_keeps_the_rules()
    {
        var (members, record) = DemoAppeal.Build(DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var forSide = record.Positions.Where(p => p.Side == "for").ToList();
        var againstSide = record.Positions.Where(p => p.Side == "against").ToList();

        forSide.Count.ShouldBe(6800);
        forSide.Count(p => p.MemberSince <= now.AddDays(-30)).ShouldBe(6150);
        againstSide.Count.ShouldBe(4280);
        againstSide.Count(p => p.MemberSince <= now.AddDays(-30)).ShouldBe(3900);
        members.Count.ShouldBe(record.Positions.Count);
        record.Positions.Select(p => p.UserId).Distinct().Count().ShouldBe(record.Positions.Count, "one position per person");
        members.ShouldAllBe(m => m.Email.EndsWith("@pavyzdys.test", StringComparison.Ordinal));
        record.Reasons.Select(r => r.EndorsedBy.Count).ShouldBe([1920, 1310, 1480, 970]);

        var sideOf = record.Positions.ToDictionary(p => p.UserId, p => p.Side);
        foreach (var reason in record.Reasons)
        {
            sideOf[reason.AuthorUserId].ShouldBe(reason.Side, "an author writes for their own side");
            reason.EndorsedBy.ShouldAllBe(u => sideOf[u] == reason.Side, "Man svarbu only on one's own side");
            reason.Text.Length.ShouldBeLessThanOrEqualTo(500);
        }

        record.Reasons.SelectMany(r => r.EndorsedBy).GroupBy(u => u).Max(g => g.Count()).ShouldBeLessThanOrEqualTo(3);
        record.Reasons.GroupBy(r => r.AuthorUserId).Max(g => g.Count()).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void The_invented_closed_bills_take_sides_only_from_accounts_registered_before_the_vote_with_the_majority_specified()
    {
        DemoClosedBills.Bills.Count.ShouldBe(30);
        DemoClosedBills.Bills.Select(b => b.Number).ShouldBeUnique();
        DemoClosedBills.Bills.ShouldAllBe(b => b.Number.StartsWith("XVP-07", StringComparison.Ordinal) && b.Number != DemoAppeal.ClosedBill);
        DemoClosedBills.Bills.Count(b => b.Positions < AnalyticsRules.SmallSample).ShouldBeInRange(2, 5, "a few small samples show \"maža imtis\"");

        var votedAt = new DateTimeOffset(2026, 6, 11, 8, 20, 0, TimeSpan.Zero);
        var random = new Random(1);
        var pool = Enumerable.Range(1, 6000)
            .Select(n => KeyValuePair.Create(new Guid($"0193b000-0412-7000-8000-{n:D12}"), votedAt.AddDays(200 - random.Next(0, 420))))
            .ToList();
        foreach (var bill in DemoClosedBills.Bills)
        {
            foreach (var seimasFor in new[] { true, false })
            {
                var record = DemoClosedBills.Build(bill, votedAt, seimasFor, pool);
                record.Positions.Count.ShouldBe(bill.Positions);
                record.Positions.Select(p => p.UserId).ShouldBeUnique();
                record.Positions.ShouldAllBe(p => p.TakenAt < votedAt.AddHours(-1) && p.TakenAt > p.MemberSince && p.TakenAt >= record.OpenedAt);
                record.Positions.ShouldAllBe(p => pool.Any(a => a.Key == p.UserId && a.Value == p.MemberSince), "only accounts of the pool, with their own registration");
                var forCount = record.Positions.Count(p => p.Side == "for");
                var membersFor = AnalyticsRules.MembersMajorityFor(forCount, record.Positions.Count - forCount);
                membersFor.ShouldNotBeNull("no tie");
                if (AnalyticsRules.IsEnough(bill.Positions))
                {
                    (membersFor == seimasFor).ShouldBe(bill.AgreesWithSeimas, bill.Number);
                }

                var sideOf = record.Positions.ToDictionary(p => p.UserId, p => p.Side);
                record.Reasons.ShouldAllBe(r => sideOf[r.AuthorUserId] == r.Side && r.EndorsedBy.All(u => sideOf[u] == r.Side && u != r.AuthorUserId));
                DemoClosedBills.Build(bill, votedAt, seimasFor, pool).Positions.ShouldBe(record.Positions, "the same pool gives the same sides");
            }
        }
    }

    /// <summary>
    /// Design 6.5 (owner, 2026-11-18: the positions consent's text covers "Man svarbu" too, a new version): invented accounts
    /// made before it give the text in force when the command runs again, so the demonstration's sides and marks rest on the
    /// consent in force and the closed bills still find them; one that withdrew the consent stays without it.
    /// </summary>
    [Fact]
    public async Task Invented_members_on_an_older_positions_consent_give_the_text_in_force_once()
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var members = Enumerable.Range(0, 3).Select(_ => new InventedMember(Guid.NewGuid(), $"sutikimas.{Guid.NewGuid():N}@pavyzdys.test", null, since)).ToList();
        var ids = members.Select(m => m.Id).ToArray();
        try
        {
            (await InventedMembers.CreateAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(3);
            (await InventedMembers.SinceAsync(Database.MigratorConnectionString, ids, CancellationToken.None)).Count.ShouldBe(3, "made with the text in force");
            (await InventedMembers.GivePositionConsentAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(0, "nothing to give");

            // An older local database: every consent on the earlier wording, and one of them withdrawn.
            await Database.ExecuteAsync("UPDATE identity.consent SET text_version = '2026-11-13' WHERE kind = 'positions' AND user_id = ANY(@u)", ("u", ids));
            await Database.ExecuteAsync("UPDATE identity.consent SET withdrawn_at = now() WHERE kind = 'positions' AND user_id = @u", ("u", ids[2]));
            (await InventedMembers.SinceAsync(Database.MigratorConnectionString, ids, CancellationToken.None)).ShouldBeEmpty("an older text is not the consent in force");

            (await InventedMembers.GivePositionConsentAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(2);
            (await InventedMembers.GivePositionConsentAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(0, "repeatable");
            (await InventedMembers.SinceAsync(Database.MigratorConnectionString, ids, CancellationToken.None)).Keys.ShouldBe([ids[0], ids[1]], ignoreOrder: true);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM identity.consent WHERE kind = 'positions' AND text_version = @v AND withdrawn_at IS NULL AND user_id = ANY(@u)",
                ("v", PositionConsent.TextVersion), ("u", ids))).ShouldBe(2);
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM identity.\"user\" WHERE id = ANY(@u)", ("u", ids));
        }
    }

    [Fact]
    public async Task It_refuses_unless_the_configuration_allows_it()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            DemoAppeal.RunAsync(Configuration(false), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None));
        error.Message.ShouldContain("Demo:Allowed");
    }

    [Fact]
    public async Task It_refuses_on_a_database_that_ever_imported_real_seimas_data()
    {
        // Independent of test order: another test may already have filled the demonstration record, so compare what is
        // there before and after the refused run rather than expecting nothing.
        const string Appeals = "SELECT count(*) FROM appeals.bill_record WHERE bill_number = 'XVP-0412' AND status <> 'rejected'";
        const string Invented = "SELECT count(*) FROM identity.\"user\" WHERE email LIKE '%@pavyzdys.test' AND email LIKE 'demo.%'";

        // The state that used to break this test when the filling test ran first: an open record on XVP-0412 already.
        var existing = Guid.NewGuid();
        if (await Database.ScalarAsync<long>(Appeals) == 0)
        {
            await Database.ExecuteAsync(
                """
                INSERT INTO appeals.bill_record (id, bill_number, description, status, opened_at, published_at, sending, threshold_set_by, changed_at)
                VALUES (@id, 'XVP-0412', '', 'open', now(), now(), 'automatic', 'default', now())
                """,
                ("id", existing));
        }

        var recordsBefore = await Database.ScalarAsync<long>(Appeals);
        recordsBefore.ShouldBeGreaterThan(0);
        var inventedBefore = await Database.ScalarAsync<long>(Invented);
        var run = await Database.ScalarAsync<Guid>("SELECT id FROM parliament.import_run ORDER BY started_at LIMIT 1");
        await Database.ExecuteAsync("UPDATE parliament.import_run SET synthetic = false WHERE id = @id", ("id", run));
        try
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                DemoAppeal.RunAsync(Configuration(true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None));
            error.Message.ShouldContain("stand-in");
            (await Database.ScalarAsync<long>(Appeals)).ShouldBe(recordsBefore, "the refused run wrote no record");
            (await Database.ScalarAsync<long>(Invented)).ShouldBe(inventedBefore, "and no invented account");
        }
        finally
        {
            await Database.ExecuteAsync("UPDATE parliament.import_run SET synthetic = true WHERE id = @id", ("id", run));
            await Database.ExecuteAsync("DELETE FROM appeals.bill_record WHERE id = @id", ("id", existing));
        }
    }

    [Fact]
    public async Task On_the_stand_ins_data_it_fills_the_record_once_and_the_page_shows_the_numbers()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        await Database.FreeBillAsync(DemoAppeal.AiBill);
        var first = await DemoAppeal.RunAsync(Configuration(true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None);
        first.Status.ShouldBe("created");
        first.AccountsCreated.ShouldBe(6800 + 4280);
        first.AiDescription.ShouldBe("created");
        first.Organisations.ShouldBe(3);

        var again = await DemoAppeal.RunAsync(Configuration(true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None);
        // The closed demonstration bill (design 8.26.5) is only in the stand-in's local overlay, which tests never load.
        first.ClosedBill.ShouldBe("not imported yet");
        again.ShouldBe(new DemoAppeal.Outcome(
            "already there", 0, "already there", 0, ClosedBill: "not imported yet",
            ClosedBills: "0 created and closed, 0 already there, 0 left as they are, 30 not imported yet; invented positions written: 0"));

        // Design 8.23.6: invented comments with "Sutinku" under an argument and a statement, once whatever the runs.
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE bill_number = @b", ("b", DemoAppeal.Bill))).ShouldBe(3);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment_agreement a JOIN appeals.comment c ON c.id = a.comment_id WHERE c.bill_number = @b", ("b", DemoAppeal.Bill))).ShouldBe(3);

        using var stranger = NewBrowser();

        // Design 8.13: the second bill has an invented AI summary nobody has read, with its label, address and day.
        var described = await stranger.RecordAsync(DemoAppeal.AiBill);
        described.Description.ShouldBe(DemoAppeal.AiDescription);
        described.DescriptionSource.ShouldBe("ai");
        described.DescriptionUrl.ShouldNotBeNull().ShouldStartWith("https://e-seimas.lrs.lt/");
        described.DescriptionAt.ShouldNotBeNull();
        described.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));
        var page = System.Net.WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + DemoAppeal.AiBill));
        page.ShouldContain("Sukurta DI · žmonių neperžiūrėta");
        await Database.FreeBillAsync(DemoAppeal.AiBill);

        var record = await stranger.RecordAsync(AppealSteps.TaxBill);
        record.Status.ShouldBe("open");
        record.DescriptionSource.ShouldBe("moderator");
        record.DescriptionAuthor.ShouldBeNull("the record has no initiator (design 8.12)");
        record.Counts.ShouldBe(new SideCountsResponse(6800, 6150, 4280, 3900));
        record.Threshold.ShouldBe(new ThresholdResponse(100, "default", null, 11080, true, false));
        record.Surge.ShouldBeNull("the invented sides are spread over the record's life, not a surge of new accounts");
        // Owner, 2026-11-19: each side's first two, "už" then "prieš", each by its counted marks.
        record.Reasons.Select(r => (r.AuthorName, r.Endorsements)).ShouldBe(
            [("Giedrė L.", 1920), ("Mindaugas R.", 1310), ("Rūta Mockutė", 1480), ("Aistė Kazlaitė", 970)]);
        record.ReasonTotals.ShouldBe(new ReasonTotalsResponse(2, 2));
    }
}
