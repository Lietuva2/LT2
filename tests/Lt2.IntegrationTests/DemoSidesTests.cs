using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Microsoft.Extensions.Configuration;

namespace Lt2.IntegrationTests;

/// <summary>
/// The migrator's <c>--demo-sides-for</c> (design 8.26.5): one existing account's random sides on the invented closed
/// bills, only where demonstration data is allowed, only with that account's positions consent, never anyone else's.
/// </summary>
public sealed class DemoSidesTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>An invented closed bill of the stand-in's overlay; tests never load the overlay, so the test closes a record on it by hand.</summary>
    private const string Bill = "XVP-0701";

    private static IConfiguration Configuration(bool allowed) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Allowed"] = allowed ? "true" : null }).Build();

    [Fact]
    public async Task It_refuses_unless_the_configuration_allows_it()
    {
        var (_, email) = await Factory.MemberAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            DemoSides.RunAsync(Configuration(false), Database.MigratorConnectionString, email, TimeProvider.System, CancellationToken.None));
        error.Message.ShouldContain("Demo:Allowed");
        error.Message.ShouldNotContain(email, Case.Insensitive);
    }

    [Fact]
    public async Task It_refuses_an_unknown_address_and_an_account_without_the_positions_consent_without_naming_the_address()
    {
        const string Unknown = "niekas.nezino@pavyzdys.test";
        var unknown = await Should.ThrowAsync<InvalidOperationException>(() =>
            DemoSides.RunAsync(Configuration(true), Database.MigratorConnectionString, Unknown, TimeProvider.System, CancellationToken.None));
        unknown.Message.ShouldContain("no account");
        unknown.Message.ShouldNotContain(Unknown, Case.Insensitive);

        var (_, email) = await Factory.MemberAsync(consent: false);
        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            DemoSides.RunAsync(Configuration(true), Database.MigratorConnectionString, email, TimeProvider.System, CancellationToken.None));
        refused.Message.ShouldContain("consent");
        refused.Message.ShouldNotContain(email, Case.Insensitive);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.position p JOIN identity.\"user\" u ON u.id = p.user_id WHERE u.email = @e", ("e", email)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task It_gives_the_named_account_one_side_on_a_closed_invented_bill_once_and_counts_it_in_the_frozen_numbers()
    {
        await Database.FreeBillAsync(Bill);
        var (_, email) = await Factory.MemberAsync();
        var other = await Factory.MemberAsync(displayName: "Jonas Petraitis");
        var closedAt = DateTimeOffset.UtcNow.AddDays(-20);
        var record = Guid.NewGuid();
        await Database.ExecuteAsync(
            """
            INSERT INTO appeals.bill_record (id, bill_number, description, description_source, status, opened_at, published_at, sending, threshold_set_by, changed_at,
                                        closed_at, closing_vote_id, closed_for, closed_for_older_than30days, closed_against, closed_against_older_than30days)
            VALUES (@id, @b, 'Išgalvotas aprašymas.', 'moderator', 'closed', @opened, @opened, 'automatic', 'default', @closed, @closed, 99100201, 3, 2, 2, 1)
            """,
            ("id", record), ("b", Bill), ("opened", closedAt.AddDays(-40)), ("closed", closedAt));
        try
        {
            // Matched as sign-in matches an address: case does not matter.
            var first = await DemoSides.RunAsync(Configuration(true), Database.MigratorConnectionString, "  " + email.ToUpperInvariant() + " ", TimeProvider.System, CancellationToken.None);
            (first.For + first.Against).ShouldBe(1, "only the one closed invented record exists here");

            var side = await Database.ScalarAsync<string>(
                "SELECT p.side FROM appeals.position p JOIN identity.\"user\" u ON u.id = p.user_id WHERE p.record_id = @r AND u.email = @e",
                ("r", record), ("e", email));
            side.ShouldBe(first.For == 1 ? "for" : "against");
            (await Database.ScalarAsync<bool>(
                "SELECT bool_and(p.created_at < @closed AND p.created_at >= @opened) FROM appeals.position p WHERE p.record_id = @r",
                ("r", record), ("closed", closedAt), ("opened", closedAt.AddDays(-40)))).ShouldBeTrue("taken while the record was open");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE record_id = @r", ("r", record))).ShouldBe(1, "no other account was touched");
            (await Database.ScalarAsync<int>("SELECT closed_for + closed_against FROM appeals.bill_record WHERE id = @r", ("r", record))).ShouldBe(6);
            (await Database.ScalarAsync<int>("SELECT closed_for_older_than30days + closed_against_older_than30days FROM appeals.bill_record WHERE id = @r", ("r", record)))
                .ShouldBe(3, "the account registered today is not older than 30 days at the closing");
            (await Database.ScalarAsync<bool>("SELECT changed_at > @closed FROM appeals.bill_record WHERE id = @r", ("r", record), ("closed", closedAt))).ShouldBeTrue("the kept analytics copies see the change");

            var again = await DemoSides.RunAsync(Configuration(true), Database.MigratorConnectionString, email, TimeProvider.System, CancellationToken.None);
            again.ShouldBe(new DemoSides.Outcome(0, 0));
            (await Database.ScalarAsync<int>("SELECT closed_for + closed_against FROM appeals.bill_record WHERE id = @r", ("r", record))).ShouldBe(6);
            other.Email.ShouldNotBe(email);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
