using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>Design 8.8 over HTTP: appeals, positions, arguments, endorsements, reports and the consent before a first position.</summary>
public sealed class AppealApiTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly SemaphoreSlim ModeratorGate = new(1, 1);
    private static StaffMember? _moderator;

    private async Task<TestBrowser> ModeratorAsync()
    {
        await ModeratorGate.WaitAsync();
        try
        {
            _moderator ??= await StaffMembers.ActiveAsync(Factory, Database, "moderator");
            return _moderator.Browser;
        }
        finally
        {
            ModeratorGate.Release();
        }
    }

    [Fact]
    public async Task The_rules_offer_the_default_threshold_and_state_the_minimum_to_anyone()
    {
        using var stranger = NewBrowser();
        var rules = await stranger.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules);
        rules.ShouldBe(new AppealRulesResponse(100, 50, 500, 2, 3, 1500));
    }

    [Fact]
    public async Task Opening_an_appeal_needs_a_signed_in_member()
    {
        using var stranger = NewBrowser();
        using var response = await stranger.OpenAppealAsync(AppealSteps.SecondBill);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Opening_an_appeal_needs_the_consent_to_store_the_initiators_side()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (browser, _) = await Factory.MemberAsync(consent: false);
        using var _b = browser;
        using var response = await browser.OpenAppealAsync(AppealSteps.SecondBill);
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
    }

    [Fact]
    public async Task An_opened_appeal_waits_for_a_moderator_and_only_its_initiator_sees_it()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, _) = await Factory.MemberAsync("Rūta Mockutė");
        var (other, _) = await Factory.MemberAsync("Giedrė L.");
        using var _i = initiator;
        using var _o = other;
        using var stranger = NewBrowser();

        using var opened = await initiator.OpenAppealAsync(AppealSteps.SecondBill, "against", threshold: 1000);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        opened.Headers.Location!.ToString().ShouldBe("/api/v1/bills/XVP-0533/appeal");
        var appeal = await opened.ReadAsync<AppealResponse>();
        appeal.Status.ShouldBe("pending");
        appeal.InitiatorName.ShouldBe("Rūta Mockutė");
        appeal.Bill.Number.ShouldBe("XVP-0533");
        appeal.Threshold.ShouldBe(new ThresholdResponse(1000, "initiator", null, 1, false, false));
        appeal.Counts.ShouldBe(new SideCountsResponse(0, 0, 1, 0), "the initiator's own side");

        (await initiator.AppealAsync(AppealSteps.SecondBill)).Status.ShouldBe("pending");
        var mine = await initiator.MineAsync(AppealSteps.SecondBill);
        mine.Side.ShouldBe("against");
        mine.IsInitiator.ShouldBeTrue();
        mine.MayChangeThreshold.ShouldBeTrue();
        mine.MyReasons.Single().Status.ShouldBe("pending");
        mine.ReasonsLeft.ShouldBe(1);

        foreach (var browser in new[] { other, stranger })
        {
            using var hidden = await browser.Client.GetAsync(AppealRoutes.AppealOf(AppealSteps.SecondBill));
            await hidden.ShouldBeProblemAsync(HttpStatusCode.NotFound, "appeal.not-found");
        }

        (await stranger.GetJsonAsync<AppealListResponse>(AppealRoutes.Appeals)).Items.ShouldNotContain(i => i.Id == appeal.Id);
    }

    [Fact]
    public async Task A_bill_has_one_appeal_so_a_second_one_is_refused()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (first, _) = await Factory.MemberAsync();
        var (second, _) = await Factory.MemberAsync();
        using var _f = first;
        using var _s = second;
        (await first.OpenAppealAsync(AppealSteps.SecondBill)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var again = await second.OpenAppealAsync(AppealSteps.SecondBill, "for");
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "appeal.exists");
    }

    [Fact]
    public async Task An_appeal_is_opened_only_for_a_bill_that_exists()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var response = await member.OpenAppealAsync("XVP-9999");
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
    }

    [Fact]
    public async Task A_threshold_below_the_minimum_is_refused()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var response = await member.OpenAppealAsync(AppealSteps.SecondBill, threshold: 49);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "threshold.below-minimum");
    }

    [Theory]
    [InlineData("abstain", 100, "Argumentas.", "Aprašymas.")]
    [InlineData("for", 0, "Argumentas.", "Aprašymas.")]
    [InlineData("for", 100, " ", "Aprašymas.")]
    [InlineData("for", 100, "Argumentas.", "")]
    public async Task An_appeal_needs_a_side_a_positive_threshold_a_first_argument_and_a_description(string side, int threshold, string reason, string description)
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var response = await member.OpenAppealAsync(AppealSteps.SecondBill, side, threshold, reason, description);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Published_appeals_are_listed_with_both_sides_and_their_older_account_counts()
    {
        var (initiator, initiatorEmail) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        await Database.AgeAccountAsync(initiatorEmail, 45);
        var (supporter, _) = await Factory.MemberAsync();
        using var _s = supporter;
        (await supporter.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        var list = await stranger.GetJsonAsync<AppealListResponse>(AppealRoutes.Appeals);
        var item = list.Items.Single(i => i.Id == appeal.Id);
        item.Counts.ShouldBe(new SideCountsResponse(For: 1, ForOlderThan30Days: 0, Against: 1, AgainstOlderThan30Days: 1));
        item.Bill.Title.ShouldNotBeNullOrEmpty();
        (await stranger.GetJsonAsync<AppealListResponse>(AppealRoutes.AppealsList("newest", null))).Items.ShouldContain(i => i.Id == appeal.Id);

        var page = await stranger.AppealAsync(AppealSteps.ThirdBill);
        page.Status.ShouldBe("open");
        page.Counts.ShouldBe(item.Counts);
        page.Threshold.Participants.ShouldBe(2);
        page.Reasons.Single().Text.ShouldBe(AppealSteps.AgainstReason);
    }

    [Fact]
    public async Task The_public_appeal_is_cached_by_etag_and_revalidated_every_time()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill);
        using var stranger = NewBrowser();

        using var first = await stranger.Client.GetAsync(AppealRoutes.AppealOf(AppealSteps.ThirdBill));
        first.Headers.CacheControl!.ToString().ShouldBe("public, no-cache");
        using var request = new HttpRequestMessage(HttpMethod.Get, AppealRoutes.AppealOf(AppealSteps.ThirdBill));
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var second = await stranger.Client.SendAsync(request);
        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Taking_changing_and_withdrawing_a_side_moves_the_counts()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;

        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.AppealAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(1, 0, 1, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("for");

        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.AppealAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(0, 0, 2, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("against");

        using (var cleared = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(AppealSteps.ThirdBill)))
        {
            cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await member.AppealAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(0, 0, 1, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBeNull();
    }

    [Fact]
    public async Task A_side_is_for_or_against_and_only_on_a_published_appeal()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (initiator, _) = await Factory.MemberAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _i = initiator;
        using var _m = member;
        (await initiator.OpenAppealAsync(AppealSteps.SecondBill)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using (var pending = await member.TakeSideAsync(AppealSteps.SecondBill, "for"))
        {
            await pending.ShouldBeProblemAsync(HttpStatusCode.NotFound, "appeal.not-found");
        }

        using (var ownPending = await initiator.TakeSideAsync(AppealSteps.SecondBill, "for"))
        {
            await ownPending.ShouldBeProblemAsync(HttpStatusCode.Conflict, "appeal.not-open");
        }

        using var abstain = await member.TakeSideAsync(AppealSteps.SecondBill, "abstain");
        await abstain.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Without_the_consent_no_side_is_stored()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync(consent: false);
        using var _m = member;

        using var refused = await member.TakeSideAsync(AppealSteps.ThirdBill, "for");
        await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE user_id = @u", ("u", await Database.UserIdAsync(email)))).ShouldBe(0);
        (await member.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).HasPositionConsent.ShouldBeFalse();
    }

    [Fact]
    public async Task Consent_is_given_to_the_text_in_force_only()
    {
        var (member, _) = await Factory.MemberAsync(consent: false);
        using var _m = member;
        using var old = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = "2020-01-01" });
        await old.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.outdated");

        using var given = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion });
        given.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).HasPositionConsent.ShouldBeTrue();
    }

    [Fact]
    public async Task Withdrawing_the_consent_deletes_every_position_and_mark_and_the_counts_fall()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reasonId = appeal.Reasons.Count > 0 ? appeal.Reasons[0].Id : (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.AppealAsync(AppealSteps.ThirdBill)).Counts.Against.ShouldBe(2);

        using (var withdrawn = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent))
        {
            withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var userId = await Database.UserIdAsync(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.endorsement WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        var after = await member.AppealAsync(AppealSteps.ThirdBill);
        after.Counts.Against.ShouldBe(1);
        after.Reasons.Single().Endorsements.ShouldBe(0);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent WHERE user_id = @u AND kind = 'positions' AND withdrawn_at IS NOT NULL", ("u", userId))).ShouldBe(1);

        using var again = await member.TakeSideAsync(AppealSteps.ThirdBill, "for");
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
    }

    [Fact]
    public async Task The_count_of_accounts_older_than_30_days_counts_only_those()
    {
        var (initiator, initiatorEmail) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "for");
        await Database.AgeAccountAsync(initiatorEmail, 31);
        var (young, youngEmail) = await Factory.MemberAsync();
        using var _y = young;
        await Database.AgeAccountAsync(youngEmail, 29);
        (await young.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await young.AppealAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(2, 1, 0, 0));
    }

    [Fact]
    public async Task An_argument_is_written_for_either_side_without_a_side_or_consent_at_most_twice_and_flagged_ones_wait_for_a_moderator()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync("Mindaugas R.", consent: false);
        using var _m = member;
        var before = (await member.AppealAsync(AppealSteps.ThirdBill)).Counts;

        // No side taken and no positions consent given: writing stores no position, so it needs neither.
        using (var first = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Gynyba finansuojama nekeliant PVM."))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
            (await first.ReadAsync<ReasonCreatedResponse>()).Status.ShouldBe("published");
        }

        using (var flagged = await member.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Daugiau: https://pavyzdys.test/straipsnis"))
        {
            flagged.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await flagged.ReadAsync<ReasonCreatedResponse>()).Status.ShouldBe("pending");
        }

        using (var third = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Trečias argumentas."))
        {
            await third.ShouldBeProblemAsync(HttpStatusCode.Conflict, "reason.limit");
        }

        using (var tooLong = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", new string('a', 501)))
        {
            await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        var page = await member.AppealAsync(AppealSteps.ThirdBill);
        page.Counts.ShouldBe(before, "writing an argument is not taking a side");
        page.Reasons.Select(r => r.Text).ShouldBe(["Gynyba finansuojama nekeliant PVM.", AppealSteps.AgainstReason], ignoreOrder: true);
        page.Reasons.Single(r => r.Side == "for").AuthorName.ShouldBe("Mindaugas R.");
        var mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Side.ShouldBeNull();
        mine.MyReasons.Select(r => (r.Side, r.Status)).ShouldBe([("for", "published"), ("against", "pending")]);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position p JOIN appeals.appeal a ON a.id = p.appeal_id WHERE a.bill_number = 'XVP-0618' AND a.status = 'open'"))
            .ShouldBe(1, "only the initiator's side is stored");
    }

    [Fact]
    public async Task Man_svarbu_still_needs_ones_own_side_and_changing_side_keeps_ones_arguments()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync("Mindaugas R.");
        using var _m = member;
        (await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Gynyba finansuojama nekeliant PVM.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var forReason = (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single(r => r.Side == "for").Id;
        var againstReason = (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single(r => r.Side == "against").Id;

        using (var noSide = await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(forReason)))
        {
            await noSide.ShouldBeProblemAsync(HttpStatusCode.Conflict, "position.required");
        }

        // Asks to vote against, though the argument they wrote is for: allowed; the mark stays on their own side.
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var otherSide = await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(forReason)))
        {
            await otherSide.ShouldBeProblemAsync(HttpStatusCode.Conflict, "endorsement.other-side");
        }

        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(againstReason))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Endorsed.ShouldBeEmpty("changing side drops the marks");
        mine.MyReasons.Select(r => r.Side).ShouldBe(["for"], "and never the person's arguments");
    }

    [Fact]
    public async Task A_similar_existing_argument_is_offered_before_a_new_one()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;

        var similar = await member.PostAsync(
            AppealRoutes.SimilarReasonsOf(AppealSteps.ThirdBill),
            new ReasonRequest { Side = "against", Text = "Akcijų pardavimo įskaičiavimas paliestų vienkartinius įvykius, pavyzdžiui šeimos verslo pardavimą" });
        similar.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await similar.ReadAsync<List<ReasonResponse>>()).Single().Text.ShouldBe(AppealSteps.AgainstReason);

        var unrelated = await member.PostAsync(AppealRoutes.SimilarReasonsOf(AppealSteps.ThirdBill), new ReasonRequest { Side = "against", Text = "Visai kita mintis apie biudžetą." });
        (await unrelated.ReadAsync<List<ReasonResponse>>()).ShouldBeEmpty();
        var otherSide = await member.PostAsync(AppealRoutes.SimilarReasonsOf(AppealSteps.ThirdBill), new ReasonRequest { Side = "for", Text = AppealSteps.AgainstReason });
        (await otherSide.ReadAsync<List<ReasonResponse>>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Man_svarbu_is_for_ones_own_side_at_most_three_and_goes_when_the_side_changes()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var writers = new List<TestBrowser>();
        for (var i = 0; i < 3; i++)
        {
            var (writer, _) = await Factory.MemberAsync();
            writers.Add(writer);
            (await writer.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", new[] { "Mokesčių našta krenta smulkiems verslams.", "Kapitalas išvažiuos į kaimynines šalis.", "Sistema taps sudėtingesnė ir brangesnė administruoti." }[i])).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var (supporterOfFor, _) = await Factory.MemberAsync();
        (await supporterOfFor.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (member, _) = await Factory.MemberAsync();
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reasons = (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons;
        reasons.Count.ShouldBe(4);

        using (var otherSide = await supporterOfFor.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons[0].Id)))
        {
            await otherSide.ShouldBeProblemAsync(HttpStatusCode.Conflict, "endorsement.other-side");
        }

        foreach (var reason in reasons.Take(3))
        {
            (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reason.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons[0].Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent, "marking again changes nothing");
        using (var fourth = await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons[3].Id)))
        {
            await fourth.ShouldBeProblemAsync(HttpStatusCode.Conflict, "endorsement.limit");
        }

        var mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Endorsed.Count.ShouldBe(3);
        mine.EndorsementsLeft.ShouldBe(0);
        (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons.Where(r => reasons.Take(3).Any(x => x.Id == r.Id)).ShouldAllBe(r => r.Endorsements == 1);

        (await member.SendAsync(HttpMethod.Delete, AppealRoutes.EndorsementOf(reasons[0].Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MineAsync(AppealSteps.ThirdBill)).Endorsed.Count.ShouldBe(2);

        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MineAsync(AppealSteps.ThirdBill)).Endorsed.ShouldBeEmpty();
        (await member.AppealAsync(AppealSteps.ThirdBill)).Reasons.Sum(r => r.Endorsements).ShouldBe(0);

        foreach (var browser in writers.Append(supporterOfFor).Append(member))
        {
            browser.Dispose();
        }
    }

    [Fact]
    public async Task A_member_reports_an_appeal_or_an_argument_once()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against");
        var reasonId = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        var (member, _) = await Factory.MemberAsync(consent: false);
        using var _m = member;

        foreach (var (kind, id) in new[] { ("appeal", appeal.Id), ("reason", reasonId), ("reason", reasonId) })
        {
            using var reported = await member.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = kind, TargetId = id, Ground = "insult", Text = "Įžeidžia." });
            reported.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reported.Content.ReadAsStringAsync());
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.report WHERE target_id = @t AND status = 'open'", ("t", reasonId))).ShouldBe(1);

        using var wrongGround = await member.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "boring" });
        await wrongGround.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        using var missing = await member.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = Guid.NewGuid(), Ground = "spam" });
        await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, "report.target-not-found");
        using var stranger = NewBrowser();
        using var anonymous = await stranger.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "spam" });
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_initiator_changes_the_threshold_and_nobody_else_can()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, await ModeratorAsync(), AppealSteps.ThirdBill, "against", threshold: 100);
        var (other, _) = await Factory.MemberAsync();
        using var _o = other;

        (await initiator.SendAsync(HttpMethod.Put, AppealRoutes.ThresholdOf(AppealSteps.ThirdBill), new ThresholdRequest { Threshold = 1000 })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await other.AppealAsync(AppealSteps.ThirdBill)).Threshold.Value.ShouldBe(1000);

        using (var below = await initiator.SendAsync(HttpMethod.Put, AppealRoutes.ThresholdOf(AppealSteps.ThirdBill), new ThresholdRequest { Threshold = 10 }))
        {
            await below.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "threshold.below-minimum");
        }

        using var notTheirs = await other.SendAsync(HttpMethod.Put, AppealRoutes.ThresholdOf(AppealSteps.ThirdBill), new ThresholdRequest { Threshold = 5000 });
        await notTheirs.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "appeal.initiator-only");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM appeals.revision WHERE field = 'threshold' AND old_value = '100' AND new_value = '1000'")).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Changing_a_side_or_writing_needs_a_member_and_the_antiforgery_header()
    {
        using var stranger = NewBrowser();
        using (var anonymous = await stranger.TakeSideAsync(AppealSteps.ThirdBill, "for"))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var pending = NewBrowser();
        await pending.RegisterAndSignInAsync(complete: false);
        using (var notMember = await pending.TakeSideAsync(AppealSteps.ThirdBill, "for"))
        {
            notMember.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var noToken = await member.SendAsync(HttpMethod.Put, AppealRoutes.PositionOf(AppealSteps.ThirdBill), new PositionRequest { Side = "for" }, antiforgery: false);
        await noToken.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "antiforgery.invalid");
    }
}
