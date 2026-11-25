using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.4, "Entering a day's agenda" (owner, 2026-11-25), over HTTP: a moderator or an administrator in a staff-passkey
/// session previews a sitting day's lines (nothing stored), saves them as the day's whole entered agenda (bills the site does not
/// know are created, a changed stage replaces the row, a bill missing from the list leaves the day; the watcher's and the
/// moderators' rows and other days are untouched), audited; the agenda and the bill's page show the day's document. Invented
/// numbers XVP-91xx only, removed after each test.
/// </summary>
public sealed class AgendaEntryTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Document = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAK/00000000aaaa11f1aaaa000000000412";
    private const string OtherDocument = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAK/00000000aaaa11f1aaaa000000000533";

    /// <summary>Days of their own, far enough ahead that no other test plans on them, and within the 31 days.</summary>
    private static DateOnly Day => ReasonsToComeSteps.VilniusToday().AddDays(24);

    private static DateOnly NextDay => Day.AddDays(2);

    public async ValueTask InitializeAsync()
    {
        await ParliamentSeed.EnsureAsync(Database);
        await ForgetAsync();
    }

    public async ValueTask DisposeAsync() => await ForgetAsync();

    [Fact]
    public async Task A_preview_stores_nothing_and_a_save_creates_the_unknown_bills_and_shows_on_the_agenda_and_the_bills_page()
    {
        using var moderator = await ModeratorAsync();
        var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");
        var titleBefore = await Database.ScalarAsync<string>("SELECT title FROM parliament.bill WHERE number = 'XVP-0618'");
        var lines = "XVP-0618 | antrasis svarstymas | Kitas pavadinimas, kurio svetainė nerašo\n\nXVP-9101(2) | Pateikimas, svarstymas, priėmimas | Išgalvoto įstatymo projektas\n";

        var preview = await PreviewAsync(moderator.Browser, Day, Document, lines);
        preview.Saved.ShouldBeFalse();
        (preview.Added, preview.StageChanged, preview.Unchanged, preview.Removed, preview.Created).ShouldBe((2, 0, 0, 0, 1));
        preview.Bills.ShouldBe(
        [
            new AgendaEntryBillResponse(1, "XVP-0618", BillStages.Consideration, "Kitas pavadinimas, kurio svetainė nerašo", false, AgendaEntryChanges.Added),
            new AgendaEntryBillResponse(3, "XVP-9101", BillStages.Adoption, "Išgalvoto įstatymo projektas", true, AgendaEntryChanges.Added),
        ]);
        preview.Refused.ShouldBeEmpty();
        (await EnteredRowsAsync(Day)).ShouldBe(0, "a preview stores nothing");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.bill WHERE number = 'XVP-9101'")).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.agenda_entry WHERE planned_date = @d", ("d", Day))).ShouldBe(0);

        var saved = await SaveAsync(moderator.Browser, Day, Document, lines);
        saved.Saved.ShouldBeTrue();
        saved.Bills.ShouldBe(preview.Bills);
        (saved.Added, saved.Created).ShouldBe((2, 1));

        // The rows: entered, the document, who saved it; the created bill with its line's title and no e-seimas link; a known
        // bill's title stands.
        (await Database.ScalarAsync<string>(
                "SELECT string_agg(bill_number || ' ' || coalesce(stage, '-') || ' ' || source || ' ' || source_url, ' | ' ORDER BY bill_number) FROM parliament.planned_item WHERE planned_date = @d AND gone_at IS NULL",
                ("d", Day)))
            .ShouldBe($"XVP-0618 consideration entry {Document} | XVP-9101 adoption entry {Document}");
        (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM parliament.planned_item p JOIN parliament.agenda_entry e ON e.planned_date = p.planned_date AND e.entered_by = p.confirmed_by WHERE p.planned_date = @d",
                ("d", Day)))
            .ShouldBe(2, "confirmed_by is who saved it");
        (await Database.ScalarAsync<string>("SELECT title || ' ' || coalesce(e_seimas_url, '-') FROM parliament.bill WHERE number = 'XVP-9101'"))
            .ShouldBe("Išgalvoto įstatymo projektas -");
        (await Database.ScalarAsync<string>("SELECT title FROM parliament.bill WHERE number = 'XVP-0618'")).ShouldBe(titleBefore);
        (await Database.ScalarAsync<string>(
                "SELECT source_url || ' ' || rows || ' ' || added || ' ' || changed || ' ' || removed || ' ' || created_bills || ' ' || (body = @body) FROM parliament.agenda_entry WHERE planned_date = @d",
                ("d", Day), ("body", lines)))
            .ShouldBe($"{Document} 2 2 0 0 1 true", "kept as sent");

        // The days from today.
        var days = await GetAsync<AgendaEntryDaysResponse>(moderator.Browser, ParliamentRoutes.AgendaEntries);
        var day = days.Days.Single(d => d.Day == Day);
        (day.Bills, day.SourceUrl).ShouldBe((2, Document));
        day.LastSaved.ShouldNotBeNull();

        // The agenda: the created bill has its title and page; the read time and link are the last save's.
        using var visitor = NewBrowser();
        var agenda = await GetAsync<AgendaResponse>(visitor, AgendaRoutes.Agenda);
        var item = agenda.Days.Single(d => d.Date == Day).Items.Single(i => i.Number == "XVP-9101");
        (item.Title, item.Stage, item.SetByModerator).ShouldBe(("Išgalvoto įstatymo projektas", BillStages.Adoption, false));
        agenda.AgendaSourceUrl.ShouldBe(Document);
        agenda.AgendaReadAt.ShouldBe(day.LastSaved);

        // The bill's page links the day's document as where its date comes from.
        var bill = await GetAsync<BillResponse>(visitor, ParliamentRoutes.BillByNumber("XVP-9101"));
        (bill.Planned?.Date, bill.PlannedAgendaUrl).ShouldBe((Day, Document));
        var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/XVP-9101"));
        page.ShouldContain("id=\"planned-agenda\"");
        page.ShouldContain($"href=\"{Document}\"");

        // Found by the search at once.
        (await GetAsync<BillListResponse>(visitor, ParliamentRoutes.Bills + "?q=" + Uri.EscapeDataString("Išgalvoto įstatymo"))).Items.ShouldContain(i => i.Number == "XVP-9101");

        // Audited: the day, the document and the counts, nothing about any person.
        (await Database.ScalarAsync<string>(
                "SELECT string_agg(action || ' ' || (details->>'sourceUrl') || ' ' || (details->>'rows') || ' ' || (details->>'added') || ' ' || (details->>'createdBills'), ' | ') FROM audit.event WHERE target = @t AND occurred_at >= @since",
                ("t", $"agenda:{Day:yyyy-MM-dd}"), ("since", since)))
            .ShouldBe($"agenda.entered {Document} 2 2 1");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target = @t AND details::text LIKE '%@%'", ("t", $"agenda:{Day:yyyy-MM-dd}"))).ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_day_again_replaces_a_changed_stage_takes_a_missing_bill_off_and_leaves_other_rows_and_days_alone()
    {
        using var moderator = await ModeratorAsync();
        await SaveAsync(moderator.Browser, NextDay, OtherDocument, "XVP-9104 | svarstymas | Kitos dienos projektas");

        // The watcher's row and a moderator's row on the same day.
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, source_url, first_seen, last_seen, note)
            VALUES (gen_random_uuid(), 'XVP-9102', @d, 'presentation', 'agenda', 'https://www.lrs.lt/darbotvarke', now() - interval '1 hour', now() - interval '1 hour', NULL),
                   (gen_random_uuid(), 'XVP-9103', @d, 'adoption', 'moderator', NULL, now() - interval '1 hour', now() - interval '1 hour', 'Pataisyta data.')
            """,
            ("d", Day));
        var othersBefore = await OtherRowsAsync();

        var first = await SaveAsync(moderator.Browser, Day, Document, "XVP-9102 | pateikimas | Pirmasis projektas\nXVP-9105 | svarstymas | Antrasis projektas");
        (first.Added, first.Created).ShouldBe((2, 2));

        var preview = await PreviewAsync(moderator.Browser, Day, OtherDocument, "XVP-9102 | svarstymas | Pirmasis projektas");
        (preview.Added, preview.StageChanged, preview.Unchanged, preview.Removed).ShouldBe((0, 1, 0, 1));
        preview.RemovedNumbers.ShouldBe(["XVP-9105"]);

        var again = await SaveAsync(moderator.Browser, Day, OtherDocument, "XVP-9102 | svarstymas | Pirmasis projektas");
        (again.Added, again.StageChanged, again.Unchanged, again.Removed, again.Created).ShouldBe((0, 1, 0, 1, 0));
        again.Bills.Single().Change.ShouldBe(AgendaEntryChanges.StageChanged);

        // The entered rows: the old stage and the bill taken off have gone_at; the new stage's row is in force.
        (await Database.ScalarAsync<string>(
                "SELECT string_agg(bill_number || ' ' || stage || ' ' || (gone_at IS NULL) || ' ' || source_url, ' | ' ORDER BY bill_number, gone_at NULLS LAST) FROM parliament.planned_item WHERE planned_date = @d AND source = 'entry'",
                ("d", Day)))
            .ShouldBe($"XVP-9102 presentation false {Document} | XVP-9102 consideration true {OtherDocument} | XVP-9105 consideration false {Document}");
        (await OtherRowsAsync()).ShouldBe(othersBefore, "the watcher's and the moderators' rows and the other day are untouched");

        // Saved once more unchanged: seen again, nothing replaced.
        var same = await SaveAsync(moderator.Browser, Day, OtherDocument, "XVP-9102 | svarstymas | Pirmasis projektas");
        (same.Added, same.StageChanged, same.Unchanged, same.Removed).ShouldBe((0, 0, 1, 0));
        (await EnteredRowsAsync(Day)).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.planned_item WHERE planned_date = @d AND source = 'entry'", ("d", Day))).ShouldBe(3);

        // An empty list is refused for a day with entered rows, in a preview and a save; nothing changes.
        foreach (var empty in new[] { "", "  \n\n " })
        {
            using (var refused = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(Day), new AgendaEntryRequest { SourceUrl = Document, Lines = empty }))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "agenda-entry.empty");
            }

            using var refusedPreview = await moderator.Browser.PostAsync(ParliamentRoutes.AgendaEntryPreview, new AgendaEntryPreviewRequest { Day = Day, SourceUrl = Document, Lines = empty });
            await refusedPreview.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "agenda-entry.empty");
        }

        (await EnteredRowsAsync(Day)).ShouldBe(1);
        (await OtherRowsAsync()).ShouldBe(othersBefore);
    }

    [Fact]
    public async Task A_refused_line_a_day_out_of_range_or_another_address_is_refused_and_changes_nothing()
    {
        using var moderator = await ModeratorAsync();
        const string lines = "XVP-9106 | svarstymas | Tinkama eilutė\nXVP-9107 svarstymas be skirtukų\nDarbotvarkės tvirtinimas | | Posėdis\nXVP-9108 | pateikimas |  ";

        // A preview lists the refused lines with their numbers and reasons, and the lines that are fine.
        var preview = await PreviewAsync(moderator.Browser, Day, Document, lines);
        preview.Refused.ShouldBe(
        [
            new AgendaEntryRefusalResponse(2, AgendaEntryRefusals.Fields),
            new AgendaEntryRefusalResponse(3, AgendaEntryRefusals.Number),
            new AgendaEntryRefusalResponse(4, AgendaEntryRefusals.TitleEmpty),
        ]);
        preview.Bills.Select(b => b.Number).ShouldBe(["XVP-9106"]);

        // A save with any refused line applies nothing.
        using (var refused = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(Day), new AgendaEntryRequest { SourceUrl = Document, Lines = lines }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "agenda-entry.lines");
            (await refused.ProblemAsync()).GetProperty("errors").TryGetProperty("lines", out _).ShouldBeTrue();
        }

        var today = ReasonsToComeSteps.VilniusToday();
        foreach (var (day, source, code) in new[]
        {
            (today.AddDays(-1), Document, "agenda-entry.day"),
            (today.AddDays(32), Document, "agenda-entry.day"),
            (Day, "http://e-seimas.lrs.lt/portal/legalAct/lt/TAK/1", "agenda-entry.source"),
            (Day, "https://seimas.pavyzdys.test/darbotvarke", "agenda-entry.source"),
        })
        {
            using (var put = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(day), new AgendaEntryRequest { SourceUrl = source, Lines = "XVP-9106 | svarstymas | Tinkama eilutė" }))
            {
                await put.ShouldBeProblemAsync(HttpStatusCode.BadRequest, code);
            }

            using var post = await moderator.Browser.PostAsync(ParliamentRoutes.AgendaEntryPreview, new AgendaEntryPreviewRequest { Day = day, SourceUrl = source, Lines = "XVP-9106 | svarstymas | Tinkama eilutė" });
            await post.ShouldBeProblemAsync(HttpStatusCode.BadRequest, code);
        }

        using (var badDay = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntries + "/2026-13-01", new AgendaEntryRequest { SourceUrl = Document, Lines = "XVP-9106 | svarstymas | Tinkama eilutė" }))
        {
            await badDay.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "agenda-entry.day");
        }

        using (var noSource = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(Day), new AgendaEntryRequest { SourceUrl = " ", Lines = "XVP-9106 | svarstymas | Tinkama eilutė" }))
        {
            noSource.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await noSource.ProblemAsync()).GetProperty("errors").TryGetProperty("sourceUrl", out _).ShouldBeTrue();
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.planned_item WHERE bill_number LIKE 'XVP-91%'")).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.bill WHERE number LIKE 'XVP-91%'")).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.agenda_entry")).ShouldBe(0);
    }

    [Fact]
    public async Task Only_a_moderator_or_an_administrator_in_a_staff_passkey_session_enters_the_agenda()
    {
        var (moderator, administrator) = await StaffAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        // A moderator of its own for the code session: the shared one's address keeps its code allowance for other tests.
        using var ownModerator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var codeSession = await Factory.CodeSessionAsync(ownModerator.Email);
        const string line = "XVP-9109 | svarstymas | Projektas";

        foreach (var (who, browser) in new[] { ("member", member), ("visitor", NewBrowser()), ("moderator's code session", codeSession) })
        {
            using (browser == member || browser == codeSession ? null : browser)
            {
                using var read = await browser.Client.GetAsync(ParliamentRoutes.AgendaEntries);
                read.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], who);
                using var preview = await browser.PostAsync(ParliamentRoutes.AgendaEntryPreview, new AgendaEntryPreviewRequest { Day = Day, SourceUrl = Document, Lines = line });
                preview.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], who);
                using var save = await browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(Day), new AgendaEntryRequest { SourceUrl = Document, Lines = line });
                save.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized], who);
            }
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.agenda_entry")).ShouldBe(0, "nothing was saved");

        // Without the antiforgery token a staff write is refused too.
        using (var noToken = await moderator.Browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(Day), new AgendaEntryRequest { SourceUrl = Document, Lines = line }, antiforgery: false))
        {
            noToken.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // The page: the form for staff, the refusal for a member.
        var staffPage = WebUtility.HtmlDecode(await moderator.Browser.Client.GetStringAsync(ParliamentRoutes.AgendaEntryPage));
        foreach (var id in new[] { "agenda-day", "agenda-source", "agenda-lines", "agenda-preview", "agenda-save", "agenda-days" })
        {
            staffPage.ShouldContain($"id=\"{id}\"");
        }

        staffPage.ShouldNotContain("type=\"date\"");
        (await member.Client.GetStringAsync(ParliamentRoutes.AgendaEntryPage)).ShouldNotContain("id=\"agenda-entry-form\"");
        WebUtility.HtmlDecode(await moderator.Browser.Client.GetStringAsync("/moderavimas")).ShouldContain($"href=\"{ParliamentRoutes.AgendaEntryPage}\" id=\"agenda-entry-link\"");

        // An administrator enters it as a moderator does.
        (await GetAsync<AgendaEntryDaysResponse>(administrator.Browser, ParliamentRoutes.AgendaEntries)).Days.ShouldNotContain(d => d.Day == Day);
        var saved = await SaveAsync(administrator.Browser, Day, Document, line);
        saved.Created.ShouldBe(1);
    }

    private static async Task<AgendaEntryResponse> PreviewAsync(TestBrowser browser, DateOnly day, string source, string lines)
    {
        using var response = await browser.PostAsync(ParliamentRoutes.AgendaEntryPreview, new AgendaEntryPreviewRequest { Day = day, SourceUrl = source, Lines = lines });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AgendaEntryResponse>())!;
    }

    private static async Task<AgendaEntryResponse> SaveAsync(TestBrowser browser, DateOnly day, string source, string lines)
    {
        using var response = await browser.SendAsync(HttpMethod.Put, ParliamentRoutes.AgendaEntryDayOf(day), new AgendaEntryRequest { SourceUrl = source, Lines = lines });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AgendaEntryResponse>())!;
    }

    private static async Task<T> GetAsync<T>(TestBrowser browser, string url)
    {
        using var response = await browser.Client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>The day's entered rows in force.</summary>
    private Task<long> EnteredRowsAsync(DateOnly day) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM parliament.planned_item WHERE planned_date = @d AND source = 'entry' AND gone_at IS NULL", ("d", day));

    /// <summary>Every row of the test's numbers that is not an entered row of <see cref="Day"/>, as it stands.</summary>
    private Task<string?> OtherRowsAsync() =>
        Database.ScalarAsync<string>(
            """
            SELECT coalesce(string_agg(bill_number || ' ' || planned_date || ' ' || coalesce(stage, '-') || ' ' || source || ' ' || last_seen || ' ' || coalesce(gone_at::text, '-'), ' | ' ORDER BY bill_number, source), '')
            FROM parliament.planned_item WHERE bill_number LIKE 'XVP-91%' AND NOT (source = 'entry' AND planned_date = @d)
            """,
            ("d", Day));

    /// <summary>Removes what the tests made: the entered days, the rows and bills of XVP-91xx, their search cards.</summary>
    private async Task ForgetAsync()
    {
        await Database.ExecuteAsync("DELETE FROM parliament.agenda_entry");
        await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number LIKE 'XVP-91%' OR source = 'entry'");
        await Database.ExecuteAsync("DELETE FROM appeals.bill_card WHERE bill_number LIKE 'XVP-91%'");
        await Database.ExecuteAsync("DELETE FROM parliament.bill WHERE number LIKE 'XVP-91%'");
    }
}
