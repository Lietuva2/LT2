using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.14.14, stage A, over HTTP: registering an organisation, its profile and list, changing it with a public history,
/// confirmation by email domain, representatives through one-time links, leaving and closing, and statements on bills.
/// </summary>
public sealed class OrganisationApiTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_member_registers_an_organisation_shown_at_once_unconfirmed_and_becomes_its_representative()
    {
        var (member, _) = await Factory.MemberAsync("Laura Pavyzdytė");
        using var _m = member;
        var name = OrganisationSteps.NewName("Pavyzdinė žvejų bendrija");
        var created = await member.RegisterOrganisationAsync(name, "https://www." + OrganisationSteps.NewHost() + "/apie");
        created.Slug.ShouldStartWith("pavyzdine-zveju-bendrija-");

        using var visitor = NewBrowser();
        var list = await visitor.GetJsonAsync<OrganisationListResponse>(OrganisationRoutes.List(name));
        list.Items.ShouldHaveSingleItem().Label.ShouldBe(new OrganisationLabelResponse(null, null));
        var profile = await visitor.ProfileAsync(created.Slug);
        profile.Name.ShouldBe(name);
        profile.Status.ShouldBe("active");
        profile.WebsiteHost.ShouldNotBeNull().ShouldEndWith(".pavyzdys.test");
        profile.Statements.ShouldBeEmpty();

        var mine = await member.GetJsonAsync<List<MyOrganisationResponse>>(OrganisationRoutes.Mine);
        mine.ShouldContain(o => o.Slug == created.Slug);
        var management = await member.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug));
        management.IsRepresentative.ShouldBeTrue();
        management.Representatives.ShouldHaveSingleItem().DisplayName.ShouldBe("Laura Pavyzdytė");

        // The page is server-rendered: the profile and the list read without JavaScript.
        var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/o/" + created.Slug));
        html.ShouldContain(name);
        html.ShouldContain("Nepatvirtinta");
        // Design 8.15: what this label means is answered in place (a hint), every label on the methodology page.
        AppealPageTests.HintOf(html, "label-meaning").ShouldBe(("Ką reiškia ši žyma?", "„Nepatvirtinta“: niekas nepatikrino, ar organizacija tikrai valdo nurodytą svetainę.", "/metodika#m-organisation-labels"));
        var listed = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/organizacijos?q=" + Uri.EscapeDataString(name)));
        listed.ShouldContain(name);
        // The labels repeat in the rows: what they mean is said once, beside the list.
        AppealPageTests.HintOf(listed, "labels-what").Trigger.ShouldBe("Ką reiškia žymos?");

        // A new organisation is in the moderators' queue to be looked at, not approved.
        var moderator = (await ModeratorAsync()).Browser;
        var queue = await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue);
        queue.Organisations.ShouldContain(o => o.Id == created.Id && !o.Seen && !o.Changed);
    }

    [Fact]
    public async Task A_name_another_active_organisation_has_without_case_is_refused_and_bad_fields_are_said_at_the_field()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var name = OrganisationSteps.NewName();
        await member.RegisterOrganisationAsync(name);
        using (var again = await member.PostAsync(OrganisationRoutes.Organisations, new OrganisationRequest { Name = "  " + name.ToUpperInvariant() + " " }))
        {
            await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "organisation.name-taken");
        }

        foreach (var (request, field) in new (OrganisationRequest, string)[]
        {
            (new OrganisationRequest { Name = "Ab" }, "name"),
            (new OrganisationRequest { Name = OrganisationSteps.NewName(), Website = "http://asociacija.test" }, "website"),
            (new OrganisationRequest { Name = OrganisationSteps.NewName(), Website = "https://user@asociacija.test" }, "website"),
            (new OrganisationRequest { Name = OrganisationSteps.NewName(), LegalCode = "12345" }, "legalCode"),
            (new OrganisationRequest { Name = OrganisationSteps.NewName(), Description = new string('a', 501) }, "description"),
        })
        {
            // A member of their own each time: registering is limited per account and hour (design 6.4).
            var (fresh, _) = await Factory.MemberAsync();
            using var _fresh = fresh;
            using var refused = await fresh.PostAsync(OrganisationRoutes.Organisations, request);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
            (await refused.ProblemAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        using var visitor = NewBrowser();
        using var anonymous = await visitor.PostAsync(OrganisationRoutes.Organisations, new OrganisationRequest { Name = OrganisationSteps.NewName() });
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_account_registers_at_most_the_settings_number_of_organisations()
    {
        using var admin = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        await admin.Browser.SetSettingAsync("Organisations:PerAccount", 2);
        try
        {
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            var first = await member.RegisterOrganisationAsync();
            await member.RegisterOrganisationAsync();
            using (var third = await member.PostAsync(OrganisationRoutes.Organisations, new OrganisationRequest { Name = OrganisationSteps.NewName() }))
            {
                await third.ShouldBeProblemAsync(HttpStatusCode.Conflict, "organisation.limit");
            }

            // A closed one no longer counts.
            using (var closed = await member.PostAsync(OrganisationRoutes.CloseOf(first.Slug), new { }))
            {
                closed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await closed.Content.ReadAsStringAsync());
            }

            await member.RegisterOrganisationAsync();
        }
        finally
        {
            await admin.Browser.ResetSettingAsync("Organisations:PerAccount");
        }
    }

    [Fact]
    public async Task Changing_the_name_or_website_keeps_a_public_history_and_puts_the_organisation_back_in_the_queue()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var host = OrganisationSteps.NewHost();
        var oldName = OrganisationSteps.NewName();
        var created = await member.RegisterOrganisationAsync(oldName, "https://" + host);
        await member.ConfirmByDomainAsync(Database, created.Slug, host);
        await moderator.ActAsync(OrganisationRoutes.Keep, created.Id, null);
        (await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue)).Organisations.ShouldNotContain(o => o.Id == created.Id);

        // Same host with www: the confirmation by domain stays; a new name is in the history.
        var newName = OrganisationSteps.NewName("Pervadinta asociacija");
        using (var changed = await member.SendAsync(HttpMethod.Put, OrganisationRoutes.Of(created.Slug), new OrganisationRequest { Name = newName, Website = "https://www." + host + "/naujas" }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());
        }

        using var visitor = NewBrowser();
        var profile = await visitor.ProfileAsync(created.Slug);
        profile.Name.ShouldBe(newName);
        profile.Slug.ShouldBe(created.Slug, "the address does not change with the name");
        profile.Label.ShouldBe(new OrganisationLabelResponse("domain", host));
        profile.History.ShouldContain(h => h.Field == "name" && h.OldValue == oldName && h.NewValue == newName);
        profile.History.ShouldContain(h => h.Field == "website" && h.OldValue == "https://" + host);
        var queued = (await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue)).Organisations.Single(o => o.Id == created.Id);
        queued.Changed.ShouldBeTrue();

        // Another host removes the confirmation by domain.
        using (var moved = await member.SendAsync(HttpMethod.Put, OrganisationRoutes.Of(created.Slug), new OrganisationRequest { Name = newName, Website = "https://" + OrganisationSteps.NewHost() }))
        {
            moved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await visitor.ProfileAsync(created.Slug)).Label.ShouldBe(new OrganisationLabelResponse(null, null));

        // Somebody who is not a representative cannot change it.
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        using var refused = await other.SendAsync(HttpMethod.Put, OrganisationRoutes.Of(created.Slug), new OrganisationRequest { Name = OrganisationSteps.NewName() });
        await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "organisation.representatives-only");
    }

    [Fact]
    public async Task A_code_on_the_websites_domain_confirms_and_stores_only_the_domain()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var host = OrganisationSteps.NewHost();
        var created = await member.RegisterOrganisationAsync(website: "https://www." + host);

        foreach (var (address, problem) in new[]
        {
            ("kas@gmail.com", "confirm.public-mail"),
            ("info@evil-" + host, "confirm.wrong-domain"),
            ("info@" + host + ".evil.example", "confirm.wrong-domain"),
            ("info@" + host + "@evil.example", "confirm.address-invalid"),
        })
        {
            using var refused = await member.PostAsync(OrganisationRoutes.ConfirmationCodeOf(created.Slug), new ConfirmationCodeRequest { Address = address });
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, problem);
            (await Database.LatestOrganisationCodeAsync(address)).ShouldBeNull(address);
        }

        var mailbox = "pirmininke@mail." + host;
        using (var sent = await member.PostAsync(OrganisationRoutes.ConfirmationCodeOf(created.Slug), new ConfirmationCodeRequest { Address = mailbox }))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync());
        }

        var code = (await Database.LatestOrganisationCodeAsync(mailbox)).ShouldNotBeNull();

        // The module keeps the domain, a hash, the expiry and attempts; never the mailbox (only the outbox row has it).
        (await Database.ScalarAsync<string>("SELECT domain FROM appeals.organisation_code WHERE organisation_id = @o ORDER BY created_at DESC LIMIT 1", ("o", created.Id))).ShouldBe(host);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.organisation_code c WHERE row_to_json(c)::text ILIKE @m", ("m", "%pirmininke%"))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.organisation_code c WHERE row_to_json(c)::text LIKE @c", ("c", "%" + code + "%"))).ShouldBe(0);

        using (var wrong = await member.PostAsync(OrganisationRoutes.ConfirmationOf(created.Slug), new ConfirmationRequest { Code = code == "000000" ? "111111" : "000000" }))
        {
            await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "confirm.code-invalid");
        }

        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        using (var stranger = await other.PostAsync(OrganisationRoutes.ConfirmationOf(created.Slug), new ConfirmationRequest { Code = code }))
        {
            await stranger.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "organisation.representatives-only");
        }

        using (var right = await member.PostAsync(OrganisationRoutes.ConfirmationOf(created.Slug), new ConfirmationRequest { Code = code }))
        {
            right.StatusCode.ShouldBe(HttpStatusCode.NoContent, await right.Content.ReadAsStringAsync());
        }

        using var visitor = NewBrowser();
        (await visitor.ProfileAsync(created.Slug)).Label.ShouldBe(new OrganisationLabelResponse("domain", host));
        WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/o/" + created.Slug)).ShouldContain("Patvirtinta per el. pašto adresą @" + host);
        using var reused = await member.PostAsync(OrganisationRoutes.ConfirmationOf(created.Slug), new ConfirmationRequest { Code = code });
        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a code is used once");
    }

    [Fact]
    public async Task A_website_on_a_shared_platform_cannot_be_confirmed_by_domain_and_a_moderator_confirms_by_hand()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync(website: "https://www.facebook.com/pavyzdine-bendrija");
        (await member.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug))).DomainConfirmation.ShouldBe("confirm.shared-website");
        using (var refused = await member.PostAsync(OrganisationRoutes.ConfirmationCodeOf(created.Slug), new ConfirmationCodeRequest { Address = "info@facebook.com" }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        using (var noNote = await moderator.PostAsync(OrganisationRoutes.With(OrganisationRoutes.Confirm, created.Id), new StatementRequest { Statement = " " }))
        {
            noNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        await moderator.ActAsync(OrganisationRoutes.Confirm, created.Id, "Patikrinau registrų centro išrašą.");
        using var visitor = NewBrowser();
        (await visitor.ProfileAsync(created.Slug)).Label.ShouldBe(new OrganisationLabelResponse("moderator", null));
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("confirmed");
    }

    [Fact]
    public async Task After_a_moderator_withdraws_a_confirmation_a_code_confirms_nothing()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var host = OrganisationSteps.NewHost();
        var created = await member.RegisterOrganisationAsync(website: "https://" + host);
        await member.ConfirmByDomainAsync(Database, created.Slug, host);

        // A second code asked before the withdrawal is waiting when the moderator withdraws.
        await moderator.ActAsync(OrganisationRoutes.Unconfirm, created.Id, "Domenas priklauso kitai organizacijai.");
        using var visitor = NewBrowser();
        (await visitor.ProfileAsync(created.Slug)).Label.ShouldBe(new OrganisationLabelResponse(null, null));
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("confirmation-withdrawn");
        using (var refused = await member.PostAsync(OrganisationRoutes.ConfirmationCodeOf(created.Slug), new ConfirmationCodeRequest { Address = "info@" + host }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "confirm.barred");
        }

        // Even a code that would match an earlier row confirms nothing.
        var old = await Database.LatestOrganisationCodeAsync("info@" + host);
        using (var late = await member.PostAsync(OrganisationRoutes.ConfirmationOf(created.Slug), new ConfirmationRequest { Code = old! }))
        {
            late.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await visitor.ProfileAsync(created.Slug)).Label.ConfirmedBy.ShouldBeNull();
        await moderator.ActAsync(OrganisationRoutes.Confirm, created.Id, "Patikrinau kitu būdu.");
        (await visitor.ProfileAsync(created.Slug)).Label.ConfirmedBy.ShouldBe("moderator");
    }

    [Fact]
    public async Task Representatives_come_by_a_one_time_link_see_each_others_names_and_the_last_one_only_closes()
    {
        var (first, _) = await Factory.MemberAsync("Laura Pavyzdytė");
        using var _f = first;
        var created = await first.RegisterOrganisationAsync();
        using var invited = await first.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { });
        invited.StatusCode.ShouldBe(HttpStatusCode.OK, await invited.Content.ReadAsStringAsync());
        var invite = await invited.ReadAsync<InviteCreatedResponse>();
        invite.Path.ShouldStartWith("/organizacijos/kvietimas#");
        invite.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(6.9));

        var (second, _) = await Factory.MemberAsync("Tomas Pavyzdys");
        using var _s = second;
        (await second.PostJsonAsync<InviteResponse>(OrganisationRoutes.Invite, new InviteTokenRequest { Token = invite.Token })).Usable.ShouldBeTrue();
        using (var accepted = await second.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token }))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        }

        var (third, _) = await Factory.MemberAsync();
        using var _t = third;
        using (var again = await third.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token }))
        {
            await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invite.used");
        }

        var seen = await second.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug));
        seen.Representatives.Select(r => r.DisplayName).ShouldBe(["Laura Pavyzdytė", "Tomas Pavyzdys"], ignoreOrder: true);
        var outsider = await third.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug));
        outsider.IsRepresentative.ShouldBeFalse();
        outsider.Representatives.ShouldBeEmpty();

        // The second leaves; the first, now last, cannot leave and closes instead.
        using (var left = await second.PostAsync(OrganisationRoutes.LeaveOf(created.Slug), new { }))
        {
            left.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var last = await first.PostAsync(OrganisationRoutes.LeaveOf(created.Slug), new { }))
        {
            await last.ShouldBeProblemAsync(HttpStatusCode.Conflict, "organisation.last-representative");
        }

        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        await first.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "remarks", "Pastabos bandymui.");
        using (var closed = await first.PostAsync(OrganisationRoutes.CloseOf(created.Slug), new { }))
        {
            closed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var visitor = NewBrowser();
        var profile = await visitor.ProfileAsync(created.Slug);
        profile.Status.ShouldBe("closed");
        profile.Statements.ShouldBeEmpty();
        (await visitor.StatementsAsync(AppealSteps.ThirdBill)).Items.ShouldNotContain(i => i.OrganisationId == created.Id);
        (await Database.ScalarAsync<string>("SELECT status FROM appeals.organisation_statement WHERE organisation_id = @o", ("o", created.Id))).ShouldBe("withdrawn");
        (await visitor.GetJsonAsync<OrganisationListResponse>(OrganisationRoutes.List(profile.Name))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_representative_removes_another_by_the_representatives_id()
    {
        var (first, _) = await Factory.MemberAsync();
        using var _f = first;
        var created = await first.RegisterOrganisationAsync();
        var invite = await (await first.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { })).ReadAsync<InviteCreatedResponse>();
        var (second, _) = await Factory.MemberAsync();
        using var _s = second;
        (await second.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token })).Dispose();
        var reps = (await first.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug))).Representatives;
        var me = reps.Single(r => r.IsMe);
        using (var self = await first.SendAsync(HttpMethod.Delete, OrganisationRoutes.RepresentativeOf(created.Slug, me.Id)))
        {
            await self.ShouldBeProblemAsync(HttpStatusCode.Conflict, "representative.self");
        }

        using (var removed = await first.SendAsync(HttpMethod.Delete, OrganisationRoutes.RepresentativeOf(created.Slug, reps.Single(r => !r.IsMe).Id)))
        {
            removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await second.GetJsonAsync<OrganisationManagementResponse>(OrganisationRoutes.ManagementOf(created.Slug))).IsRepresentative.ShouldBeFalse();
    }

    [Fact]
    public async Task A_statement_is_published_at_once_counted_nowhere_changed_with_a_public_history_and_withdrawn()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        var (participant, _) = await Factory.MemberAsync();
        using var _p = participant;
        (await participant.TakeSideAsync(AppealSteps.SecondBill, "for")).Dispose();

        var saved = await member.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, "for", "Prašome balsuoti už.\nAntra pastraipa.", "https://www.lgs.pavyzdys.test/pozicija", 21);
        saved.Status.ShouldBe("published");
        using var visitor = NewBrowser();
        var shown = (await visitor.StatementsAsync(AppealSteps.SecondBill)).Items.Single(i => i.OrganisationId == created.Id);
        shown.Statement.Stance.ShouldBe("for");
        shown.Statement.LinkHost.ShouldBe("lgs.pavyzdys.test");
        shown.Statement.Goal.ShouldBe(21);
        shown.Statement.Progress.ShouldBe(1, "the participants on the organisation's side as the page counts them");
        shown.Label.ShouldBe(new OrganisationLabelResponse(null, null));
        var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
        page.ShouldContain("Organizacijų pozicijos");
        page.ShouldContain("Siekiame 21 dalyvio · dabar 1");
        page.ShouldContain("rel=\"nofollow noopener ugc\"");

        // A changed statement keeps every earlier version, public on the profile.
        await member.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, "against", "Persigalvojome: prašome balsuoti prieš.");
        var profile = await visitor.ProfileAsync(created.Slug);
        var onProfile = profile.Statements.Single();
        onProfile.Statement.Stance.ShouldBe("against");
        onProfile.Statement.EditedAt.ShouldNotBeNull();
        onProfile.Earlier.ShouldHaveSingleItem().Text.ShouldBe("Prašome balsuoti už.\nAntra pastraipa.");

        // One statement per organisation per bill; remarks carry no goal.
        using (var goal = await member.PutStatementAsync(AppealSteps.SecondBill, created.Slug, "remarks", "Pastabos.", goal: 10))
        {
            goal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var withdrawn = await member.SendAsync(HttpMethod.Delete, OrganisationRoutes.StatementOf(AppealSteps.SecondBill, created.Slug)))
        {
            withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await visitor.StatementsAsync(AppealSteps.SecondBill)).Items.ShouldNotContain(i => i.OrganisationId == created.Id);
        (await member.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, "remarks", "Nauja pozicija po atsiėmimo.")).Status.ShouldBe("published");
    }

    [Fact]
    public async Task A_flagged_statement_waits_for_a_moderator_who_publishes_it_and_the_representatives_are_told()
    {
        var moderator = (await ModeratorAsync()).Browser;
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        var saved = await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Rašykite mums: pozicija@pavyzdys.test");
        saved.Status.ShouldBe("pending");
        using var visitor = NewBrowser();
        (await visitor.StatementsAsync(AppealSteps.ThirdBill)).Items.ShouldNotContain(i => i.OrganisationId == created.Id);
        var mine = await member.GetJsonAsync<List<MyBillOrganisationResponse>>(OrganisationRoutes.MineOnBillOf(AppealSteps.ThirdBill));
        mine.Single(o => o.Slug == created.Slug).Statement!.Status.ShouldBe("pending");

        var panel = await moderator.GetJsonAsync<List<QueueStatementResponse>>(OrganisationRoutes.ModerationBillOf(AppealSteps.ThirdBill));
        panel.Single(s => s.Id == saved.Id).Flags.ShouldContain("email");
        using (var refused = await member.Client.GetAsync(OrganisationRoutes.ModerationBillOf(AppealSteps.ThirdBill)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var published = await moderator.PostAsync(OrganisationRoutes.With(OrganisationRoutes.PublishStatement, saved.Id), new ModerationRequest()))
        {
            published.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await visitor.StatementsAsync(AppealSteps.ThirdBill)).Items.ShouldContain(i => i.OrganisationId == created.Id);
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("statement-published");
    }

    [Fact]
    public async Task A_hidden_record_or_a_finished_bill_takes_no_statement_and_a_closed_bill_freezes_it()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var record = await moderator.RecordIdAsync(AppealSteps.ThirdBill);
        await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Prieš uždarant.");
        try
        {
            // Hidden: taking part closed; nothing of the organisations is shown and nothing is accepted.
            using (var hidden = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, record), new StatementRequest { Statement = "Bandymas." }))
            {
                hidden.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            using var visitor = NewBrowser();
            (await visitor.StatementsAsync(AppealSteps.ThirdBill)).Items.ShouldBeEmpty();
            using (var refused = await member.PutStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Po paslėpimo."))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "statement.bill-not-open");
            }

            // Closed: shown, frozen.
            await Database.ExecuteAsync("UPDATE appeals.bill_record SET status = 'closed', closed_at = now(), closed_for = 0, closed_for_older_than30days = 0, closed_against = 0, closed_against_older_than30days = 0 WHERE id = @id", ("id", record));
            var statements = await visitor.StatementsAsync(AppealSteps.ThirdBill);
            statements.Open.ShouldBeFalse();
            statements.Items.ShouldContain(i => i.OrganisationId == created.Id);
            using (var frozen = await member.SendAsync(HttpMethod.Delete, OrganisationRoutes.StatementOf(AppealSteps.ThirdBill, created.Slug)))
            {
                await frozen.ShouldBeProblemAsync(HttpStatusCode.Conflict, "statement.bill-not-open");
            }
        }
        finally
        {
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    [Fact]
    public async Task A_writing_restriction_reached_through_a_statement_stops_registering_and_publishing()
    {
        var moderator = (await ModeratorAsync()).Browser;
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, _) = await Factory.MemberAsync("Rašanti Atstovė");
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        var saved = await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Pozicija, dėl kurios ribojama.");
        var writer = await moderator.GetJsonAsync<AuthorViewResponse>(OrganisationRoutes.With(OrganisationRoutes.StatementWriter, saved.Id));
        writer.DisplayName.ShouldBe("Rašanti Atstovė");
        using (var restricted = await moderator.PostAsync(OrganisationRoutes.With(OrganisationRoutes.RestrictStatementWriter, saved.Id), new RestrictionRequest { Statement = "Bandymas." }))
        {
            restricted.StatusCode.ShouldBe(HttpStatusCode.Created, await restricted.Content.ReadAsStringAsync());
        }

        using (var register = await member.PostAsync(OrganisationRoutes.Organisations, new OrganisationRequest { Name = OrganisationSteps.NewName() }))
        {
            await register.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        using (var publish = await member.PutStatementAsync(AppealSteps.ThirdBill, created.Slug, "against", "Kitaip."))
        {
            await publish.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        (await member.GetJsonAsync<List<MyBillOrganisationResponse>>(OrganisationRoutes.MineOnBillOf(AppealSteps.ThirdBill))).Single().Refusal.ShouldBe("writing.restricted");
        var restrictionId = (await moderator.GetJsonAsync<List<RestrictionResponse>>(AppealRoutes.Restrictions)).Single(r => r.DisplayName == "Rašanti Atstovė").Id;
        using var lifted = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.LiftRestriction, restrictionId), new StatementRequest { Statement = "Bandymas baigtas." });
        lifted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Anyone_signed_in_reports_an_organisation_or_a_statement_as_impersonation_and_palikti_closes_it()
    {
        var moderator = (await ModeratorAsync()).Browser;
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        var saved = await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Tekstas pranešimui.");
        await moderator.ActAsync(OrganisationRoutes.Keep, created.Id, null);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        foreach (var (kind, id) in new[] { ("organisation", created.Id), ("statement", saved.Id) })
        {
            using var reported = await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = kind, TargetId = id, Ground = "impersonation", Text = "Panašu į kitą." });
            reported.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reported.Content.ReadAsStringAsync());
        }

        using (var argumentGround = await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = saved.Id, Ground = "impersonation" }))
        {
            argumentGround.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "an argument is not reported as impersonation");
        }

        var queue = await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue);
        queue.Organisations.Single(o => o.Id == created.Id).Reports.ShouldHaveSingleItem().Ground.ShouldBe("impersonation");
        queue.Statements.Single(s => s.Id == saved.Id).Reports.ShouldHaveSingleItem();
        await moderator.ActAsync(OrganisationRoutes.Keep, created.Id, null);
        using (var kept = await moderator.PostAsync(OrganisationRoutes.With(OrganisationRoutes.PublishStatement, saved.Id), new ModerationRequest()))
        {
            kept.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        queue = await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue);
        queue.Organisations.ShouldNotContain(o => o.Id == created.Id);
        queue.Statements.ShouldNotContain(s => s.Id == saved.Id);
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("kept");
        (await Database.OrganisationNoticesAsync(email)).ShouldContain("statement-kept");

        using var visitor = NewBrowser();
        using var anonymous = await visitor.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "organisation", TargetId = created.Id, Ground = "impersonation" });
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deleting_the_last_representatives_account_closes_the_organisation_and_empties_who_wrote()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync();
        var created = await member.RegisterOrganisationAsync();
        await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Pozicija prieš ištrinant paskyrą.");
        using (var export = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export)))
        {
            var appeals = export.RootElement.GetProperty("appeals");
            appeals.GetProperty("organisationRoles")[0].GetProperty("slug").GetString().ShouldBe(created.Slug);
            appeals.GetProperty("organisationStatements")[0].GetProperty("text").GetString().ShouldBe("Pozicija prieš ištrinant paskyrą.");
        }
        var userId = await Database.UserIdAsync(email);
        using (var deleted = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        member.Dispose();
        using var visitor = NewBrowser();
        (await visitor.ProfileAsync(created.Slug)).Status.ShouldBe("closed");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.organisation_representative WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.organisation_statement WHERE written_by_user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.organisation WHERE registered_by_user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<string>("SELECT status FROM appeals.organisation_statement WHERE organisation_id = @o", ("o", created.Id))).ShouldBe("withdrawn");
    }
}
