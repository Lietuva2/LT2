using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3 over HTTP: the queue and the moderation actions. Each needs an active moderator role (a staff-passkey
/// session), is audited, and tells the author why by email through the outbox.
/// </summary>
public sealed class ModerationApiTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static StaffMember? _moderator;

    private async Task<StaffMember> ModeratorAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return _moderator ??= await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        }
        finally
        {
            Gate.Release();
        }
    }

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

        using var publish = await codeSession.PublishAsync(Guid.NewGuid());
        await publish.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");

        (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_new_appeal_is_in_the_queue_and_publishing_it_opens_it_audits_and_tells_the_initiator()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, email) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        using var opened = await initiator.OpenAppealAsync(
            AppealSteps.SecondBill, description: AppealSteps.Description + " Skambinkite +370 612 34567, paaiškinsiu.");
        var appeal = await opened.ReadAsync<AppealResponse>();
        var moderator = (await ModeratorAsync()).Browser;

        var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.Single(a => a.Id == appeal.Id);
        queued.Status.ShouldBe("pending");
        queued.InitiatorName.ShouldBe("Rūta Mockutė");
        queued.Description.ShouldBe(AppealSteps.Description + " Skambinkite +370 612 34567, paaiškinsiu.");
        queued.Flags.ShouldBe(["phone"], "the moderator reviews the description only; there is no first argument");

        using (var published = await moderator.PublishAsync(appeal.Id, "Aprašymas neutralus."))
        {
            published.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var stranger = NewBrowser();
        var page = await stranger.AppealAsync(AppealSteps.SecondBill);
        page.Status.ShouldBe("open");
        page.PublishedAt.ShouldNotBeNull();
        page.Reasons.ShouldBeEmpty("opened without arguments; the initiator writes them like anyone else");
        (await AuditAsync("moderation.appeal.publish", appeal.Id)).ShouldBe(1);
        (await NoticesAsync(email, "appeal-published")).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.moderation_action WHERE target_id = @t AND action = 'publish'", ("t", appeal.Id))).ShouldBe(1);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.ShouldNotContain(a => a.Id == appeal.Id);

        using var twice = await moderator.ReturnAppealAsync(appeal.Id, "Per vėlu.");
        await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
    }

    [Fact]
    public async Task A_returned_appeal_carries_the_note_to_its_initiator_who_corrects_it_and_sends_it_again()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await (await initiator.OpenAppealAsync(AppealSteps.SecondBill, description: "Labai blogas projektas.")).ReadAsync<AppealResponse>();
        var moderator = (await ModeratorAsync()).Browser;

        using (var noStatement = await moderator.ReturnAppealAsync(appeal.Id, " "))
        {
            await noStatement.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        (await moderator.ReturnAppealAsync(appeal.Id, "Aprašymas turi būti neutralus.")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var returned = await initiator.AppealAsync(AppealSteps.SecondBill);
        returned.Status.ShouldBe("returned");
        returned.ReturnNote.ShouldBe("Aprašymas turi būti neutralus.");
        (await NoticesAsync(email, "appeal-returned")).ShouldBe(1);
        (await Database.ScalarAsync<string>(
            "SELECT payload->>'statement' FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to", ("to", email))).ShouldBe("Aprašymas turi būti neutralus.");

        using (var edited = await initiator.SendAsync(HttpMethod.Put, AppealRoutes.AppealOf(AppealSteps.SecondBill), new EditAppealRequest
        {
            Description = AppealSteps.Description,
        }))
        {
            edited.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await initiator.AppealAsync(AppealSteps.SecondBill)).Status.ShouldBe("pending");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.revision WHERE entity_id = @a AND field = 'description'", ("a", appeal.Id))).ShouldBe(1);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.ShouldContain(a => a.Id == appeal.Id);
    }

    [Fact]
    public async Task A_hidden_appeal_disappears_and_leaves_the_bill_free_for_a_new_one()
    {
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill);

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideAppeal, appeal.Id), new StatementRequest { Statement = "Neteisėtas turinys." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        using (var gone = await stranger.Client.GetAsync(AppealRoutes.AppealOf(AppealSteps.ThirdBill)))
        {
            await gone.ShouldBeProblemAsync(HttpStatusCode.NotFound, "appeal.not-found");
        }

        (await AuditAsync("moderation.appeal.hide", appeal.Id)).ShouldBe(1);
        (await NoticesAsync(email, "appeal-hidden")).ShouldBe(1);
        var (next, _) = await Factory.MemberAsync();
        using var _n = next;
        (await next.OpenAppealAsync(AppealSteps.ThirdBill)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_threshold_set_by_a_moderator_is_public_with_its_reason_and_final_for_the_initiator()
    {
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill, threshold: 5000);

        using (var below = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealThreshold, appeal.Id), new ModeratorThresholdRequest { Threshold = 1, Statement = "Per maža." }))
        {
            await below.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "threshold.below-minimum");
        }

        (await moderator.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealThreshold, appeal.Id), new ModeratorThresholdRequest
        {
            Threshold = 300,
            Statement = "Riba buvo nepasiekiama ir užkirto kelią kitai pusei.",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        (await stranger.AppealAsync(AppealSteps.ThirdBill)).Threshold.ShouldBe(new ThresholdResponse(300, "moderator", "Riba buvo nepasiekiama ir užkirto kelią kitai pusei.", 1, false, false));
        (await NoticesAsync(email, "threshold-changed")).ShouldBe(1);
        (await AuditAsync("moderation.appeal.threshold", appeal.Id)).ShouldBe(1);

        using var initiatorChange = await initiator.SendAsync(HttpMethod.Put, AppealRoutes.ThresholdOf(AppealSteps.ThirdBill), new ThresholdRequest { Threshold = 100 });
        await initiatorChange.ShouldBeProblemAsync(HttpStatusCode.Conflict, "threshold.set-by-moderator");
        (await initiator.MineAsync(AppealSteps.ThirdBill)).MayChangeThreshold.ShouldBeFalse();
    }

    [Fact]
    public async Task A_moderator_corrects_the_description_directly_with_a_statement_to_the_initiator_and_it_enters_the_history()
    {
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill);
        var route = AppealRoutes.With(AppealRoutes.AppealDescription, appeal.Id);
        const string Corrected = "Projektas keičia vietinių rinkliavų tvarką: savivaldybės galės nustatyti rinkliavą už stovėjimą.";

        using (var byMember = await initiator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Corrected, Statement = "Bandymas." }))
        {
            await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using (var noStatement = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = Corrected, Statement = " " }))
        {
            await noStatement.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var unchanged = await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest { Description = appeal.Description, Statement = "Be pakeitimų." }))
        {
            await unchanged.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
        }

        (await moderator.SendAsync(HttpMethod.Put, route, new ModeratorDescriptionRequest
        {
            Description = "  " + Corrected + " ",
            Statement = "Aprašymas buvo šališkas: rinkliava vadinta mokesčiu vairuotojams.",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        (await stranger.AppealAsync(AppealSteps.ThirdBill)).Description.ShouldBe(Corrected, "shown at once, trimmed, without waiting for anyone");
        var history = await stranger.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.ThirdBill));
        history.ShouldContain(h => h.NewText == Corrected && h.OldText == appeal.Description);
        (await NoticesAsync(email, "description-corrected")).ShouldBe(1);
        (await AuditAsync("moderation.appeal.describe", appeal.Id)).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.moderation_action WHERE target_id = @t AND action = 'describe'", ("t", appeal.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Only_a_moderator_reads_the_state_of_an_appeal_for_the_controls_on_its_page()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill, threshold: 400);
        var route = AppealRoutes.With(AppealRoutes.ModerationAppeal, appeal.Id);

        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var byMember = await initiator.Client.GetAsync(route))
        {
            await byMember.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        var state = await moderator.GetJsonAsync<ModerationAppealResponse>(route);
        state.Id.ShouldBe(appeal.Id);
        state.Status.ShouldBe("open");
        state.Description.ShouldBe(appeal.Description);
        state.Threshold.ShouldBe(400);
        state.Held.ShouldBeFalse();
        state.HasOpenItems.ShouldBeFalse();

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HoldAppeal, appeal.Id), new StatementRequest { Statement = "Laukiame Seimo darbotvarkės." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await initiator.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "appeal", TargetId = appeal.Id, Ground = "other", Text = "Bandymas." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        state = await moderator.GetJsonAsync<ModerationAppealResponse>(route);
        state.Held.ShouldBeTrue();
        state.HasOpenItems.ShouldBeTrue("a report waits, so \"Palikti\" means something");

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideAppeal, appeal.Id), new StatementRequest { Statement = "Bandymas." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var hidden = await moderator.Client.GetAsync(route);
        await hidden.ShouldBeProblemAsync(HttpStatusCode.NotFound, "appeal.not-found");
    }

    [Fact]
    public async Task A_flagged_argument_waits_and_a_moderator_publishes_it()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
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
        (await author.AppealAsync(AppealSteps.ThirdBill)).Reasons.ShouldContain(r => r.Id == created.Id);
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
        await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
        var reasonId = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        (await initiator.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "insult", Text = "Įžeidžia." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.Single(r => r.Id == reasonId);
        queued.Reports.Single().Ground.ShouldBe("insult");
        queued.Reports.Single().Text.ShouldBe("Įžeidžia.");

        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, reasonId), new StatementRequest { Statement = "Įžeidus tonas." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.ShouldBeEmpty();
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
        await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
        var original = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single();
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

        var reasons = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons;
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
    public async Task Keeping_a_reported_appeal_closes_its_reports_without_an_email()
    {
        var (initiator, email) = await Factory.MemberAsync();
        using var _i = initiator;
        var moderator = (await ModeratorAsync()).Browser;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill);
        var (reporter, _) = await Factory.MemberAsync();
        using var _r = reporter;
        (await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "appeal", TargetId = appeal.Id, Ground = "other" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.Single(a => a.Id == appeal.Id).Reports.Count.ShouldBe(1);

        (await moderator.PublishAsync(appeal.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.ShouldNotContain(a => a.Id == appeal.Id);
        (await NoticesAsync(email, "appeal-published")).ShouldBe(1, "only the first publication");
    }
}

internal static class ModerationSteps
{
    public static Task<HttpResponseMessage> ReturnAppealAsync(this TestBrowser moderator, Guid appealId, string statement) =>
        moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReturnAppeal, appealId), new StatementRequest { Statement = statement });
}
