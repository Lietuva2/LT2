using System.Net;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.14.9, 8.14.14: moderation of organisations after the fact. A blocked organisation's statements are shown
/// nowhere and it cannot publish; lifting the block returns exactly what was hidden. Hidden statements and blocked
/// organisations are on the list of hidden items with "Atkurti". Every action is emailed to the representatives.
/// </summary>
public sealed class OrganisationModerationTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_blocked_organisation_cannot_publish_its_statements_disappear_and_lifting_the_block_returns_exactly_what_was_hidden()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        foreach (var bill in new[] { AppealSteps.TaxBill, AppealSteps.SecondBill, AppealSteps.ThirdBill })
        {
            await Database.FreeBillAsync(bill);
        }

        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();

        // Three statements: one stays published, one a moderator hides, one the organisation withdraws.
        var shown = await member.PublishStatementAsync(AppealSteps.TaxBill, created.Slug, "for", "Lieka paskelbta.", goal: 100);
        var hidden = await member.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, "against", "Paslėps moderatorius.");
        await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "remarks", "Atsiims pati organizacija.");
        await moderator.Browser.ActAsync(OrganisationRoutes.HideStatement, hidden.Id, "Pažeidžia taisykles.");
        (await member.SendAsync(HttpMethod.Delete, OrganisationRoutes.StatementOf(AppealSteps.ThirdBill, created.Slug))).Dispose();

        using var visitor = NewBrowser();
        async Task<IReadOnlyList<string>> ShownBillsAsync()
        {
            var bills = new List<string>();
            foreach (var bill in new[] { AppealSteps.TaxBill, AppealSteps.SecondBill, AppealSteps.ThirdBill })
            {
                if ((await visitor.StatementsAsync(bill)).Items.Any(i => i.OrganisationId == created.Id))
                {
                    bills.Add(bill);
                }
            }

            return bills;
        }

        (await ShownBillsAsync()).ShouldBe([AppealSteps.TaxBill]);

        await moderator.Browser.ActAsync(OrganisationRoutes.Block, created.Id, "Apsimeta kita organizacija.");
        (await ShownBillsAsync()).ShouldBeEmpty();
        var profile = await visitor.ProfileAsync(created.Slug);
        profile.Status.ShouldBe("blocked");
        profile.BlockStatement.ShouldBe("Apsimeta kita organizacija.");
        profile.Statements.ShouldBeEmpty();
        WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/o/" + created.Slug)).ShouldContain("Organizacija užblokuota");
        WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.TaxBill)).ShouldNotContain("Lieka paskelbta.");
        (await visitor.GetJsonAsync<OrganisationListResponse>(OrganisationRoutes.List(profile.Name))).Items.ShouldBeEmpty();
        using (var refused = await member.PutStatementAsync(AppealSteps.SecondBill, created.Slug, "for", "Bandau per blokavimą."))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "organisation.blocked");
        }

        (await Database.OrganisationNoticesAsync(email)).ShouldContain("blocked");

        // The hidden list: the blocked organisation and the hidden statement.
        var list = await moderator.Browser.GetJsonAsync<HiddenOrganisationsResponse>(OrganisationRoutes.ModerationHidden);
        list.Organisations.Single(o => o.Id == created.Id).Statement.ShouldBe("Apsimeta kita organizacija.");
        list.Statements.Single(s => s.Id == hidden.Id).Restorable.ShouldBeTrue();

        await moderator.Browser.ActAsync(OrganisationRoutes.Unblock, created.Id, "Paaiškėjo, kad tai tikroji organizacija.");
        (await ShownBillsAsync()).ShouldBe([AppealSteps.TaxBill], "exactly what was hidden by the block: not the hidden, not the withdrawn");
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("unblocked");

        await moderator.Browser.ActAsync(OrganisationRoutes.RestoreStatement, hidden.Id, "Atkuriama.");
        (await ShownBillsAsync()).ShouldBe([AppealSteps.TaxBill, AppealSteps.SecondBill]);
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("statement-restored");

        foreach (var bill in new[] { AppealSteps.TaxBill, AppealSteps.SecondBill, AppealSteps.ThirdBill })
        {
            await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE organisation_id = @o AND bill_number = @b", ("o", created.Id), ("b", bill));
        }
    }

    /// <summary>Design 8.14.14, 6.8: a changed description that pre-screening flags brings the organisation back to the queue.</summary>
    [Fact]
    public async Task A_flagged_new_description_brings_a_seen_organisation_back_to_the_queue()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var name = OrganisationSteps.NewName();
        var created = await member.RegisterOrganisationAsync(name);
        await moderator.Browser.ActAsync(OrganisationRoutes.Keep, created.Id, "Peržiūrėta.");

        async Task<bool> QueuedAsync() =>
            (await moderator.Browser.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue)).Organisations.Any(o => o.Id == created.Id);

        async Task ChangeAsync(string description)
        {
            using var changed = await member.SendAsync(HttpMethod.Put, OrganisationRoutes.Of(created.Slug), new OrganisationRequest { Name = name, Description = description });
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());
        }

        (await QueuedAsync()).ShouldBeFalse("seen");
        await ChangeAsync("Kitas išgalvotos organizacijos aprašymas.");
        (await QueuedAsync()).ShouldBeFalse("a description pre-screening does not flag is not looked at again");
        await ChangeAsync("Rašykite info@pavyzdys.test arba skambinkite 861234567.");
        var queued = (await moderator.Browser.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue)).Organisations.Single(o => o.Id == created.Id);
        queued.Description.ShouldContain("861234567");
    }

    [Fact]
    public async Task Every_moderation_action_is_recorded_and_audited_without_an_address_or_a_text()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        await moderator.Browser.ActAsync(OrganisationRoutes.Block, created.Id, "Bandymas blokuoti.");
        await moderator.Browser.ActAsync(OrganisationRoutes.Unblock, created.Id, "Bandymas atšaukti.");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.moderation_action WHERE target_kind = 'organisation' AND target_id = @o", ("o", created.Id))).ShouldBe(2);
        var audit = await Database.ScalarAsync<string>(
            "SELECT string_agg(action || ' ' || coalesce(details::text, ''), ' | ') FROM audit.event WHERE target = @t", ("t", "organisation:" + created.Id));
        audit.ShouldNotBeNull().ShouldContain("moderation.organisation.block");
        audit.ShouldContain("moderation.organisation.unblock");
        audit.ShouldNotContain(email, Case.Insensitive);

        // A moderator's action needs a statement, and a member cannot act.
        using (var empty = await moderator.Browser.PostAsync(OrganisationRoutes.With(OrganisationRoutes.Block, created.Id), new StatementRequest { Statement = "" }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var refused = await member.PostAsync(OrganisationRoutes.With(OrganisationRoutes.Block, created.Id), new StatementRequest { Statement = "Ne moderatorius." });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
