using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.14.2, 8.14.14 privacy, non-negotiable: who represents an organisation is told only to its other representatives
/// (display names), and to a moderator only through restricting a statement's writer. No public or member-facing answer,
/// page, email or log line names them or ties a representative's own side on a bill to the organisation; a statement's
/// writer is in no public answer.
/// </summary>
public sealed class OrganisationPrivacyTests(DatabaseFixture database) : ApiTest(database)
{
    private const string RepresentativeName = "Slapta Atstovė Pavyzdytė";
    private const string SecondName = "Kitas Slaptas Atstovas";

    [Fact]
    public async Task No_public_answer_page_or_staff_screen_names_a_representative_or_who_wrote_a_statement()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (rep, repEmail) = await Factory.MemberAsync(RepresentativeName);
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        var invite = await (await rep.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { })).ReadAsync<InviteCreatedResponse>();
        var (second, secondEmail) = await Factory.MemberAsync(SecondName);
        using var _s = second;
        (await second.PostAsync(OrganisationRoutes.AcceptInviteOf(invite.Token), new { })).Dispose();
        var saved = await second.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, "for", "Pozicija privatumo bandymui.");
        var (reporter, _) = await Factory.MemberAsync();
        using var _rp = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "statement", TargetId = saved.Id, Ground = "impersonation" })).Dispose();

        var secrets = new[]
        {
            RepresentativeName, SecondName, repEmail, secondEmail,
            (await Database.UserIdAsync(repEmail)).ToString(), (await Database.UserIdAsync(secondEmail)).ToString(),
        };

        using var visitor = NewBrowser();
        var (stranger, _) = await Factory.MemberAsync();
        using var _st = stranger;
        var publicRoutes = new[]
        {
            OrganisationRoutes.List(null), OrganisationRoutes.Of(created.Slug), OrganisationRoutes.OnBillOf(AppealSteps.SecondBill),
            AppealRoutes.AppealOf(AppealSteps.SecondBill), "/o/" + created.Slug, "/organizacijos", "/seimas/" + AppealSteps.SecondBill,
        };
        foreach (var browser in new[] { visitor, stranger })
        {
            foreach (var route in publicRoutes)
            {
                var body = WebUtility.HtmlDecode(await browser.Client.GetStringAsync(route));
                foreach (var secret in secrets)
                {
                    body.ShouldNotContain(secret, Case.Insensitive, route);
                }
            }

            // A member who is not a representative gets the empty management answer.
            if (browser == stranger)
            {
                var management = await stranger.Client.GetStringAsync(OrganisationRoutes.ManagementOf(created.Slug));
                foreach (var secret in secrets)
                {
                    management.ShouldNotContain(secret, Case.Insensitive);
                }

                (await stranger.Client.GetStringAsync(OrganisationRoutes.MineOnBillOf(AppealSteps.SecondBill))).ShouldBe("[]");
            }
        }

        // Staff screens: the queue, the panel, the hidden list, the bill's moderation state and the restrictions name nobody;
        // only the writer's screen, reached through the statement, does.
        foreach (var route in new[]
        {
            OrganisationRoutes.ModerationQueue, OrganisationRoutes.ModerationBillOf(AppealSteps.SecondBill), OrganisationRoutes.ModerationHidden,
            AppealRoutes.ModerationQueue, AppealRoutes.ModerationBillOf(AppealSteps.SecondBill), AppealRoutes.Hidden, "/moderavimas",
        })
        {
            var body = WebUtility.HtmlDecode(await moderator.Browser.Client.GetStringAsync(route));
            foreach (var secret in secrets)
            {
                body.ShouldNotContain(secret, Case.Insensitive, route);
            }
        }

        var writer = await moderator.Browser.GetJsonAsync<AuthorViewResponse>(OrganisationRoutes.With(OrganisationRoutes.StatementWriter, saved.Id));
        writer.DisplayName.ShouldBe(SecondName);

        // Representatives see each other's names, and nothing else of each other.
        var seen = await rep.Client.GetStringAsync(OrganisationRoutes.ManagementOf(created.Slug));
        seen.ShouldContain(SecondName);
        seen.ShouldNotContain(secondEmail, Case.Insensitive);
        seen.ShouldNotContain(secrets[5], Case.Insensitive);

        // The public statement carries no writer field at all.
        using var document = JsonDocument.Parse(await visitor.Client.GetStringAsync(OrganisationRoutes.OnBillOf(AppealSteps.SecondBill)));
        document.RootElement.ToString().ShouldNotContain("writtenBy", Case.Insensitive);
        document.RootElement.ToString().ShouldNotContain("registeredBy", Case.Insensitive);
    }

    [Fact]
    public async Task A_representatives_own_side_changes_nothing_any_answer_says_about_the_organisation()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ClearStatementsAsync(AppealSteps.SecondBill);
        var (rep, _) = await Factory.MemberAsync(RepresentativeName);
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        await rep.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, "for", "Organizacija prašo balsuoti už.", goal: 50);

        using var visitor = NewBrowser();
        var routes = new[] { OrganisationRoutes.OnBillOf(AppealSteps.SecondBill), OrganisationRoutes.Of(created.Slug), OrganisationRoutes.List(null) };
        var before = new Dictionary<string, string>();
        foreach (var route in routes)
        {
            before[route] = await visitor.Client.GetStringAsync(route);
        }

        var mineBefore = await rep.Client.GetStringAsync(OrganisationRoutes.MineOnBillOf(AppealSteps.SecondBill));
        var managementBefore = await rep.Client.GetStringAsync(OrganisationRoutes.ManagementOf(created.Slug));

        // The representative takes the other side as a person: nothing about the organisation moves, except the goal's
        // count of participants, which counts everyone on the side, the representative included like anyone.
        (await rep.TakeSideAsync(AppealSteps.SecondBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        foreach (var route in routes)
        {
            (await visitor.Client.GetStringAsync(route)).ShouldBe(before[route], route);
        }

        (await rep.Client.GetStringAsync(OrganisationRoutes.MineOnBillOf(AppealSteps.SecondBill))).ShouldBe(mineBefore, "the caller's own answer about the organisation holds no side of the person");
        (await rep.Client.GetStringAsync(OrganisationRoutes.ManagementOf(created.Slug))).ShouldBe(managementBefore);
        mineBefore.ShouldNotContain("\"side\"", Case.Insensitive);
    }

    [Fact]
    public async Task A_notice_email_goes_to_each_representative_and_names_nobody_else()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var (rep, repEmail) = await Factory.MemberAsync(RepresentativeName);
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        var invite = await (await rep.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { })).ReadAsync<InviteCreatedResponse>();
        var (second, secondEmail) = await Factory.MemberAsync(SecondName);
        using var _s = second;
        (await second.PostAsync(OrganisationRoutes.AcceptInviteOf(invite.Token), new { })).Dispose();
        await moderator.Browser.ActAsync(OrganisationRoutes.Block, created.Id, "Bandymas.");
        var payloads = await Database.ScalarAsync<string>(
            "SELECT string_agg(payload::text, '\n') FROM notify.outbox WHERE kind = 'email.organisation-notice' AND payload->>'organisationName' = @n",
            ("n", (await rep.ProfileAsync(created.Slug)).Name));
        var lines = payloads.ShouldNotBeNull().Split('\n');
        lines.Length.ShouldBe(2, "one email per representative");
        lines.Single(l => l.Contains(repEmail, StringComparison.OrdinalIgnoreCase)).ShouldNotContain(secondEmail, Case.Insensitive);
        lines.Single(l => l.Contains(secondEmail, StringComparison.OrdinalIgnoreCase)).ShouldNotContain(repEmail, Case.Insensitive);
        payloads.ShouldNotContain(RepresentativeName);
        payloads.ShouldNotContain(SecondName);
        payloads.ShouldContain("moderatoriai@lt2.test", Case.Sensitive, "with the moderators' contact address");
        await moderator.Browser.ActAsync(OrganisationRoutes.Unblock, created.Id, "Bandymas baigtas.");
    }

    [Fact]
    public async Task Member_actions_on_organisations_leave_no_audit_row_tying_a_person_to_an_organisation()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (rep, repEmail) = await Factory.MemberAsync();
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        await rep.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "remarks", "Pastabos audito bandymui.");
        var userId = await Database.UserIdAsync(repEmail);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE (actor_user_id = @u AND (action LIKE '%organisation%' OR action LIKE '%statement%')) OR target LIKE @t",
            ("u", userId), ("t", "%" + created.Id + "%"))).ShouldBe(0);

        // A profile's and a statement's public history carries no person (appeals.revision is readable for analysis).
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.revision WHERE entity_kind IN ('organisation', 'statement') AND changed_by_user_id IS NOT NULL")).ShouldBe(0);
    }

    [Theory]
    [InlineData("appeals.organisation")]
    [InlineData("appeals.organisation_representative")]
    [InlineData("appeals.organisation_statement")]
    [InlineData("appeals.organisation_code")]
    [InlineData("appeals.organisation_invite")]
    public async Task The_read_only_role_cannot_read_the_tables_that_hold_an_account_beside_an_organisation(string table)
    {
        await using var connection = new NpgsqlConnection(Database.ReadonlyConnectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100 // A fixed list of table names from the test.
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
#pragma warning restore CA2100
        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());
        error.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }
}
