using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, 8.12.3 over HTTP: moderation after the fact, the queue and the actions. Each needs an active moderator role
/// (a staff-passkey session), is audited, and tells an argument's author why by email through the outbox.
/// </summary>
public sealed class ModerationApiTests(DatabaseFixture database) : ApiTest(database)
{
    private Task<long> NoticesAsync(string email, string notice) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' = @n",
            ("to", email), ("n", notice));

    private Task<long> AuditAsync(string action, Guid target) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = @a AND target = @t AND actor_role = 'moderator' AND ip_hash IS NOT NULL",
            ("a", action), ("t", action.Split('.')[1] + ":" + target));

    [Fact]
    public async Task Only_an_active_moderator_in_a_passkey_session_sees_the_queue()
    {
        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(AppealRoutes.ModerationQueue)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using (var refused = await member.Client.GetAsync(AppealRoutes.ModerationQueue))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        var moderator = await ModeratorAsync();
        using var codeSession = NewBrowser();
        await codeSession.PostAsync(Lt2.Api.Contracts.ApiRoutes.Auth.Code, new Lt2.Api.Contracts.Auth.RequestCodeRequest { Email = moderator.Email });
        await codeSession.SignInWithCodeAsync(moderator.Email);
        using (var byCode = await codeSession.Client.GetAsync(AppealRoutes.ModerationQueue))
        {
            await byCode.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using var keep = await codeSession.PostAsync(AppealRoutes.With(AppealRoutes.KeepRecord, Guid.NewGuid()), new ModerationRequest());
        await keep.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");

        (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Nothing_waits_for_approval_a_bill_nobody_reported_is_not_in_the_queue()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill, "against");
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.ShouldNotContain(a => a.Id == record.Id);
    }

    [Fact]
    public async Task Hiding_a_bills_record_closes_taking_part_with_the_statement_on_its_page_and_keeps_sides_unseen()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill);

        using (var noStatement = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, record.Id), new StatementRequest { Statement = " " }))
        {
            await noStatement.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, record.Id), new StatementRequest { Statement = "Neteisėtas turinys." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        var hidden = await stranger.RecordAsync(AppealSteps.ThirdBill);
        hidden.Status.ShouldBe("rejected");
        hidden.HiddenStatement.ShouldBe("Neteisėtas turinys.");
        hidden.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0), "nothing of the record is shown");
        hidden.Reasons.ShouldBeEmpty();
        hidden.Description.ShouldBeEmpty();
        var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
        page.ShouldContain("id=\"taking-part-closed\"");
        page.ShouldContain("Neteisėtas turinys.");
        page.ShouldNotContain("id=\"side-picker\"");

        using (var side = await member.TakeSideAsync(AppealSteps.ThirdBill, "for"))
        {
            await side.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE record_id = @a", ("a", record.Id))).ShouldBe(1, "sides are kept, unseen");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = @b", ("b", AppealSteps.ThirdBill))).ShouldBe(1, "one record per bill: hiding frees nothing");
        (await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.Appeals)).Items.ShouldNotContain(i => i.Bill.Number == AppealSteps.ThirdBill);
        (await AuditAsync("moderation.record.hide", record.Id)).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' NOT LIKE 'reason-%'", ("to", email)))
            .ShouldBe(0, "a record has no author to email");
    }

    [Fact]
    public async Task A_threshold_set_by_a_moderator_is_public_with_its_reason()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill);
        using var stranger = NewBrowser();
        (await stranger.RecordAsync(AppealSteps.ThirdBill)).Threshold.ShouldBe(new ThresholdResponse(100, "default", null, 1, false, false));

        using (var below = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record.Id), new ModeratorThresholdRequest { Threshold = 1, Statement = "Per maža." }))
        {
            await below.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "threshold.below-minimum");
        }

        (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record.Id), new ModeratorThresholdRequest
        {
            Threshold = 300,
            Statement = "Bendra riba šiam projektui per maža.",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await stranger.RecordAsync(AppealSteps.ThirdBill)).Threshold.ShouldBe(new ThresholdResponse(300, "moderator", "Bendra riba šiam projektui per maža.", 1, false, false));
        (await AuditAsync("moderation.record.threshold", record.Id)).ShouldBe(1);
        (await NoticesAsync(email, "threshold-changed")).ShouldBe(0);
    }

    [Fact]
    public async Task A_moderator_writes_the_description_of_any_active_bill_creating_its_record_and_corrects_it_into_the_history()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var route = AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill);
        const string First = "Projektas keičia vietinių rinkliavų tvarką.";
        const string Corrected = "Projektas keičia vietinių rinkliavų tvarką: savivaldybės galės nustatyti rinkliavą už stovėjimą.";

        using (var byMember = await member.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Corrected, Statement = "Bandymas." }))
        {
            await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using (var noStatement = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Corrected, Statement = " " }))
        {
            await noStatement.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = First, Statement = "Projektas darbotvarkėje." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var stranger = NewBrowser();
        var described = await stranger.RecordAsync(AppealSteps.SecondBill);
        described.Id.ShouldNotBeNull("writing the description created the bill's record");
        described.Description.ShouldBe(First);
        described.DescriptionSource.ShouldBe("moderator");
        described.DescriptionAuthor.ShouldBeNull();
        described.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));

        using (var unchanged = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = First, Statement = "Be pakeitimų." }))
        {
            await unchanged.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "description.unchanged");
        }

        (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest
        {
            Description = "  " + Corrected + " ",
            Statement = "Aprašymas buvo netikslus.",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await stranger.RecordAsync(AppealSteps.SecondBill)).Description.ShouldBe(Corrected, "shown at once, trimmed, without waiting for anyone");
        var history = await stranger.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.SecondBill));
        history.Select(h => (h.OldText, h.NewText)).ShouldBe([(null, First), (First, Corrected)]);
        (await AuditAsync("moderation.record.describe", described.Id!.Value)).ShouldBe(2);
        var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill + "/istorija"));
        page.ShouldContain(Corrected);
        page.ShouldContain("aprašymo nebuvo");

        using var unknown = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf("XVP-9999"), new ModeratorDescriptionRequest { Description = First, Statement = "Bandymas." });
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
    }

    [Fact]
    public async Task Only_a_moderator_reads_the_state_of_a_bill_for_the_controls_on_its_page_and_an_action_creates_the_record()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var route = AppealRoutes.ModerationBillOf(AppealSteps.SecondBill);

        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var byMember = await member.Client.GetAsync(route))
        {
            await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using (var byMember = await member.PostAsync(AppealRoutes.BillRecordOf(AppealSteps.SecondBill)))
        {
            await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        var empty = await moderator.ModerationBillAsync(AppealSteps.SecondBill);
        empty.Id.ShouldBeNull();
        empty.Status.ShouldBe("open");
        empty.Threshold.ShouldBe(100);
        empty.ThresholdSetBy.ShouldBe("default");
        empty.Reasons.ShouldBeEmpty();

        var id = await moderator.RecordIdAsync(AppealSteps.SecondBill);
        (await moderator.RecordIdAsync(AppealSteps.SecondBill)).ShouldBe(id, "found the second time, not created again");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target = @t", ("t", "record:" + id))).ShouldBe(0, "creating the record alone is not an action");

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HoldRecord, id), new StatementRequest { Statement = "Laukiame Seimo darbotvarkės." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var state = await moderator.ModerationBillAsync(AppealSteps.SecondBill);
        state.Id.ShouldBe(id);
        state.Held.ShouldBeTrue();
        state.HasOpenItems.ShouldBeFalse();
    }
    [Fact]
    public async Task A_flagged_argument_waits_and_a_moderator_publishes_it()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
        var (author, email) = await Factory.MemberAsync();
        using var _a = author;
        (await author.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var created = await (await author.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Rašykite man: autorius@pavyzdys.test")).ReadAsync<ReasonCreatedResponse>();
        created.Status.ShouldBe("pending");

        var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.Single(r => r.Id == created.Id);
        queued.Flags.ShouldBe(["email"]);
        queued.Status.ShouldBe("pending");
        queued.Side.ShouldBe("for");

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, created.Id), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await author.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldContain(r => r.Id == created.Id);
        (await NoticesAsync(email, "reason-published")).ShouldBe(1);
        (await AuditAsync("moderation.reason.publish", created.Id)).ShouldBe(1);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.ShouldNotContain(r => r.Id == created.Id, "seen now");
    }

    [Fact]
    public async Task A_reported_argument_is_queued_and_hiding_it_removes_it_and_its_marks()
    {
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
        var reasonId = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        (await initiator.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "insult", Text = "Įžeidžia." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.Single(r => r.Id == reasonId);
        queued.Reports.Single().Ground.ShouldBe("insult");
        queued.Reports.Single().Text.ShouldBe("Įžeidžia.");

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, reasonId), new StatementRequest { Statement = "Įžeidus tonas." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldBeEmpty();
        (await initiator.MineAsync(AppealSteps.ThirdBill)).Endorsed.ShouldBeEmpty();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.report WHERE target_id = @t AND status = 'open'", ("t", reasonId))).ShouldBe(0);
        (await NoticesAsync(email, "reason-hidden")).ShouldBe(1);
        (await AuditAsync("moderation.reason.hide", reasonId)).ShouldBe(1);
    }

    [Fact]
    public async Task Merging_an_argument_moves_its_marks_to_the_similar_one_and_tells_its_author()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
        var original = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single();
        var (author, email) = await Factory.MemberAsync();
        using var _a = author;
        (await author.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var duplicate = await (await author.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Akcijų pardavimo įskaičiavimas paliestų vienkartinius šeimos verslo pardavimus.")).ReadAsync<ReasonCreatedResponse>();
        (await author.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(duplicate.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await author.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(original.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await initiator.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(duplicate.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.Single(r => r.Id == duplicate.Id);
        queued.Seen.ShouldBeFalse();
        queued.MergeCandidates.Single().Id.ShouldBe(original.Id);

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.MergeReason, duplicate.Id), new MergeReasonRequest { IntoReasonId = original.Id, Statement = "Tas pats argumentas." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reasons = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons;
        reasons.Single().Id.ShouldBe(original.Id);
        reasons.Single().Endorsements.ShouldBe(2, "the author's mark and the initiator's moved mark; the author's two marks became one");
        (await NoticesAsync(email, "reason-merged")).ShouldBe(1);
        (await Database.ScalarAsync<string>(
            "SELECT payload->>'excerpt' FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to", ("to", email)))
            .ShouldStartWith("Akcijų pardavimo įskaičiavimas paliestų");
        (await AuditAsync("moderation.reason.merge", duplicate.Id)).ShouldBe(1);

        var (forSide, _) = await Factory.MemberAsync();
        using var _f = forSide;
        (await forSide.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var other = await (await forSide.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Gynyba finansuojama nekeliant PVM.")).ReadAsync<ReasonCreatedResponse>();
        using var acrossSides = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.MergeReason, other.Id), new MergeReasonRequest { IntoReasonId = original.Id, Statement = "Ne." });
        await acrossSides.ShouldBeProblemAsync(HttpStatusCode.Conflict, "merge.target-invalid");
    }

    [Fact]
    public async Task Keeping_a_bill_whose_description_was_reported_closes_the_reports_without_an_email()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "record", TargetId = record.Id, Ground = "other" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.Single(a => a.Id == record.Id).Reports.Count.ShouldBe(1);
        (await moderator.ModerationBillAsync(AppealSteps.ThirdBill)).HasOpenItems.ShouldBeTrue("a report waits, so \"Palikti\" means something");

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.KeepRecord, record.Id), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.ShouldNotContain(a => a.Id == record.Id);
        (await AuditAsync("moderation.record.keep", record.Id)).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' NOT LIKE 'reason-%'", ("to", email)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task A_description_is_reported_only_while_the_bill_has_one()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var id = (await member.RecordAsync(AppealSteps.SecondBill)).Id!.Value;
        using var report = await member.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "record", TargetId = id, Ground = "other" });
        await report.ShouldBeProblemAsync(HttpStatusCode.NotFound, "report.target-not-found");
    }

    /// <summary>
    /// Design 8.13 (decided by the owner on 2026-11-14): the panel on the bill's page shows the open reports about the
    /// description as the queue does (ground, the reporter's words, when), from the moderators-only answer; nobody else
    /// receives any of it, a moderator's code session included.
    /// </summary>
    [Fact]
    public async Task Reports_about_the_description_reach_the_panels_answer_and_nobody_but_a_moderator_receives_them()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var staff = await ModeratorAsync();
        var moderator = staff.Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill);
        var (reporter, _) = await Factory.MemberAsync("Pranešėja Bandomoji");
        using var _r = reporter;
        const string Said = "Tarifas taikomas nuo 60, ne nuo 40 vidutinių darbo užmokesčių (pranešimo bandymas).";
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "record", TargetId = record.Id, Ground = "inaccurate", Text = Said }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The moderator's answer: exactly what the queue shows for that report.
        var state = await moderator.ModerationBillAsync(AppealSteps.ThirdBill);
        var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.Single(a => a.Id == record.Id).Reports;
        state.DescriptionReports.ShouldBe(queued);
        var report = state.DescriptionReports.ShouldHaveSingleItem();
        (report.Ground, report.Text).ShouldBe(("inaccurate", Said));
        report.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        var moderatorsPage = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
        moderatorsPage.ShouldContain("id=\"panel-description-reports\"");
        moderatorsPage.ShouldContain(Said);

        // Nobody else: not a visitor, a member, the reporter, nor the moderator in a code session.
        using var stranger = NewBrowser();
        using var codeSession = NewBrowser();
        await codeSession.PostAsync(Lt2.Api.Contracts.ApiRoutes.Auth.Code, new Lt2.Api.Contracts.Auth.RequestCodeRequest { Email = staff.Email });
        await codeSession.SignInWithCodeAsync(staff.Email);
        var route = AppealRoutes.ModerationBillOf(AppealSteps.ThirdBill);
        using (var byVisitor = await stranger.Client.GetAsync(route))
        {
            byVisitor.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await byVisitor.Content.ReadAsStringAsync()).ShouldNotContain(Said);
        }

        foreach (var browser in new[] { member, reporter, codeSession })
        {
            using var refused = await browser.Client.GetAsync(route);
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        foreach (var (who, browser) in new[] { ("visitor", stranger), ("member", member), ("reporter", reporter), ("moderator's code session", codeSession) })
        {
            var page = WebUtility.HtmlDecode(await browser.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
            page.ShouldNotContain("panel-description-reports", customMessage: who);
            page.ShouldNotContain("pranešimo bandymas", customMessage: who);
            page.ShouldNotContain("id=\"moderation-panel\"", customMessage: who);
        }

        // Correcting the description from the panel closes its reports, as from the queue.
        (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.ThirdBill),
            new ModeratorDescriptionRequest { Description = AppealSteps.Description + " Taikoma nuo 2027 metų.", Statement = "Pataisyta pagal pranešimą." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.ModerationBillAsync(AppealSteps.ThirdBill)).DescriptionReports.ShouldBeEmpty();
    }

    /// <summary>
    /// Design 8.13 (decided by the owner on 2026-11-14): a moderator records the official text's address with a description
    /// they write or correct. Empty or https on lrs.lt or under it, no user info, at most 500 characters; the field's value
    /// becomes the record's address and an empty one clears it. Shown as "Oficialus tekstas, iš kurio parengta".
    /// </summary>
    [Fact]
    public async Task A_moderator_records_the_official_texts_address_with_the_description_and_an_empty_field_clears_it()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var staff = await ModeratorAsync();
        var moderator = staff.Browser;
        var route = AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill);
        const string Text = "Projektas leidžia savivaldybėms nustatyti vietinių rinkliavų dydį pagal sąnaudas.";
        const string Address = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas-0533";
        const string Other = "https://www.lrs.lt/sip/portal.show?p_r=35403&p_k=1&bandymas=0533";

        // Refused at the field, on the server too; nothing is saved.
        foreach (var refused in new[]
        {
            "https://evil-lrs.lt/bandymas", "https://lrs.lt.evil.example/bandymas", "https://lrs.lt@evil.example/bandymas",
            "http://e-seimas.lrs.lt/bandymas", "https://e-seimas.lrs.lt/" + new string('a', 480),
        })
        {
            using var response = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Text, Statement = "Bandymas.", DescriptionUrl = refused });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused);
            (await response.ProblemAsync()).GetProperty("errors").TryGetProperty("descriptionUrl", out _).ShouldBeTrue(refused);
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe(0);

        // Saved with the description: shown at once beside it, and the form starts from it next time.
        (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Text, Statement = "Parašyta pagal aiškinamąjį raštą.", DescriptionUrl = " " + Address + " " }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var stranger = NewBrowser();
        var described = await stranger.RecordAsync(AppealSteps.SecondBill);
        (described.Description, described.DescriptionSource, described.DescriptionUrl).ShouldBe((Text, "moderator", Address));
        (await moderator.ModerationBillAsync(AppealSteps.SecondBill)).DescriptionUrl.ShouldBe(Address);
        var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
        page.ShouldContain($"<a href=\"{Address}\" rel=\"external\" id=\"description-from\">Oficialus tekstas, iš kurio parengta</a>");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'moderation.record.describe' AND target = @t AND details->'extra'->>'descriptionUrl' = @u",
            ("t", "record:" + described.Id), ("u", Address))).ShouldBe(1, "the address is not personal data and is in the audit details");

        // The same text and address change nothing; another address alone is a change, which leaves the text's history.
        using (var unchanged = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Text, Statement = "Bandymas.", DescriptionUrl = Address }))
        {
            await unchanged.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "description.unchanged");
        }

        (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Text, Statement = "Tikslesnis adresas.", DescriptionUrl = Other }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await stranger.RecordAsync(AppealSteps.SecondBill)).DescriptionUrl.ShouldBe(Other);
        (await stranger.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.SecondBill))).ShouldHaveSingleItem().NewText.ShouldBe(Text);

        // An empty field clears it: the page no longer names an official text.
        (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Text + " Taikoma nuo 2027 metų.", Statement = "Adresas netinka.", DescriptionUrl = "" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await stranger.RecordAsync(AppealSteps.SecondBill)).DescriptionUrl.ShouldBeNull();
        WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill)).ShouldNotContain("id=\"description-from\"");

        // The module applies the same rule whatever calls it, not only the endpoint's validator.
        await using var scope = Factory.Services.CreateAsyncScope();
        var module = scope.ServiceProvider.GetRequiredService<Lt2.Appeals.IAppealsModeration>();
        var actor = new Lt2.SharedKernel.StaffActor(await Database.UserIdAsync(staff.Email), "moderator", null);
        var direct = await module.WriteDescriptionAsync(AppealSteps.SecondBill, new Lt2.Appeals.ModeratorDescription(Text, "Bandymas.", false, "https://lrs.lt@evil.example/"), actor, CancellationToken.None);
        direct.Error.ShouldNotBeNull().Code.ShouldBe("description-url.invalid");
    }

    /// <summary>Design 8.12.3 (decided by the owner on 2026-11-14): arguments are moderated on the bill's page too.</summary>
    [Fact]
    public async Task Arguments_are_moderated_on_the_bills_page_from_the_moderators_only_answer_with_the_queues_actions()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill, "against");
        var (author, email) = await Factory.MemberAsync("Argumentų Autorė");
        using var _a = author;
        var shown = await (await author.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Akcijų pardavimo įskaičiavimas paliestų šeimos verslų pardavimus.")).ReadAsync<ReasonCreatedResponse>();
        var waiting = await (await author.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Rašykite: autore@pavyzdys.test")).ReadAsync<ReasonCreatedResponse>();
        waiting.Status.ShouldBe("pending");

        // Nobody but a moderator gets the answer the controls need, nor any control on the page.
        using var stranger = NewBrowser();
        var route = AppealRoutes.ModerationBillOf(AppealSteps.ThirdBill);
        (await stranger.Client.GetAsync(route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var byMember = await member.Client.GetAsync(route))
        {
            await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        foreach (var (who, browser) in new[] { ("visitor", stranger), ("member", member), ("author", author) })
        {
            var page = WebUtility.HtmlDecode(await browser.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
            page.ShouldNotContain("id=\"moderation-panel\"", customMessage: who);
            page.ShouldNotContain("id=\"moderate-", customMessage: who);
            page.ShouldNotContain("panel-reason-", customMessage: who);
            page.ShouldNotContain("autore@pavyzdys.test", customMessage: who + ": a waiting argument is not shown to others");
        }

        // The moderator's answer carries the bill's arguments as the queue shows them; the panel lists the waiting one.
        var state = await moderator.ModerationBillAsync(AppealSteps.ThirdBill);
        var shownState = state.Reasons.Single(r => r.Id == shown.Id);
        shownState.MergeCandidates.ShouldContain(r => r.Text == AppealSteps.AgainstReason);
        state.Reasons.Single(r => r.Id == waiting.Id).Flags.ShouldBe(["email"]);
        state.Reasons.ShouldAllBe(r => r.Status == "pending" || r.Status == "published");
        var moderatorsPage = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
        moderatorsPage.ShouldContain("id=\"panel-reason-" + waiting.Id + "\"");

        // Hiding from the page is the queue's action: recorded, audited and told to the author.
        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, shown.Id), new StatementRequest { Statement = "Kartoja jau esamą argumentą įžeidžiamai." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await stranger.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldNotContain(r => r.Id == shown.Id);
        (await NoticesAsync(email, "reason-hidden")).ShouldBe(1);
        (await AuditAsync("moderation.reason.hide", shown.Id)).ShouldBe(1);
        (await Database.ScalarAsync<string>(
            "SELECT payload->>'path' FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to", ("to", email))).ShouldBe("/seimas/" + AppealSteps.ThirdBill);
        (await moderator.ModerationBillAsync(AppealSteps.ThirdBill)).Reasons.ShouldNotContain(r => r.Id == shown.Id, "restoring stays on the list of hidden items");
    }

    /// <summary>Design 6.8: a report is closed only by an action that answers it; a threshold, holding or releasing do not.</summary>
    [Fact]
    public async Task A_threshold_holding_or_releasing_leaves_a_report_on_the_description_open()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "record", TargetId = record.Id, Ground = "inaccurate", Text = "Tarifas kitas." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record.Id), new ModeratorThresholdRequest { Threshold = 150, Statement = "Kitas slenkstis." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HoldRecord, record.Id), new StatementRequest { Statement = "Sulaikoma." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReleaseRecord, record.Id), new StatementRequest { Statement = "Paleidžiama." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.ModerationBillAsync(AppealSteps.ThirdBill)).DescriptionReports.ShouldHaveSingleItem("none of these answers the report");
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.Single(a => a.Id == record.Id).Reports.Count.ShouldBe(1);

        // "Palikti" answers it.
        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.KeepRecord, record.Id), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.ModerationBillAsync(AppealSteps.ThirdBill)).DescriptionReports.ShouldBeEmpty();
    }

    /// <summary>Design 8.13: an address changed alone leaves the description's label (its source) as it is.</summary>
    [Fact]
    public async Task Changing_only_the_official_texts_address_keeps_the_descriptions_label()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = (await ModeratorAsync()).Browser;
        var record = await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill, side: null);

        // The summary job's text nobody has read yet ("Sukurta DI · žmonių neperžiūrėta").
        await Database.ExecuteAsync("UPDATE appeals.bill_record SET description_source = 'ai' WHERE id = @a", ("a", record.Id));
        (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.ThirdBill),
                new ModeratorDescriptionRequest { Description = AppealSteps.Description, Statement = "Tik adresas.", DescriptionUrl = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas-0618" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var stranger = NewBrowser();
        (await stranger.RecordAsync(AppealSteps.ThirdBill)).DescriptionSource.ShouldBe("ai", "an address alone is not reading or correcting the text");

        // A changed text is a moderator's correction.
        (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.ThirdBill),
                new ModeratorDescriptionRequest { Description = AppealSteps.Description + " Nuo 2027 metų.", Statement = "Pataisyta.", DescriptionUrl = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas-0618" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await stranger.RecordAsync(AppealSteps.ThirdBill)).DescriptionSource.ShouldBe("ai-draft");
    }

    /// <summary>Design 6.8: a moderator's statement is checked by the module that stores it, whatever calls it.</summary>
    [Fact]
    public async Task The_module_checks_a_moderators_statement_whatever_calls_it()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var staff = await ModeratorAsync();
        var record = await Database.PrepareBillAsync(member, staff.Browser, AppealSteps.ThirdBill, side: null);
        await using var scope = Factory.Services.CreateAsyncScope();
        var module = scope.ServiceProvider.GetRequiredService<Lt2.Appeals.IAppealsModeration>();
        var actor = new Lt2.SharedKernel.StaffActor(await Database.UserIdAsync(staff.Email), "moderator", null);

        (await module.HoldAsync(record.Id, "", actor, CancellationToken.None)).Error.ShouldNotBeNull().Code.ShouldBe("statement.required");
        (await module.ReleaseAsync(record.Id, " ", actor, CancellationToken.None)).Error.ShouldNotBeNull().Code.ShouldBe("statement.required");
        (await module.SetPlannedAsync(record.Id, new DateOnly(2027, 1, 15), null, "", actor, CancellationToken.None)).Error.ShouldNotBeNull().Code.ShouldBe("statement.required");
        (await module.HideRecordAsync(record.Id, new string('a', 1001), actor, CancellationToken.None)).Error.ShouldNotBeNull().Code.ShouldBe("staff-statement.too-long");
        (await module.KeepRecordAsync(record.Id, new string('a', 1001), actor, CancellationToken.None)).Error.ShouldNotBeNull().Code.ShouldBe("staff-statement.too-long");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.moderation_action WHERE target_id = @a AND action IN ('hold', 'release', 'planned', 'hide', 'keep')", ("a", record.Id)))
            .ShouldBe(0, "nothing was recorded");
    }
}