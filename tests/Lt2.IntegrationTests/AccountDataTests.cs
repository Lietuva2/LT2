using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Feedback;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 6.5: a person downloads everything they gave and deletes the account.</summary>
public sealed class AccountDataTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task The_export_holds_everything_the_person_gave_and_nothing_of_anyone_else()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (initiator, initiatorEmail) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var othersReason = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single();
        var (member, email) = await Factory.MemberAsync("Giedrė L.");
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Mano paties argumentas eksportui.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(othersReason.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = "Reformų partija", Consent = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await member.Client.GetAsync(ApiRoutes.Me.Export);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var json = await response.Content.ReadAsStringAsync();
        using var export = JsonDocument.Parse(json);
        var root = export.RootElement;
        root.GetProperty("account").GetProperty("email").GetString().ShouldBe(email);
        root.GetProperty("partySupport").GetProperty("party").GetString().ShouldBe("Reformų partija");
        root.GetProperty("consents").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ShouldBe(["age-18-or-older", "positions", "party-support"], ignoreOrder: true);
        var appeals = root.GetProperty("appeals");
        appeals.GetProperty("positions").EnumerateArray().Single().GetProperty("side").GetString().ShouldBe("against");
        appeals.GetProperty("reasons").EnumerateArray().Single().GetProperty("text").GetString().ShouldBe("Mano paties argumentas eksportui.");
        appeals.GetProperty("endorsements").EnumerateArray().Single().GetProperty("reason").GetGuid().ShouldBe(othersReason.Id);

        // Nothing of the initiator: not their address, name, side or words.
        json.ShouldNotContain(initiatorEmail);
        json.ShouldNotContain("Rūta Mockutė");
        json.ShouldNotContain(AppealSteps.AgainstReason);
        json.ShouldNotContain((await Database.UserIdAsync(initiatorEmail)).ToString());

        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(ApiRoutes.Me.Export)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deleting_the_account_removes_positions_marks_consents_and_counts_but_not_frozen_summaries_and_logs_nothing()
    {
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        using var admin = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        await admin.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);

        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            var reasonId = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
            var (member, email) = await logged.MemberAsync("Ištrinamas Narys");
            using var _m = member;
            var userId = await Database.UserIdAsync(email);
            (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Šis argumentas dings su paskyra.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = "Reformų partija", Consent = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.RecordAsync(AppealSteps.ThirdBill)).Counts.Against.ShouldBe(2);

            using var urgent = await moderator.Browser.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, record.Id), new UrgentDigestRequest { Statement = "Prieš ištrynimą." });
            var frozen = await urgent.ReadAsync<DigestRunResponse>();
            frozen.Items.Single().Against.ShouldBe(2);

            using (var deleted = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root))
            {
                deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            foreach (var (table, column) in new[]
            {
                ("appeals.position", "user_id"), ("appeals.endorsement", "user_id"), ("appeals.reason", "author_user_id"), ("appeals.writing_restriction", "user_id"), ("identity.consent", "user_id"),
                ("identity.party_support", "user_id"), ("identity.profile", "user_id"), ("identity.session", "user_id"), ("identity.\"user\"", "id"),
            })
            {
                (await Database.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {column} = @u", ("u", userId))).ShouldBe(0, table);
            }

            using var stranger = NewBrowser();
            var after = await stranger.RecordAsync(AppealSteps.ThirdBill);
            after.Counts.Against.ShouldBe(1, "the count falls");
            after.Reasons.Single().Endorsements.ShouldBe(0);
            (await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(frozen.RunDate))).Single(r => r.Id == frozen.Id).Items.Single().Against
                .ShouldBe(2, "a frozen summary keeps its totals");
            (await member.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "signed out");

            foreach (var line in sink.Lines)
            {
                line.ShouldNotContain(email, Case.Insensitive);
                line.ShouldNotContain("Ištrinamas Narys");
            }
        }
        finally
        {
            await admin.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
        }
    }

    /// <summary>Design 6.8: the outbox forgets a deleted account: nothing addressed to it waits or stays failed.</summary>
    [Fact]
    public async Task Deleting_the_account_leaves_no_email_to_its_address_in_the_outbox()
    {
        var (member, email) = await Factory.MemberAsync("Pamirštamas Narys");
        using var _m = member;
        var (other, otherEmail) = await Factory.MemberAsync();
        using var _o = other;
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @e", ("e", email))).ShouldBeGreaterThan(0, "the sign-in code is still waiting");
        await Database.ExecuteAsync(
            """
            INSERT INTO notify.outbox (id, kind, payload, created_at, due_at, attempts, failed_at, last_error)
            VALUES (gen_random_uuid(), 'email.feedback-reply', jsonb_build_object('to', upper(@e), 'reply', 'Ačiū', 'excerpt', 'x'), now(), now(), 8, now(), 'SmtpCommandException 550')
            """,
            ("e", email));

        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE lower(payload->>'to') = lower(@e)", ("e", email))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @e", ("e", otherEmail))).ShouldBeGreaterThan(0, "nobody else's mail");
    }

    /// <summary>Design 6.5, 8.3: an argument deleted with its author's account leaves no open report behind, as a withdrawn one.</summary>
    [Fact]
    public async Task Deleting_the_account_closes_the_open_reports_on_its_arguments()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        await Database.PrepareBillAsync(author, moderator.Browser, AppealSteps.ThirdBill, "against");
        var reasonId = (await author.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await author.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.reason WHERE id = @r", ("r", reasonId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.report WHERE target_id = @r AND status = 'open'", ("r", reasonId))).ShouldBe(0);
    }

    /// <summary>
    /// Design 6.5, 6.8: after an account is deleted, no column anywhere holds its id and no email waits for its address.
    /// The sweep reads every column whose name ends in <c>user_id</c> in every module's schema, so a table added later is
    /// swept without changing this test. Only the append-only audit log keeps who acted (design 5.5).
    /// </summary>
    [Fact]
    public async Task Deleting_the_account_leaves_its_id_in_no_column_of_any_table_and_no_mail_to_its_address()
    {
        var moderator = await ModeratorAsync();
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        await Database.PrepareBillAsync(other, moderator.Browser, AppealSteps.SecondBill, "for");
        var othersReason = (await other.RecordAsync(AppealSteps.SecondBill)).Reasons.Single().Id;

        // The person does everything a member can: a side, an argument, a mark, a report, follows of every kind, the email
        // switches, party support, feedback that may be answered, and an organisation they represent.
        var (person, email) = await Factory.MemberAsync("Visur Pabuvusi");
        var userId = await Database.UserIdAsync(email);
        (await person.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.WriteReasonAsync(AppealSteps.SecondBill, "for", "Visur pabuvusios argumentas, kuris dings su paskyra.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await person.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(othersReason))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = othersReason, Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await person.FollowBillAsync(AppealSteps.SecondBill);
        await person.FollowMpAsync(ReasonsToComeSteps.Petraitis);
        var (rep, _) = await Factory.MemberAsync();
        using var _r = rep;
        var followed = await rep.RegisterOrganisationAsync();
        (await person.SendAsync(HttpMethod.Put, FollowRoutes.OrganisationOf(followed.Slug))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = true, Voted = false, Statements = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = "Reformų partija", Consent = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await person.PostAsync(FeedbackRoutes.Send, new FeedbackRequest { Text = "Atsiliepimas, į kurį galima atsakyti.", PagePath = "/sveiki", MayReply = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var own = await person.RegisterOrganisationAsync();
        await person.PublishStatementAsync(AppealSteps.SecondBill, own.Slug, "remarks", "Organizacijos pastabos, kurių rašytojas išnyks.");

        (await person.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var columns = await Database.ScalarAsync<string>(
            """
            SELECT string_agg(format('%I.%I.%I', table_schema, table_name, column_name), ',')
            FROM information_schema.columns
            WHERE table_schema IN ('identity', 'appeals', 'notify', 'audit', 'config', 'parliament') AND column_name LIKE '%user_id'
              AND data_type = 'uuid' AND format('%I.%I', table_schema, column_name) <> 'audit.actor_user_id'
            """);
        var holding = new List<string>();
        foreach (var column in columns!.Split(','))
        {
            var parts = column.Split('.');
#pragma warning disable CA2100 // Names read from the catalogue and quoted by format('%I').
            if (await Database.ScalarAsync<long>($"SELECT count(*) FROM {parts[0]}.{parts[1]} WHERE {parts[2]} = @u", ("u", userId)) > 0)
#pragma warning restore CA2100
            {
                holding.Add(column);
            }
        }

        holding.ShouldBeEmpty("no column holds the deleted account's id");
        columns.Split(',').Length.ShouldBeGreaterThan(20, "the sweep found the modules' columns");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE lower(payload->>'to') = lower(@e)", ("e", email))).ShouldBe(0, "design 6.8: the outbox forgets");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.\"user\" WHERE email = @e", ("e", email))).ShouldBe(0);
    }
}