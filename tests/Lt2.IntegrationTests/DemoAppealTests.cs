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
        again.ShouldBe(new DemoAppeal.Outcome("already there", 0, "already there", 0, ClosedBill: "not imported yet"));

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
        record.Reasons.Select(r => (r.AuthorName, r.Endorsements)).ShouldBe(
            [("Giedrė L.", 1920), ("Rūta Mockutė", 1480), ("Mindaugas R.", 1310), ("Aistė Kazlaitė", 970)]);
    }
}
