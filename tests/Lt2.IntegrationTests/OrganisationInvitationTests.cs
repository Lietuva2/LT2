using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.14.14, stage B: the organisation's invitation code. One per organisation, made when a representative first asks,
/// seen only by its representatives with a link per bill and one number; it belongs to the organisation and follows its
/// state (blocked off, lifted on, closed off for good; an administrator's switch-off survives a lifted block). The core
/// privacy rule: nothing on a new account, its pending state, its export, the audit log, the administrators' page or the
/// logs leads from the account to the organisation's code.
/// </summary>
public sealed class OrganisationInvitationTests(DatabaseFixture database) : ApiTest(database)
{
    private static Task<OrganisationInvitationResponse> CodeAsync(TestBrowser representative, string slug) =>
        representative.GetJsonAsync<OrganisationInvitationResponse>(OrganisationRoutes.InvitationOf(slug));

    /// <summary>A new person registers with <paramref name="code"/> and completes as the browser does: the code is sent again at completion.</summary>
    private async Task<(TestBrowser Browser, string Email)> RegisterWithAsync(string code, string name = "Atėjusi Per Organizaciją")
    {
        var browser = NewBrowser();
        var email = TestData.Email();
        var signInCode = await browser.RegisterAsync(email, code);
        await browser.SignInWithCodeAsync(email, signInCode);
        using var completed = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = name, ConfirmsAdult = true, AcceptsTerms = true, InvitationCode = code });
        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync());
        return (browser, email);
    }

    private static async Task<HttpResponseMessage> TryRegisterAsync(TestBrowser browser, string code) =>
        await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = TestData.Email(), InvitationCode = code });

    [Fact]
    public async Task Representatives_see_one_code_with_a_link_per_bill_and_one_number_and_nobody_else_sees_anything_of_it()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (rep, _) = await Factory.MemberAsync("Kodo Atstovė");
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        await rep.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Pozicija kvietimo nuorodai.");

        var code = await CodeAsync(rep, created.Slug);
        var theCode = code.Code!;
        theCode.ShouldMatch("^[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$");
        code.Link.ShouldBe("/prisijungti#kodas=" + code.Code);
        (code.Completed, code.Usable, code.On, code.OffWithOrganisation, code.Contact).ShouldBe((0, true, true, false, (string?)null));
        var bill = code.Bills.Single();
        bill.Bill.Number.ShouldBe(AppealSteps.ThirdBill);
        bill.Link.ShouldBe($"/prisijungti#kodas={code.Code}&tikslas=%2Fseimas%2F{AppealSteps.ThirdBill}");

        // One code per organisation: a second representative gets the same one, and asking again makes no new one.
        var invite = await (await rep.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { })).ReadAsync<InviteCreatedResponse>();
        var (second, _) = await Factory.MemberAsync("Antras Kodo Atstovas");
        using var _s = second;
        (await second.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token })).Dispose();
        (await CodeAsync(second, created.Slug)).Code.ShouldBe(code.Code);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.invitation WHERE kind = 'organisation'"
            + " AND organisation_id = (SELECT id FROM appeals.organisation WHERE slug = @s)", ("s", created.Slug))).ShouldBe(1);

        // It belongs to the organisation: no account on it.
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.invitation WHERE code = @c AND issued_by_user_id IS NULL", ("c", theCode))).ShouldBe(1);

        // Not a representative: refused; a visitor: signed out.
        var (stranger, _) = await Factory.MemberAsync();
        using var _st = stranger;
        using (var refused = await stranger.Client.GetAsync(OrganisationRoutes.InvitationOf(created.Slug)))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "organisation.representatives-only");
        }

        using var visitor = NewBrowser();
        (await visitor.Client.GetAsync(OrganisationRoutes.InvitationOf(created.Slug))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Nothing of the code or its number in any public answer or prerendered page, the representative's own included.
        foreach (var browser in new[] { visitor, stranger, rep })
        {
            foreach (var route in new[] { OrganisationRoutes.Of(created.Slug), "/o/" + created.Slug, OrganisationRoutes.OnBillOf(AppealSteps.ThirdBill), "/seimas/" + AppealSteps.ThirdBill, OrganisationRoutes.ManagementOf(created.Slug) })
            {
                using var answer = await browser.Client.GetAsync(route);
                var body = WebUtility.HtmlDecode(await answer.Content.ReadAsStringAsync());
                body.ShouldNotContain(theCode, Case.Insensitive, route);
                body.ShouldNotContain(theCode.Replace("-", "", StringComparison.Ordinal), Case.Insensitive, route);
                body.ShouldNotContain("kodas=", Case.Insensitive, route);
            }
        }

        using var raw = await rep.Client.GetAsync(OrganisationRoutes.InvitationOf(created.Slug));
        raw.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Registering_with_an_organisations_code_leaves_nothing_on_the_new_account_that_names_the_code_or_the_organisation()
    {
        var admin = (await AdministratorAsync()).Browser;
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        const string OrganisationName = "Slaptoji Narystės Sąjunga";
        var (rep, _) = await logged.MemberAsync("Sąjungos Atstovė");
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync(OrganisationSteps.NewName(OrganisationName));
        var code = (await CodeAsync(rep, created.Slug)).Code!;
        var organisationId = created.Id.ToString();
        var level = await Database.ScalarAsync<int>("SELECT level FROM identity.invitation WHERE code = @c", ("c", code));

        // Registering: the pending account holds no code at all.
        var newcomer = logged.NewBrowser();
        using var _n = newcomer;
        var email = TestData.Email();
        var signInCode = await newcomer.RegisterAsync(email, code);
        var userId = await Database.UserIdAsync(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE user_id = @u AND invitation_code IS NULL AND invitation_id IS NULL AND invitation_level IS NULL", ("u", userId))).ShouldBe(1);

        // Completing without the code (another device) asks for it again; with it, it counts.
        await newcomer.SignInWithCodeAsync(email, signInCode);
        using (var without = await newcomer.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = "Naujas Narys", ConfirmsAdult = true, AcceptsTerms = true }))
        {
            await without.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
        }

        using (var with = await newcomer.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = "Naujas Narys", ConfirmsAdult = true, AcceptsTerms = true, InvitationCode = code }))
        {
            with.StatusCode.ShouldBe(HttpStatusCode.NoContent, await with.Content.ReadAsStringAsync());
        }

        // The representatives' number and the code's daily total rose by one.
        (await CodeAsync(rep, created.Slug)).Completed.ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT coalesce(sum(d.completed), 0) FROM identity.invitation_daily d JOIN identity.invitation i ON i.id = d.invitation_id WHERE i.code = @c", ("c", code))).ShouldBe(1);

        // The completed profile keeps only the level it would have had.
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.profile WHERE user_id = @u AND invitation_code IS NULL AND invitation_id IS NULL AND invitation_level = @l",
            ("u", userId), ("l", level))).ShouldBe(1);

        // Every row about the account, in every table that has the account, says nothing of the code or the organisation.
        var everything = await Database.ScalarAsync<string>(
            """
            SELECT concat_ws(' ',
                (SELECT row_to_json(p)::text FROM identity.profile p WHERE p.user_id = @u),
                (SELECT row_to_json(x)::text FROM identity."user" x WHERE x.id = @u),
                (SELECT coalesce(json_agg(c)::text, '') FROM identity.consent c WHERE c.user_id = @u),
                (SELECT coalesce(json_agg(s)::text, '') FROM identity.sign_in_code s WHERE s.user_id = @u),
                (SELECT coalesce(json_agg(i)::text, '') FROM identity.invitation i WHERE i.issued_by_user_id = @u),
                (SELECT coalesce(json_agg(e)::text, '') FROM audit.event e WHERE e.actor_user_id = @u OR e.target LIKE '%' || @t || '%'),
                (SELECT coalesce(json_agg(o)::text, '') FROM notify.outbox o WHERE o.payload->>'to' = @e))
            """,
            ("u", userId), ("t", userId.ToString()), ("e", email));
        foreach (var secret in new[] { code, code.Replace("-", "", StringComparison.Ordinal), organisationId, created.Slug, OrganisationName })
        {
            everything!.ShouldNotContain(secret, Case.Insensitive);
        }

        // The person's own export: no code, no organisation; their own personal code is theirs.
        var export = await newcomer.Client.GetStringAsync(ApiRoutes.Me.Export);
        foreach (var secret in new[] { code, organisationId, created.Slug, OrganisationName })
        {
            export.ShouldNotContain(secret, Case.Insensitive);
        }

        using (var document = JsonDocument.Parse(export))
        {
            var account = document.RootElement.GetProperty("account");
            account.GetProperty("invitationCode").ValueKind.ShouldBe(JsonValueKind.Null);
            account.GetProperty("invitationLevel").GetInt32().ShouldBe(level);
        }

        // The administrators' page names the organisation's code by the organisation and shows numbers only.
        var stats = await admin.GetJsonAsync<InvitationStatsResponse>(ApiRoutes.Admin.Invitations + "?code=" + code);
        var row = stats.Codes.Single();
        (row.Kind, row.Label, row.Completed, row.On).ShouldBe(("organisation", (await rep.ProfileAsync(created.Slug)).Name, 1, true));
        var statsJson = await admin.Client.GetStringAsync(ApiRoutes.Admin.Invitations + "?code=" + code);
        statsJson.ShouldNotContain(email, Case.Insensitive);
        statsJson.ShouldNotContain(userId.ToString(), Case.Insensitive);
        statsJson.ShouldNotContain("Naujas Narys");

        // No log line carries the code, in any spelling.
        foreach (var line in sink.Lines)
        {
            line.ShouldNotContain(code, Case.Insensitive);
            line.ShouldNotContain(code.Replace("-", "", StringComparison.Ordinal), Case.Insensitive);
        }
    }

    [Fact]
    public async Task A_blocked_organisations_code_is_refused_with_the_usual_message_and_an_administrators_switch_off_survives_a_lifted_block()
    {
        var (admin, moderator) = ((await AdministratorAsync()).Browser, (await ModeratorAsync()).Browser);
        var (rep, _) = await Factory.MemberAsync("Blokuojamos Atstovas");
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        var code = (await CodeAsync(rep, created.Slug)).Code!;
        var invitationId = await Database.ScalarAsync<Guid>("SELECT id FROM identity.invitation WHERE code = @c", ("c", code));
        using var stranger = NewBrowser();

        await moderator.ActAsync(OrganisationRoutes.Block, created.Id, "Apsimeta kita organizacija.");
        using (var refused = await TryRegisterAsync(stranger, code))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        var blocked = await CodeAsync(rep, created.Slug);
        (blocked.Usable, blocked.OffWithOrganisation, blocked.On).ShouldBe((false, true, true));
        blocked.Bills.ShouldBeEmpty();
        using (var replace = await rep.PostAsync(OrganisationRoutes.ReplaceInvitationOf(created.Slug), new { }))
        {
            await replace.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invitation.organisation-inactive");
        }

        // The administrators' page says it is off with its organisation.
        (await admin.GetJsonAsync<InvitationStatsResponse>(ApiRoutes.Admin.Invitations + "?code=" + code)).Codes.Single().OrganisationOff.ShouldBe("blocked");

        // An administrator switches it off too; lifting the block does not undo that.
        (await admin.PostAsync(ApiRoutes.Admin.SwitchInvitation(invitationId, on: false), new StatementRequest { Statement = "Platinama viešai." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await moderator.ActAsync(OrganisationRoutes.Unblock, created.Id, "Patikrinta.");
        var lifted = await CodeAsync(rep, created.Slug);
        (lifted.Usable, lifted.OffWithOrganisation, lifted.On, lifted.Contact).ShouldBe((false, false, false, "moderatoriai@lt2.test"));
        using (var refused = await TryRegisterAsync(stranger, code))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        using (var replace = await rep.PostAsync(OrganisationRoutes.ReplaceInvitationOf(created.Slug), new { }))
        {
            await replace.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invitation.organisation-switched-off");
        }

        // Switched on by the administrator: it works again.
        (await admin.PostAsync(ApiRoutes.Admin.SwitchInvitation(invitationId, on: true), new StatementRequest { Statement = "Gerai." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CodeAsync(rep, created.Slug)).Usable.ShouldBeTrue();
        var (newcomer, _) = await RegisterWithAsync(code);
        using var _n = newcomer;
        (await CodeAsync(rep, created.Slug)).Completed.ShouldBe(1);

        // A block and its lifting alone (no administrator) switch the code off and on again.
        await moderator.ActAsync(OrganisationRoutes.Block, created.Id, "Dar kartą.");
        (await CodeAsync(rep, created.Slug)).Usable.ShouldBeFalse();
        await moderator.ActAsync(OrganisationRoutes.Unblock, created.Id, "Vėl gerai.");
        (await CodeAsync(rep, created.Slug)).Usable.ShouldBeTrue();
    }

    [Fact]
    public async Task The_code_stays_when_its_asker_leaves_or_deletes_the_account_and_closing_stops_it_for_good()
    {
        var (asker, askerEmail) = await Factory.MemberAsync("Paklaususi Atstovė");
        using var _a = asker;
        var askerId = await Database.UserIdAsync(askerEmail);
        var created = await asker.RegisterOrganisationAsync();
        var code = (await CodeAsync(asker, created.Slug)).Code!;
        var invite = await (await asker.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { })).ReadAsync<InviteCreatedResponse>();
        var (other, _) = await Factory.MemberAsync("Likęs Atstovas");
        using var _o = other;
        (await other.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token })).Dispose();

        // The one who asked for it deletes the account: the code keeps working.
        (await asker.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.invitation WHERE code = @c AND switched_off_at IS NULL AND organisation_off IS NULL", ("c", code))).ShouldBe(1);
        (await CodeAsync(other, created.Slug)).Code.ShouldBe(code);
        var (first, _) = await RegisterWithAsync(code);
        using var _f = first;
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.invitation WHERE issued_by_user_id = @u", ("u", askerId))).ShouldBe(0);

        // Closing switches it off for good; the number stays.
        (await other.PostAsync(OrganisationRoutes.CloseOf(created.Slug), new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<string>("SELECT organisation_off FROM identity.invitation WHERE code = @c", ("c", code))).ShouldBe("closed");
        using var stranger = NewBrowser();
        using (var refused = await TryRegisterAsync(stranger, code))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        (await Database.ScalarAsync<int>("SELECT used_count FROM identity.invitation WHERE code = @c", ("c", code))).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_last_representatives_account_closes_the_organisation_and_its_code()
    {
        var (rep, _) = await Factory.MemberAsync("Paskutinė Atstovė");
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        var code = (await CodeAsync(rep, created.Slug)).Code!;
        (await rep.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<string>("SELECT status FROM appeals.organisation WHERE id = @o", ("o", created.Id))).ShouldBe("closed");
        (await Database.ScalarAsync<string>("SELECT organisation_off FROM identity.invitation WHERE code = @c", ("c", code))).ShouldBe("closed");
    }

    [Fact]
    public async Task Replacing_the_code_stops_the_old_one_and_the_number_carries_over_and_a_setting_limits_each_code()
    {
        var admin = (await AdministratorAsync()).Browser;
        var (rep, _) = await Factory.MemberAsync("Keičianti Atstovė");
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        var old = (await CodeAsync(rep, created.Slug)).Code!;
        var (one, _) = await RegisterWithAsync(old);
        using var _o = one;

        var replaced = await (await rep.PostAsync(OrganisationRoutes.ReplaceInvitationOf(created.Slug), new { })).ReadAsync<OrganisationInvitationResponse>();
        replaced.Code.ShouldNotBe(old);
        replaced.Completed.ShouldBe(1, "the number carries over");
        using (var stale = NewBrowser())
        using (var refused = await TryRegisterAsync(stale, old))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        await admin.SetSettingAsync("Registration:OrganisationCodeUses", 1);
        try
        {
            var (two, _) = await RegisterWithAsync(replaced.Code!);
            using var _t = two;
            var full = await CodeAsync(rep, created.Slug);
            (full.Completed, full.Usable).ShouldBe((2, false));
            using var third = NewBrowser();
            using var refused = await TryRegisterAsync(third, replaced.Code!);
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");

            // The personal codes' setting does not limit organisations' codes, nor the other way round.
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            var personal = await member.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation);
            personal.Usable.ShouldBeTrue();
        }
        finally
        {
            await admin.ResetSettingAsync("Registration:OrganisationCodeUses");
        }
    }
}
