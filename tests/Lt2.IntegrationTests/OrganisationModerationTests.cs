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

    /// <summary>
    /// Design 8.14.9 (owner, 2026-11-20): "Neįtraukti į suvestines" needs a moderator and a statement, changes nothing on the
    /// site, is recorded and audited, told to the representatives by email and on their own part of the profile only, listed for
    /// the moderators with its undo, and undone by "Vėl įtraukti į suvestines".
    /// </summary>
    [Fact]
    public async Task Keeping_an_organisation_out_of_summaries_changes_nothing_on_the_site_is_audited_told_to_its_representatives_and_undone()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        await member.PublishStatementAsync(AppealSteps.TaxBill, created.Slug, "for", "Organizacijos pozicija suvestinėms.");
        using var visitor = NewBrowser();
        var profileBefore = System.Text.Json.JsonSerializer.Serialize(await visitor.ProfileAsync(created.Slug));
        var statementsBefore = System.Text.Json.JsonSerializer.Serialize((await visitor.StatementsAsync(AppealSteps.TaxBill)).Items.Where(i => i.OrganisationId == created.Id));
        var keepOut = OrganisationRoutes.With(OrganisationRoutes.KeepOutOfSummaries, created.Id);
        var returnTo = OrganisationRoutes.With(OrganisationRoutes.ReturnToSummaries, created.Id);
        const string Why = "Organizacijos pavadinimas klaidina Seimo narius.";

        // A member cannot act, and a moderator must say why.
        using (var refused = await member.PostAsync(keepOut, new StatementRequest { Statement = "Ne moderatorius." }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var empty = await moderator.Browser.PostAsync(keepOut, new StatementRequest { Statement = " " }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var notKeptOut = await moderator.Browser.PostAsync(returnTo, new StatementRequest { Statement = "Bandymas." }))
        {
            await notKeptOut.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
        }

        await moderator.Browser.ActAsync(OrganisationRoutes.KeepOutOfSummaries, created.Id, Why);
        using (var again = await moderator.Browser.PostAsync(keepOut, new StatementRequest { Statement = "Dar kartą." }))
        {
            await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
        }

        // Nothing changes on the site: the profile and the bill's statements answer exactly as before.
        System.Text.Json.JsonSerializer.Serialize(await visitor.ProfileAsync(created.Slug)).ShouldBe(profileBefore);
        System.Text.Json.JsonSerializer.Serialize((await visitor.StatementsAsync(AppealSteps.TaxBill)).Items.Where(i => i.OrganisationId == created.Id)).ShouldBe(statementsBefore);

        // Its representatives are told, by email and on their own part of the profile; nobody else sees it.
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("kept-out-of-summaries");
        var mine = await member.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug));
        mine.KeptOutOfSummariesAt.ShouldNotBeNull();
        mine.KeptOutStatement.ShouldBe(Why);
        var (stranger, _) = await Factory.MemberAsync();
        using (stranger)
        {
            var theirs = await stranger.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug));
            theirs.IsRepresentative.ShouldBeFalse();
            theirs.KeptOutOfSummariesAt.ShouldBeNull();
            theirs.KeptOutStatement.ShouldBeNull();
        }

        // The moderators see it on the list of hidden items with who, when and why.
        var kept = (await moderator.Browser.GetJsonAsync<HiddenOrganisationsResponse>(OrganisationRoutes.ModerationHidden)).KeptOut.ShouldNotBeNull().Single(o => o.Id == created.Id);
        kept.Statement.ShouldBe(Why);
        kept.KeptOutAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        await moderator.Browser.ActAsync(OrganisationRoutes.ReturnToSummaries, created.Id, "Paaiškėjo, kad pavadinimas tikras.");
        (await Database.OrganisationNoticesAsync(email)).First().ShouldBe("back-in-summaries");
        (await member.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug))).KeptOutOfSummariesAt.ShouldBeNull();
        (await moderator.Browser.GetJsonAsync<HiddenOrganisationsResponse>(OrganisationRoutes.ModerationHidden)).KeptOut.ShouldNotBeNull().ShouldNotContain(o => o.Id == created.Id);
        System.Text.Json.JsonSerializer.Serialize(await visitor.ProfileAsync(created.Slug)).ShouldBe(profileBefore);

        // Recorded and audited, without an address; neither answers the organisation's reports.
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.moderation_action WHERE target_kind = 'organisation' AND target_id = @o AND action IN ('keep-out', 'keep-in')", ("o", created.Id))).ShouldBe(2);
        var audit = await Database.ScalarAsync<string>(
            "SELECT string_agg(action || ' ' || coalesce(details::text, ''), ' | ') FROM audit.event WHERE target = @t", ("t", "organisation:" + created.Id));
        audit.ShouldNotBeNull().ShouldContain("moderation.organisation.keep-out");
        audit.ShouldContain("moderation.organisation.keep-in");
        audit.ShouldContain(Why);
        audit.ShouldNotContain(email, Case.Insensitive);

        await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE organisation_id = @o", ("o", created.Id));
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

    /// <summary>
    /// Design 6.9 (owner, 2026-11-15): a statement still waiting when the bill closes is never published. The queue still
    /// shows it and offers only "Palikti" and hiding; "Palikti" keeps it unpublished and tells the representatives.
    /// </summary>
    [Fact]
    public async Task A_statement_still_waiting_when_the_bill_closes_cannot_be_published_and_palikti_leaves_it_unpublished()
    {
        const long finalVote = 99199620;
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        var waiting = await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Rašykite mums: laukianti@pavyzdys.test");
        waiting.Status.ShouldBe("pending");
        using (var early = await moderator.Browser.PostAsync(OrganisationRoutes.With(OrganisationRoutes.KeepStatement, waiting.Id), new ModerationRequest()))
        {
            await early.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
        }

        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', false, true, "for", against, abstain, voted, seats
            FROM parliament.vote WHERE id = 99100102
            """,
            ("id", finalVote));
        try
        {
            using (var publish = await moderator.Browser.PostAsync(OrganisationRoutes.With(OrganisationRoutes.PublishStatement, waiting.Id), new ModerationRequest()))
            {
                await publish.ShouldBeProblemAsync(HttpStatusCode.Conflict, "statement.waiting-bill-closed");
                (await publish.Content.ReadAsStringAsync()).ShouldContain("Seimas dėl šio projekto jau balsavo galutinai");
            }

            var queued = (await moderator.Browser.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue)).Statements.Single(s => s.Id == waiting.Id);
            (queued.Status, queued.BillFinished).ShouldBe(("pending", true));

            using (var kept = await moderator.Browser.PostAsync(OrganisationRoutes.With(OrganisationRoutes.KeepStatement, waiting.Id), new ModerationRequest { Statement = "Seimas jau balsavo." }))
            {
                kept.StatusCode.ShouldBe(HttpStatusCode.NoContent, await kept.Content.ReadAsStringAsync());
            }

            (await moderator.Browser.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue)).Statements.ShouldNotContain(s => s.Id == waiting.Id);
            using var visitor = NewBrowser();
            (await visitor.StatementsAsync(AppealSteps.ThirdBill)).Items.ShouldNotContain(i => i.OrganisationId == created.Id);
            (await Database.ScalarAsync<string>("SELECT status FROM appeals.organisation_statement WHERE id = @id", ("id", waiting.Id))).ShouldBe("pending");
            (await Database.OrganisationNoticesAsync(email)).ShouldContain("statement-kept");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'moderation.statement.keep' AND target = @t", ("t", "statement:" + waiting.Id))).ShouldBe(1);
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", finalVote));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}
