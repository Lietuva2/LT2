using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
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
    public void The_invented_appeal_has_the_prototypes_numbers_and_keeps_the_rules()
    {
        var (members, appeal) = DemoAppeal.Build(DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var forSide = appeal.Positions.Where(p => p.Side == "for").ToList();
        var againstSide = appeal.Positions.Where(p => p.Side == "against").ToList();

        forSide.Count.ShouldBe(6800);
        forSide.Count(p => p.MemberSince <= now.AddDays(-30)).ShouldBe(6150);
        againstSide.Count.ShouldBe(4280);
        againstSide.Count(p => p.MemberSince <= now.AddDays(-30)).ShouldBe(3900);
        members.Count.ShouldBe(appeal.Positions.Count);
        appeal.Positions.Select(p => p.UserId).Distinct().Count().ShouldBe(appeal.Positions.Count, "one position per person");
        members.ShouldAllBe(m => m.Email.EndsWith("@pavyzdys.test", StringComparison.Ordinal));
        appeal.Reasons.Select(r => r.EndorsedBy.Count).ShouldBe([1920, 1310, 1480, 970]);

        var sideOf = appeal.Positions.ToDictionary(p => p.UserId, p => p.Side);
        foreach (var reason in appeal.Reasons)
        {
            sideOf[reason.AuthorUserId].ShouldBe(reason.Side, "an author writes for their own side");
            reason.EndorsedBy.ShouldAllBe(u => sideOf[u] == reason.Side, "Man svarbu only on one's own side");
            reason.Text.Length.ShouldBeLessThanOrEqualTo(500);
        }

        appeal.Reasons.SelectMany(r => r.EndorsedBy).GroupBy(u => u).Max(g => g.Count()).ShouldBeLessThanOrEqualTo(3);
        appeal.Reasons.GroupBy(r => r.AuthorUserId).Max(g => g.Count()).ShouldBeLessThanOrEqualTo(2);
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
        // Independent of test order: another test may already have filled the demonstration appeal, so compare what is
        // there before and after the refused run rather than expecting nothing.
        const string Appeals = "SELECT count(*) FROM appeals.appeal WHERE bill_number = 'XVP-0412' AND status <> 'rejected'";
        const string Invented = "SELECT count(*) FROM identity.\"user\" WHERE email LIKE '%@pavyzdys.test' AND email LIKE 'demo.%'";

        // The state that used to break this test when the filling test ran first: an open appeal on XVP-0412 already.
        var existing = Guid.NewGuid();
        if (await Database.ScalarAsync<long>(Appeals) == 0)
        {
            await Database.ExecuteAsync(
                """
                INSERT INTO appeals.appeal (id, bill_number, initiator_user_id, description, status, opened_at, published_at, sending, threshold, threshold_set_by, screening_flags, changed_at)
                VALUES (@id, 'XVP-0412', @id, 'Jau esantis kreipimasis.', 'open', now(), now(), 'automatic', 100, 'initiator', '{}', now())
                """,
                ("id", existing));
        }

        var appealsBefore = await Database.ScalarAsync<long>(Appeals);
        appealsBefore.ShouldBeGreaterThan(0);
        var inventedBefore = await Database.ScalarAsync<long>(Invented);
        var run = await Database.ScalarAsync<Guid>("SELECT id FROM parliament.import_run ORDER BY started_at LIMIT 1");
        await Database.ExecuteAsync("UPDATE parliament.import_run SET synthetic = false WHERE id = @id", ("id", run));
        try
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                DemoAppeal.RunAsync(Configuration(true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None));
            error.Message.ShouldContain("stand-in");
            (await Database.ScalarAsync<long>(Appeals)).ShouldBe(appealsBefore, "the refused run wrote no appeal");
            (await Database.ScalarAsync<long>(Invented)).ShouldBe(inventedBefore, "and no invented account");
        }
        finally
        {
            await Database.ExecuteAsync("UPDATE parliament.import_run SET synthetic = true WHERE id = @id", ("id", run));
            await Database.ExecuteAsync("UPDATE appeals.appeal SET status = 'rejected' WHERE id = @id", ("id", existing));
        }
    }

    [Fact]
    public async Task On_the_stand_ins_data_it_fills_the_appeal_once_and_the_page_shows_the_numbers()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var first = await DemoAppeal.RunAsync(Configuration(true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None);
        first.Status.ShouldBe("created");
        first.AccountsCreated.ShouldBe(6800 + 4280);

        var again = await DemoAppeal.RunAsync(Configuration(true), Database.MigratorConnectionString, TimeProvider.System, CancellationToken.None);
        again.ShouldBe(new DemoAppeal.Outcome("already there", 0));

        using var stranger = NewBrowser();
        var appeal = await stranger.AppealAsync(AppealSteps.TaxBill);
        appeal.Status.ShouldBe("open");
        appeal.InitiatorName.ShouldBe("Rūta Mockutė");
        appeal.Counts.ShouldBe(new SideCountsResponse(6800, 6150, 4280, 3900));
        appeal.Threshold.ShouldBe(new ThresholdResponse(1000, "initiator", null, 11080, true, false));
        appeal.Surge.ShouldBeNull("the invented sides are spread over the appeal's life, not a surge of new accounts");
        appeal.Reasons.Select(r => (r.AuthorName, r.Endorsements)).ShouldBe(
            [("Giedrė L.", 1920), ("Rūta Mockutė", 1480), ("Mindaugas R.", 1310), ("Aistė Kazlaitė", 970)]);
    }
}
