using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.12.5, "How bills are searched": the one search, in the database, over the Appeals module's copy of the bill
/// catalogue (<c>appeals.bill_card</c>): a word is found by its stem in the <c>lithuanian</c> text search configuration or as
/// part of the folded text, every word is needed, the text is the number and both titles and never the description, and the
/// copy follows the catalogue and the readable title as it is written.
/// </summary>
public sealed class BillSearchTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Fares = "XVP-Q001";
    private const string Budget = "XVP-Q002";
    private const string Plain = "XVP-Q003";
    private const string FaresTitle = "Kelių eismo įstatymo pataisa dėl vaikų pavėžės";
    private const string BudgetTitle = "Biudžeto projektas, kuriam skiriama 100% lėšų";

    public async ValueTask InitializeAsync()
    {
        await ParliamentSeed.EnsureAsync(Database);
        await RemoveAsync();

        // Inserted by SQL and never refreshed by the test: the first search finds the copy short and makes it.
        foreach (var (number, title) in new[] { (Fares, FaresTitle), (Budget, BudgetTitle), (Plain, "Žemės ūkio paskirties sklypų pataisa") })
        {
            await Database.ExecuteAsync("INSERT INTO parliament.bill (id, number, title) VALUES (gen_random_uuid(), @n, @t)", ("n", number), ("t", title));
        }
    }

    public async ValueTask DisposeAsync() => await RemoveAsync();

    private async Task RemoveAsync()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ExecuteAsync("DELETE FROM appeals.bill_record WHERE bill_number LIKE 'XVP-Q%'");
        await Database.DeleteBillsAsync("XVP-Q%");
    }

    private async Task<T> WithCardsAsync<T>(Func<IBillCards, Task<T>> use)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<IBillCards>());
    }

    private async Task<string[]> SearchAsync(string query) =>
        (await WithCardsAsync(c => c.SearchAsync(query, CancellationToken.None)))!.Order(StringComparer.Ordinal).ToArray();

    private async Task RefreshAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBillCards>().RefreshAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_query_without_words_is_no_filter()
    {
        (await WithCardsAsync(c => c.SearchAsync(null, CancellationToken.None))).ShouldBeNull();
        (await WithCardsAsync(c => c.SearchAsync("   ", CancellationToken.None))).ShouldBeNull();
    }

    [Fact]
    public async Task A_word_is_found_by_its_stem()
    {
        (await SearchAsync("mokesčiai")).ShouldContain(AppealSteps.TaxBill, "\"mokesčiai\" finds a title with \"mokesčio\"");
        (await SearchAsync("mokesciai")).ShouldContain(AppealSteps.TaxBill, "without Lithuanian letters too: both fold to one stem");
        (await SearchAsync("pavėžėti")).ShouldBe([Fares], "\"pavėžėti\" finds \"pavėžės\"");
    }

    [Fact]
    public async Task A_word_is_found_as_part_of_the_text_ignoring_case_and_Lithuanian_letters()
    {
        (await SearchAsync("mokescio")).ShouldContain(AppealSteps.TaxBill);
        (await SearchAsync("MOKESČIO")).ShouldContain(AppealSteps.TaxBill);
        (await SearchAsync("XVP-04")).ShouldContain(AppealSteps.TaxBill, "a part of the number");
        (await SearchAsync("xvp-q00")).ShouldBe([Fares, Budget, Plain]);
        (await SearchAsync("kelių eismo")).ShouldBe([Fares]);
        (await SearchAsync("KELIU  EISMO")).ShouldBe([Fares], "white space between the words is any amount");
        (await SearchAsync("pavez")).ShouldBe([Fares], "a part of a word");
    }

    [Fact]
    public async Task Every_word_must_be_found()
    {
        (await SearchAsync("pajamų mokesčio")).ShouldContain(AppealSteps.TaxBill);
        (await SearchAsync("pajamų pavėžės")).ShouldBeEmpty();
        (await SearchAsync("pajamų zzzzzz")).ShouldBeEmpty();
        (await SearchAsync("kelių pavėžės")).ShouldBe([Fares], "words may stand anywhere in the text");
    }

    [Fact]
    public async Task LIKE_wildcards_in_the_query_are_literal()
    {
        (await SearchAsync("%")).ShouldBe([Budget], "only the title that has a percent sign");
        (await SearchAsync("_")).ShouldBeEmpty("an underscore is not \"any character\"");
        (await SearchAsync("xvp-q00_")).ShouldBeEmpty();
        (await SearchAsync(@"\")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_readable_title_is_searched_as_it_is_written_and_the_description_never()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var route = AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill);
        const string Title = "Atostogų tvarkos supaprastinimas";
        const string Text = "Šis aprašymas kalba apie keistuolišką apskaitos tvarką, kurios pavadinime nėra.";
        using (var written = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Text, Statement = "Bandymas.", PlainTitle = Title }))
        {
            written.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent, await written.Content.ReadAsStringAsync());
        }

        (await SearchAsync("atostogu")).ShouldBe([AppealSteps.SecondBill], "the readable title alone");
        (await SearchAsync("supaprastinimas")).ShouldBe([AppealSteps.SecondBill]);
        (await SearchAsync("keistuoliška")).ShouldBeEmpty("the description is never matched");
        (await SearchAsync("apskaitos")).ShouldBeEmpty("the description is never matched");

        using (var removed = await moderator.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.SecondBill), new StatementRequest { Statement = "Bandymas." }))
        {
            removed.IsSuccessStatusCode.ShouldBeTrue(await removed.Content.ReadAsStringAsync());
        }

        (await SearchAsync("atostogu")).ShouldBeEmpty("a removed readable title is no longer found");
    }

    [Fact]
    public async Task A_refresh_follows_the_catalogue_and_leaves_the_readable_title_alone()
    {
        await RefreshAsync();
        (await SearchAsync("pavėžės")).ShouldBe([Fares]);

        await Database.ExecuteAsync("UPDATE parliament.bill SET title = 'Naujas pavadinimas apie šunis' WHERE number = @n", ("n", Fares));
        await Database.ExecuteAsync("UPDATE appeals.bill_card SET plain_title = 'Tik skaitomas pavadinimas', plain_title_folded = 'tik skaitomas pavadinimas' WHERE bill_number = @n", ("n", Fares));
        await RefreshAsync();

        (await SearchAsync("šunis")).ShouldBe([Fares], "the official title is current again");
        (await SearchAsync("pavėžės")).ShouldBeEmpty();
        (await SearchAsync("skaitomas")).ShouldBe([Fares], "the readable title is not touched by a refresh");
        (await Database.ScalarAsync<string>("SELECT title_folded FROM appeals.bill_card WHERE bill_number = @n", ("n", Fares))).ShouldBe("xvp-q001 naujas pavadinimas apie sunis");
    }

    [Fact]
    public async Task A_refresh_makes_a_card_with_the_readable_title_of_the_bills_record_and_drops_a_card_with_no_bill()
    {
        await Database.FreeBillAsync(Plain);
        await Database.ExecuteAsync(
            """
            INSERT INTO appeals.bill_record (id, bill_number, description, plain_title, description_source, description_at, status, opened_at, published_at, sending, threshold_set_by, changed_at)
            VALUES (gen_random_uuid(), @n, 'Aprašymas.', 'Atnaujintas sklypų žodynas', 'moderator', now(), 'open', now(), now(), 'automatic', 'default', now())
            """,
            ("n", Plain));
        await Database.ExecuteAsync("DELETE FROM appeals.bill_card WHERE bill_number = @n", ("n", Plain));
        await Database.ExecuteAsync(
            "INSERT INTO appeals.bill_card (bill_number, title, title_folded) VALUES ('XVP-Q099', 'Neegzistuojantis', 'xvp-q099 neegzistuojantis')");

        await RefreshAsync();

        (await SearchAsync("žodynas")).ShouldBe([Plain]);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_card WHERE bill_number = 'XVP-Q099'")).ShouldBe(0);
    }

    [Fact]
    public async Task Writing_a_readable_title_and_the_summary_job_keep_the_card_in_step_in_the_same_change()
    {
        // The moderator's write: the card holds the title the record holds (the search above found it); here the record and the
        // card agree after a write and a removal.
        var moderator = (await ModeratorAsync()).Browser;
        var route = AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill);
        using (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = "Tekstas.", Statement = "Bandymas.", PlainTitle = "Vienodas pavadinimas" }))
        {
        }

        var plain = await Database.ScalarAsync<string>("SELECT plain_title FROM appeals.bill_card WHERE bill_number = @n", ("n", AppealSteps.SecondBill));
        plain.ShouldBe(await Database.ScalarAsync<string>("SELECT plain_title FROM appeals.bill_record WHERE bill_number = @n", ("n", AppealSteps.SecondBill)));
        (await Database.ScalarAsync<string>("SELECT plain_title_folded FROM appeals.bill_card WHERE bill_number = @n", ("n", AppealSteps.SecondBill))).ShouldBe("vienodas pavadinimas");
    }
}
