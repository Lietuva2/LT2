using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>Design 8.8, 8.12 over HTTP: every active bill open, positions, arguments, endorsements, reports and the consent before a first position.</summary>
public sealed class AppealApiTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly SemaphoreSlim ModeratorGate = new(1, 1);

    [Fact]
    public async Task The_rules_offer_the_default_threshold_and_state_the_minimum_to_anyone()
    {
        using var stranger = NewBrowser();
        var rules = await stranger.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules);
        rules.ShouldBe(new AppealRulesResponse(100, 50, 2000, 0, 0, 1500) { MinReasonWords = 3 }, "no limit per person by default; texts up to the Texts:MaxLength default; three words at least");
    }

    [Fact]
    public async Task An_active_bill_nobody_took_part_in_answers_the_empty_open_state_not_not_found()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        using var stranger = NewBrowser();
        var record = await stranger.RecordAsync(AppealSteps.SecondBill);
        record.Id.ShouldBeNull("nobody took part and no moderator acted, so the bill has no record");
        record.Status.ShouldBe("open");
        record.Bill.Number.ShouldBe(AppealSteps.SecondBill);
        record.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));
        record.Threshold.ShouldBe(new ThresholdResponse(100, "default", null, 0, false, false));
        record.Description.ShouldBeEmpty();
        record.DescriptionSource.ShouldBeNull();
        record.Reasons.ShouldBeEmpty();

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var mine = await member.MineAsync(AppealSteps.SecondBill);
        mine.Side.ShouldBeNull();
        mine.Endorsed.ShouldBeEmpty();
        mine.MyReasons.ShouldBeEmpty();
        mine.ReasonsLeft.ShouldBeNull("no limit by default, so no counter");
        mine.HasPositionConsent.ShouldBeTrue();
        (await stranger.GetJsonAsync<IReadOnlyList<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.SecondBill))).ShouldBeEmpty();
        (await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.Appeals)).Items.ShouldContain(i => i.Bill.Number == AppealSteps.SecondBill && i.Id == null);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe(0, "reading creates nothing");
    }

    [Fact]
    public async Task A_bill_that_is_not_in_the_catalogue_is_not_found()
    {
        using var stranger = NewBrowser();
        using var missing = await stranger.Client.GetAsync(AppealRoutes.RecordOf("XVP-9999"));
        await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var side = await member.TakeSideAsync("XVP-9999", "for");
        await side.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
    }

    [Fact]
    public async Task A_member_takes_a_side_on_any_active_bill_at_once_and_two_arriving_together_land_on_one_record()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var members = new List<(TestBrowser Browser, string Email)>();
        for (var i = 0; i < 6; i++)
        {
            members.Add(await Factory.MemberAsync());
        }

        try
        {
            // Nobody proposed, opened or approved anything: the first sides create the bill's record, once.
            var answers = await Task.WhenAll(members.Select((m, i) => m.Browser.TakeSideAsync(AppealSteps.SecondBill, i % 2 == 0 ? "for" : "against")));
            foreach (var answer in answers)
            {
                answer.StatusCode.ShouldBe(HttpStatusCode.NoContent, await answer.Content.ReadAsStringAsync());
                answer.Dispose();
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe(1);
            var record = await members[0].Browser.RecordAsync(AppealSteps.SecondBill);
            record.Id.ShouldNotBeNull();
            record.Counts.ShouldBe(new SideCountsResponse(3, 0, 3, 0));
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = @b AND initiator_user_id IS NOT NULL", ("b", AppealSteps.SecondBill)))
                .ShouldBe(0, "the record has no initiator: nothing says who was first");
        }
        finally
        {
            foreach (var (browser, _) in members)
            {
                browser.Dispose();
            }
        }
    }

    [Fact]
    public async Task The_first_argument_on_a_bill_creates_its_record_too_and_a_refused_side_creates_nothing()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (refused, _) = await Factory.MemberAsync(consent: false);
        using var _r = refused;
        using (var noConsent = await refused.TakeSideAsync(AppealSteps.SecondBill, "for"))
        {
            await noConsent.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.bill_record WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe(0);

        using var wrote = await refused.WriteReasonAsync(AppealSteps.SecondBill, "against", "Rinkliavos padidės smulkiems verslams.");
        wrote.StatusCode.ShouldBe(HttpStatusCode.Created, await wrote.Content.ReadAsStringAsync());
        var record = await refused.RecordAsync(AppealSteps.SecondBill);
        record.Id.ShouldNotBeNull();
        record.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0), "writing is not taking a side");
        record.Reasons.Single().Text.ShouldBe("Rinkliavos padidės smulkiems verslams.");
    }

    [Fact]
    public async Task Nothing_offers_proposing_or_asking_for_an_appeal_any_more()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using (var propose = await member.PostAsync(AppealRoutes.RecordOf(AppealSteps.SecondBill), new { description = "Aprašymas.", threshold = 100 }))
        {
            propose.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }

        foreach (var path in new[] { "/proposals", "/requests", "/request", "/threshold", "/description-change" })
        {
            using var gone = await member.SendAsync(HttpMethod.Put, AppealRoutes.RecordOf(AppealSteps.SecondBill) + path, new { threshold = 1000 });
            gone.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }
    }
    [Fact]
    public async Task Published_records_are_listed_with_both_sides_and_their_older_account_counts()
    {
        var (initiator, initiatorEmail) = await Factory.MemberAsync();
        using var _i = initiator;
        var record = await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        await Database.AgeAccountAsync(initiatorEmail, 45);
        var (supporter, _) = await Factory.MemberAsync();
        using var _s = supporter;
        (await supporter.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = NewBrowser();
        var list = await stranger.GetJsonAsync<BillListResponse>(AppealRoutes.Appeals);
        var item = list.Items.Single(i => i.Id == record.Id);
        item.Counts.ShouldBe(new SideCountsResponse(For: 1, ForOlderThan30Days: 0, Against: 1, AgainstOlderThan30Days: 1));
        item.Bill.Title.ShouldNotBeNullOrEmpty();
        item.Arguments.ShouldBe(1);

        var page = await stranger.RecordAsync(AppealSteps.ThirdBill);
        page.Status.ShouldBe("open");
        page.Counts.ShouldBe(item.Counts);
        page.Threshold.Participants.ShouldBe(2);
        page.Reasons.Single().Text.ShouldBe(AppealSteps.AgainstReason);
    }

    [Fact]
    public async Task The_public_record_is_cached_by_etag_and_revalidated_every_time()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill);
        using var stranger = NewBrowser();

        using var first = await stranger.Client.GetAsync(AppealRoutes.RecordOf(AppealSteps.ThirdBill));
        first.Headers.CacheControl!.ToString().ShouldBe("public, no-cache");
        using var request = new HttpRequestMessage(HttpMethod.Get, AppealRoutes.RecordOf(AppealSteps.ThirdBill));
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var second = await stranger.Client.SendAsync(request);
        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Taking_changing_and_withdrawing_a_side_moves_the_counts()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;

        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.RecordAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(1, 0, 1, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("for");

        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.RecordAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(0, 0, 2, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("against");

        using (var cleared = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(AppealSteps.ThirdBill)))
        {
            cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await member.RecordAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(0, 0, 1, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBeNull();
    }

    [Fact]
    public async Task A_side_is_for_or_against()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var abstain = await member.TakeSideAsync(AppealSteps.SecondBill, "abstain");
        await abstain.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
    }
    [Fact]
    public async Task Without_the_consent_no_side_is_stored()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill);
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
        var record = await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reasonId = (await member.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.RecordAsync(AppealSteps.ThirdBill)).Counts.Against.ShouldBe(2);

        using (var withdrawn = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent))
        {
            withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var userId = await Database.UserIdAsync(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.endorsement WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        var after = await member.RecordAsync(AppealSteps.ThirdBill);
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
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "for");
        await Database.AgeAccountAsync(initiatorEmail, 31);
        var (young, youngEmail) = await Factory.MemberAsync();
        using var _y = young;
        await Database.AgeAccountAsync(youngEmail, 29);
        (await young.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await young.RecordAsync(AppealSteps.ThirdBill)).Counts.ShouldBe(new SideCountsResponse(2, 1, 0, 0));
    }

    [Fact]
    public async Task An_argument_is_written_for_either_side_without_a_side_or_consent_with_no_limit_by_default_and_flagged_ones_wait_for_a_moderator()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, _) = await Factory.MemberAsync("Mindaugas R.", consent: false);
        using var _m = member;
        var before = (await member.RecordAsync(AppealSteps.ThirdBill)).Counts;

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

        using (var third = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Trečias argumentas apie mokesčius."))
        {
            third.StatusCode.ShouldBe(HttpStatusCode.Created, "no limit per person by default");
        }

        // Over the technical ceiling the shared validator refuses it (design 8.2; the length setting: ReasonLengthTests).
        using (var tooLong = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", new string('a', MemberTextLimits.Ceiling + 1)))
        {
            await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        var page = await member.RecordAsync(AppealSteps.ThirdBill);
        page.Counts.ShouldBe(before, "writing an argument is not taking a side");
        page.Reasons.Select(r => r.Text).ShouldBe(["Gynyba finansuojama nekeliant PVM.", "Trečias argumentas apie mokesčius.", AppealSteps.AgainstReason], ignoreOrder: true);
        page.Reasons.Where(r => r.Side == "for").ShouldAllBe(r => r.AuthorName == "Mindaugas R.");
        var mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Side.ShouldBeNull();
        mine.MyReasons.Select(r => (r.Side, r.Status)).ShouldBe([("for", "published"), ("against", "pending"), ("for", "published")]);
        mine.ReasonsLeft.ShouldBeNull();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.position p JOIN appeals.bill_record a ON a.id = p.record_id WHERE a.bill_number = 'XVP-0618' AND a.status = 'open'"))
            .ShouldBe(1, "only the initiator's side is stored");
    }

    /// <summary>
    /// Owner, 2026-11-18: "Man svarbu" on either side, with or without a side of one's own, counted only while the giver asks
    /// for the argument's side. Taking, changing and withdrawing a side delete no mark; they only start or stop counting, in
    /// the figure and the order. The person's own answer lists the marks that do not count; nobody else's answer tells.
    /// </summary>
    [Fact]
    public async Task Man_svarbu_is_given_on_either_side_and_counts_only_while_the_giver_asks_for_the_arguments_side()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (writer, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _w = writer;
        (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Gynyba finansuojama nekeliant PVM.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var page = await writer.RecordAsync(AppealSteps.ThirdBill);
        var forReason = page.Reasons.Single(r => r.Side == "for").Id;
        var againstReason = page.Reasons.Single(r => r.Side == "against").Id;
        var (member, email) = await Factory.MemberAsync("Mindaugas R.");
        using var _m = member;
        int Marks(BillRecordResponse record, Guid id) => record.Reasons.Single(r => r.Id == id).Endorsements;

        // No side: both marks are kept, neither counts, and the person's own answer says so.
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(forReason))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(againstReason))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var record = await member.RecordAsync(AppealSteps.ThirdBill);
        (Marks(record, forReason), Marks(record, againstReason)).ShouldBe((0, 0));
        var mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Endorsed.ShouldBe([forReason, againstReason], ignoreOrder: true);
        mine.Uncounted.ShouldBe([forReason, againstReason], ignoreOrder: true);

        // "už": the mark on the "for" argument counts.
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        record = await member.RecordAsync(AppealSteps.ThirdBill);
        (Marks(record, forReason), Marks(record, againstReason)).ShouldBe((1, 0));
        (await member.MineAsync(AppealSteps.ThirdBill)).Uncounted.ShouldBe([againstReason]);

        // "prieš": both marks kept, the counts move.
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        record = await member.RecordAsync(AppealSteps.ThirdBill);
        (Marks(record, forReason), Marks(record, againstReason)).ShouldBe((0, 1));
        mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Endorsed.Count.ShouldBe(2, "changing side deletes no mark");
        mine.Uncounted.ShouldBe([forReason]);

        // A withdrawal naming a side the person no longer has changes nothing (a late second press, another tab).
        using (var stale = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(AppealSteps.ThirdBill), new PositionRequest { Side = "for" }))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("against");

        // Pressing "prieš" again takes the side back: the marks stay, and count nowhere.
        using (var withdrawn = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(AppealSteps.ThirdBill), new PositionRequest { Side = "against" }))
        {
            withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        record = await member.RecordAsync(AppealSteps.ThirdBill);
        (Marks(record, forReason), Marks(record, againstReason)).ShouldBe((0, 0));
        record.Counts.ShouldBe(new SideCountsResponse(0, 0, 1, 0));
        mine = await member.MineAsync(AppealSteps.ThirdBill);
        mine.Side.ShouldBeNull();
        mine.Endorsed.Count.ShouldBe(2);
        mine.Uncounted.Count.ShouldBe(2);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.endorsement WHERE user_id = @u", ("u", await Database.UserIdAsync(email)))).ShouldBe(2);

        // Withdrawing again (nothing left to withdraw) is no error; an invalid side is.
        (await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(AppealSteps.ThirdBill), new PositionRequest { Side = "against" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var invalid = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(AppealSteps.ThirdBill), new PositionRequest { Side = "abstain" }))
        {
            await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "side.invalid");
        }

        // Another person's answer never carries these marks.
        var writersOwn = await writer.MineAsync(AppealSteps.ThirdBill);
        writersOwn.Endorsed.ShouldBeEmpty();
        writersOwn.Uncounted.ShouldBeEmpty();
        writersOwn.MyReasons.Single().Endorsements.ShouldBe(0, "the author's own figure counts only counted marks too");
    }

    /// <summary>
    /// Owner, 2026-11-18: the order of the arguments follows the counted marks only, so marks from people who ask for the other
    /// side (or for nothing) cannot lift an argument; any mark, counted or not, is a reaction that ends editing (design 8.2).
    /// </summary>
    [Fact]
    public async Task Only_counted_marks_order_the_arguments_and_any_mark_ends_editing()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (writer, _) = await Factory.MemberAsync();
        using var _w = writer;
        (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Kapitalas išvažiuos į kaimynines šalis.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var reasons = (await writer.RecordAsync(AppealSteps.ThirdBill)).Reasons;
        var lifted = reasons.Single(r => r.Text == "Kapitalas išvažiuos į kaimynines šalis.").Id;
        var initiators = reasons.Single(r => r.Text == AppealSteps.AgainstReason).Id;
        (await writer.MineAsync(AppealSteps.ThirdBill)).Editable.ShouldBe([lifted]);

        // Two people asking for "už" and one with no side mark the writer's argument: none of it counts.
        var others = new List<TestBrowser>();
        foreach (var side in new string?[] { "for", "for", null })
        {
            var (other, _) = await Factory.MemberAsync();
            others.Add(other);
            if (side is not null)
            {
                (await other.TakeSideAsync(AppealSteps.ThirdBill, side)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            (await other.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(lifted))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // One person asking for "prieš" marks the initiator's argument: it counts, and leads.
        var (against, _) = await Factory.MemberAsync();
        others.Add(against);
        (await against.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await against.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(initiators))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var page = await writer.RecordAsync(AppealSteps.ThirdBill);
        page.Reasons.Where(r => r.Side == "against").Select(r => (r.Id, r.Endorsements)).ShouldBe([(initiators, 1), (lifted, 0)]);

        (await writer.MineAsync(AppealSteps.ThirdBill)).Editable.ShouldBeEmpty("uncounted marks are reactions too");
        using (var edit = await writer.SendAsync(HttpMethod.Put, AppealRoutes.ReasonOf(lifted), new ReasonRequest { Side = "against", Text = "Kapitalas išvažiuos." }))
        {
            await edit.ShouldBeProblemAsync(HttpStatusCode.Conflict, "reason.reacted");
        }

        foreach (var other in others)
        {
            other.Dispose();
        }
    }

    /// <summary>Design 6.5, owner 2026-11-18: a mark needs the positions consent, as a side does; nothing is stored without it.</summary>
    [Fact]
    public async Task Man_svarbu_needs_the_positions_consent()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, email) = await Factory.MemberAsync(consent: false);
        using var _m = member;
        var reasonId = (await member.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;

        using (var refused = await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId)))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.endorsement WHERE user_id = @u", ("u", await Database.UserIdAsync(email)))).ShouldBe(0);
        (await member.MineAsync(AppealSteps.ThirdBill)).HasPositionConsent.ShouldBeFalse();

        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MineAsync(AppealSteps.ThirdBill)).Endorsed.ShouldBe([reasonId]);
    }

    /// <summary>
    /// Design 6.5 as built: a consent given to an older text (2026-11-13, sides only) is not the consent in force. Nothing is
    /// deleted: the person's side and marks stay stored and counted and show as theirs, and they can still take a side back or
    /// remove a mark; a new side or mark asks for the consent again (<c>consent.required</c>), and once given to the text in
    /// force everything goes on.
    /// </summary>
    [Fact]
    public async Task A_consent_to_an_older_text_keeps_what_was_given_and_is_asked_again_before_anything_new()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reasonId = (await member.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Database.ExecuteAsync(
            "UPDATE identity.consent SET text_version = '2026-11-13' WHERE kind = 'positions' AND user_id = @u", ("u", await Database.UserIdAsync(email)));

        (await member.MeAsync()).HasPositionConsent.ShouldBeFalse();
        var record = await member.RecordAsync(AppealSteps.ThirdBill);
        record.Counts.Against.ShouldBe(2, "the side stays counted");
        record.Reasons.Single().Endorsements.ShouldBe(1, "the mark stays counted");
        var mine = await member.MineAsync(AppealSteps.ThirdBill);
        (mine.Side, mine.HasPositionConsent).ShouldBe(("against", false));
        mine.Endorsed.ShouldBe([reasonId]);

        using (var changed = await member.TakeSideAsync(AppealSteps.ThirdBill, "for"))
        {
            await changed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
        }

        (await member.SendAsync(HttpMethod.Delete, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var marked = await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId)))
        {
            await marked.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
        }

        (await member.MineAsync(AppealSteps.ThirdBill)).Side.ShouldBe("against", "a refused change changes nothing");

        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MeAsync()).HasPositionConsent.ShouldBeTrue();
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
    [Fact]
    public async Task A_similar_existing_argument_is_offered_before_a_new_one()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
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
    public async Task Man_svarbu_is_given_within_a_limit_when_one_is_set_counting_every_mark_and_a_changed_side_keeps_them()
    {
        // The limit is off by default (design 8.2); this test switches it on at 3 and back off.
        var administrator = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        await administrator.Browser.SetSettingAsync("Appeals:EndorsementsPerPerson", 3);
        try
        {
            await MarksWithinTheLimitAsync();
        }
        finally
        {
            await administrator.Browser.ResetSettingAsync("Appeals:EndorsementsPerPerson");
        }
    }

    private async Task MarksWithinTheLimitAsync()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
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
        var reasons = await member.AllReasonsAsync(AppealSteps.ThirdBill);
        reasons.Count.ShouldBe(4);

        // Owner, 2026-11-18: the other side's argument may be marked; the mark does not count while they ask for "už".
        (await supporterOfFor.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons[0].Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

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
        (await member.AllReasonsAsync(AppealSteps.ThirdBill)).Where(r => reasons.Take(3).Any(x => x.Id == r.Id)).ShouldAllBe(r => r.Endorsements == 1);

        (await member.SendAsync(HttpMethod.Delete, AppealRoutes.EndorsementOf(reasons[0].Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MineAsync(AppealSteps.ThirdBill)).Endorsed.Count.ShouldBe(2);

        // Changing side keeps the marks; none counts now (all on "prieš" arguments), and they still count towards the limit.
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.MineAsync(AppealSteps.ThirdBill)).Endorsed.Count.ShouldBe(2);
        (await member.AllReasonsAsync(AppealSteps.ThirdBill)).Sum(r => r.Endorsements).ShouldBe(0);
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons[3].Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var overLimit = await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasons[0].Id)))
        {
            await overLimit.ShouldBeProblemAsync(HttpStatusCode.Conflict, "endorsement.limit");
        }

        foreach (var browser in writers.Append(supporterOfFor).Append(member))
        {
            browser.Dispose();
        }
    }

    [Fact]
    public async Task A_member_reports_a_record_or_an_argument_once()
    {
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var record = await Database.PrepareBillAsync(initiator, (await ModeratorAsync()).Browser, AppealSteps.ThirdBill, "against");
        var reasonId = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        var (member, _) = await Factory.MemberAsync(consent: false);
        using var _m = member;

        foreach (var (kind, id) in new[] { ("record", record.Id), ("reason", reasonId), ("reason", reasonId) })
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

    /// <summary>
    /// Design 8.4: one open date flag per person and bill. The same flag sent several times at once stands once and every
    /// sending succeeds (the unique index catches the race; nothing answers 500). The race is not certain to happen on
    /// every run; when it does, this test fails without the fix.
    /// </summary>
    [Fact]
    public async Task A_date_flag_sent_several_times_at_once_stands_once_and_every_sending_succeeds()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var userId = await Database.UserIdAsync(email);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var module = scope.ServiceProvider.GetRequiredService<Lt2.Appeals.IAppealsModule>();
            return await module.FlagDateAsync(AppealSteps.ThirdBill, userId, new Lt2.Appeals.NewFlag("date-wrong", "https://www.lrs.lt/darbotvarke", null), CancellationToken.None);
        }));
        results.ShouldAllBe(r => r.IsSuccess);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.flag WHERE reporter_user_id = @u AND status = 'open'", ("u", userId))).ShouldBe(1);
    }
}
