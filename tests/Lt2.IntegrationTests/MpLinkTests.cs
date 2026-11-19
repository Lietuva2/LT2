using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Lt2.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.5.1 to 8.5.4 and their part of 8.5.8: an MP is recognised by the official address, takes the profile over with a
/// link sent only to that address and a new passkey, and adds office members; refusals of guessed, reused, expired and
/// foreign tokens and of an address no longer official; a code session speaks for nobody and changes no passkey of a linked
/// account; the MP's own account takes no part. Each test takes its own stand-in MP and deletes the accounts it made.
/// </summary>
public sealed class MpLinkTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly string Setting = SettingKeys.RegistrationOfficialAddressesWithoutInvitation.Name;

    [Fact]
    public async Task An_official_address_is_told_so_at_once_and_nothing_is_linked_or_public_before_the_takeover()
    {
        // Case does not matter (design 8.5.1).
        var typed = MpSteps.Uosyte.Address.Replace("pavyzdys.laima", "Pavyzdys.Laima", StringComparison.Ordinal).Replace("lrs.lt", "LRS.lt", StringComparison.Ordinal);
        using var mp = await Factory.OfficialMemberAsync(MpSteps.Uosyte, typed);
        using var former = await Factory.OfficialMemberAsync(MpSteps.Alksninis);
        var (ordinary, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _o = ordinary;
        var slug = (await Database.SlugAsync(MpSteps.Uosyte))!;
        try
        {
            var me = await mp.MeAsync();
            me.MpOffer.ShouldBe(new MpOfferResponse(slug, "Laima", "Uosytė"));
            me.Mp.ShouldBeNull();

            // A former MP's address and any other address are official for nobody.
            (await former.MeAsync()).MpOffer.ShouldBeNull();
            (await ordinary.MeAsync()).MpOffer.ShouldBeNull();

            // Registering links nothing and marks no profile.
            (await Database.OpenLinksAsync(MpSteps.Uosyte.SeimasPersonId, "mp")).ShouldBe(0);
            using var visitor = NewBrowser();
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/p/{slug}"));
            page.ShouldContain("Laima Uosytė", customMessage: "the swept page is the profile");
            page.ShouldNotContain("Profilį tvarko");
            (await visitor.GetJsonAsync<Lt2.Api.Contracts.Parliament.MpProfileResponse>($"/api/v1/mps/{slug}")).ManagedSince.ShouldBeNull();

            // The offer is only in the account's own answer from the browser, never in the page the server renders.
            var hello = WebUtility.HtmlDecode(await mp.Client.GetStringAsync("/paskyra"));
            hello.ShouldContain("Laima Uosytė", customMessage: "the account page was rendered for this account (its display name)");
            hello.ShouldNotContain("oficialiu adresu");
            var state = MpSteps.StateOf(hello);
            state.ShouldContain("registrationComplete", customMessage: "the decoded state holds the account");
            state.ShouldNotContain("\"mpOffer\":{", Case.Insensitive);
            state.ShouldNotContain("laima-uosyte");

            // Owner, 2026-11-17: the notice on every page is served hidden and without the name, the same for every member, and
            // only to members; the browser fills it from GET /me.
            var notice = NoticeOf(await mp.Client.GetStringAsync("/darbotvarke"));
            notice.ShouldContain("hidden");
            notice.ShouldContain("href=\"/paskyra/dalyvavimas#mp-offer\"");
            WebUtility.HtmlDecode(notice).ShouldNotContain("Uosytė");
            NoticeOf(await ordinary.Client.GetStringAsync("/darbotvarke")).ShouldBe(notice);
            (await visitor.Client.GetStringAsync("/darbotvarke")).ShouldNotContain("mp-offer-notice");
        }
        finally
        {
            await mp.DeleteAccountAsync();
            await former.DeleteAccountAsync();
        }
    }

    /// <summary>The take-over notice's markup under the header (design 8.5.1, owner 2026-11-17).</summary>
    private static string NoticeOf(string html)
    {
        var start = html.IndexOf("<div class=\"page\" id=\"mp-offer-notice\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "a member's page carries the notice's markup");
        return html[start..(html.IndexOf("</div>", start, StringComparison.Ordinal) + 6)];
    }

    [Fact]
    public async Task With_the_setting_on_an_official_address_registers_without_an_invitation_and_no_other_address_does()
    {
        var admin = (await AdministratorAsync()).Browser;
        await Database.ForgetAccountAsync(MpSteps.Liepaitis.Address);

        // Off by default: an invitation is needed, official address or not.
        using (var browser = NewBrowser())
        using (var refused = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = MpSteps.Liepaitis.Address }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
        }

        await using (await admin.WithSettingAsync(Setting, true))
        {
            using (var ordinary = NewBrowser())
            using (var refused = await ordinary.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = TestData.Email() }))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
            }

            // Both gates let the official address through: registration and its completion.
            using var mp = await Factory.OfficialMemberAsync(MpSteps.Liepaitis, invitation: null);
            try
            {
                (await mp.MeAsync()).MpOffer!.LastName.ShouldBe("Liepaitis");
                (await Database.ScalarAsync<long>(
                    "SELECT count(*) FROM identity.profile p JOIN identity.\"user\" u ON u.id = p.user_id WHERE u.email = @e AND p.registered_at IS NOT NULL AND p.invitation_id IS NULL",
                    ("e", MpSteps.Liepaitis.Address))).ShouldBe(1);
            }
            finally
            {
                await mp.DeleteAccountAsync();
            }
        }
    }

    [Fact]
    public async Task Perimti_profili_sends_the_link_only_to_the_official_address_three_times_a_day_at_most()
    {
        var start = await Database.ScalarAsync<DateTime>("SELECT now()");
        using var mp = await Factory.OfficialMemberAsync(MpSteps.Pusinskiene);
        var (ordinary, ordinaryEmail) = await Factory.MemberAsync("Rūta Mockutė");
        using var _o = ordinary;
        var slug = (await Database.SlugAsync(MpSteps.Pusinskiene))!;
        try
        {
            // Nobody else can make the site send it: not an ordinary member, not this account for another MP.
            using (var other = await ordinary.ClaimAsync(slug))
            {
                await other.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.not-official-address");
            }

            using (var wrong = await mp.ClaimAsync((await Database.SlugAsync(MpSteps.Gluosnis))!))
            {
                await wrong.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.not-official-address");
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = @k AND payload->>'to' = @to", ("k", MessageKinds.MpTakeoverLinkEmail), ("to", ordinaryEmail)))
                .ShouldBe(0, "no link went to the ordinary member's own mailbox either");

            using (var claimed = await mp.ClaimAsync(slug))
            {
                claimed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await claimed.Content.ReadAsStringAsync());
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = @k AND created_at >= @start AND payload->>'to' = @to", ("k", MessageKinds.MpTakeoverLinkEmail), ("start", start), ("to", MpSteps.Pusinskiene.Address)))
                .ShouldBe(1, "the link went to the official address");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = @k AND created_at >= @start AND payload->>'to' <> @to", ("k", MessageKinds.MpTakeoverLinkEmail), ("start", start), ("to", MpSteps.Pusinskiene.Address)))
                .ShouldBe(0, "and to no other address");
            var first = await Database.TakeoverTokenAsync(MpSteps.Pusinskiene.Address);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.politician_token WHERE token_hash = sha256(convert_to(@t, 'UTF8'))", ("t", first)))
                .ShouldBe(1, "the token is stored only as its hash");

            // A new one replaces the old; the third a day is the last.
            for (var i = 0; i < 2; i++)
            {
                using var again = await mp.ClaimAsync(slug);
                again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            using (var fourth = await mp.ClaimAsync(slug))
            {
                await fourth.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "mp.link-limit");
            }

            using var authenticator = new SoftwareAuthenticator();
            using (var replaced = await mp.TakeOverAsync(authenticator, first))
            {
                await replaced.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            }

            using (var newest = await mp.TakeOverAsync(authenticator, await Database.TakeoverTokenAsync(MpSteps.Pusinskiene.Address)))
            {
                newest.StatusCode.ShouldBe(HttpStatusCode.OK, await newest.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            await mp.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task The_link_with_a_new_passkey_links_the_account_removes_its_other_passkeys_withdraws_its_sides_and_shows_the_mark()
    {
        const string Bill = AppealSteps.ThirdBill;
        await Database.FreeBillAsync(Bill);
        var noticesBefore = (await Database.MpNoticesAsync(MpSteps.Gluosnis.Address)).Count;
        using var mp = await Factory.OfficialMemberAsync(MpSteps.Gluosnis);
        var slug = (await Database.SlugAsync(MpSteps.Gluosnis))!;
        using var old = new SoftwareAuthenticator();
        using var oldSession = NewBrowser();
        try
        {
            // Before: a passkey of its own, a session signed in with it, and a side on an open bill.
            (await mp.AddPasskeyAsync(old)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await oldSession.SignInWithPasskeyAsync(old)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await mp.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var before = (await mp.RecordAsync(Bill)).Counts.For;
            Factory.Services.GetRequiredService<FrontPageFreshness>().Built();

            using (var claimed = await mp.ClaimAsync(slug))
            {
                claimed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            using var authenticator = new SoftwareAuthenticator();
            using (var taken = await mp.TakeOverAsync(authenticator, await Database.TakeoverTokenAsync(MpSteps.Gluosnis.Address)))
            {
                taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
                (await taken.ReadAsync<MpLinkResponse>()).ShouldBe(new MpLinkResponse(slug, "Kęstutis", "Gluosnis", "mp", true));
            }

            // The account: linked, one passkey (the new one), this session a passkey session, the old one's session gone.
            var me = await mp.MeAsync();
            me.Mp.ShouldBe(new MpLinkResponse(slug, "Kęstutis", "Gluosnis", "mp", true));
            me.MpOffer.ShouldBeNull();
            me.PasskeyCount.ShouldBe(1);
            (await mp.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!.Single(s => s.IsCurrent).Method.ShouldBe("pop");
            (await oldSession.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            using (var oldAgain = NewBrowser())
            {
                await (await oldAgain.SignInWithPasskeyAsync(old)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
            }

            // Its side went, and the counts with it; the front page's copy is out of date.
            (await mp.RecordAsync(Bill)).Counts.For.ShouldBe(before - 1);
            (await mp.MineAsync(Bill)).Side.ShouldBeNull();
            Factory.Services.GetRequiredService<FrontPageFreshness>().IsStale.ShouldBeTrue();

            // The mark on the profile, with the date; audited and told to the official address.
            using var visitor = NewBrowser();
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/p/{slug}"));
            page.ShouldContain("Profilį tvarko Seimo narys arba jo biuras");
            page.ShouldContain(Lt2.Web.Client.Display.Day(DateTimeOffset.UtcNow));
            (await visitor.GetJsonAsync<Lt2.Api.Contracts.Parliament.MpProfileResponse>($"/api/v1/mps/{slug}")).ManagedSince.ShouldNotBeNull();
            (await Database.MpNoticesAsync(MpSteps.Gluosnis.Address, noticesBefore)).ShouldBe(["mp-linked"]);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'mp.linked' AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)", ("e", MpSteps.Gluosnis.Address)))
                .ShouldBe(1);
        }
        finally
        {
            await mp.DeleteAccountAsync();
        }

        // Deleting the account ends the link: the mark goes at once.
        using var later = NewBrowser();
        WebUtility.HtmlDecode(await later.Client.GetStringAsync($"/p/{slug}")).ShouldNotContain("Profilį tvarko");
        (await Database.OpenLinksAsync(MpSteps.Gluosnis.SeimasPersonId, "mp")).ShouldBe(0);
    }

    [Fact]
    public async Task A_guessed_reused_expired_or_another_accounts_token_is_refused_and_leaves_nothing()
    {
        using var mp = await Factory.OfficialMemberAsync(MpSteps.Eglaite);
        using var other = await Factory.OfficialMemberAsync(MpSteps.Sermuksnyte);
        var slug = (await Database.SlugAsync(MpSteps.Eglaite))!;
        using var authenticator = new SoftwareAuthenticator();
        try
        {
            using (var guessed = await mp.TakeOverAsync(authenticator, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"))
            {
                await guessed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            }

            // Another account's token: the other MP's account claimed its own; this account cannot use it.
            using (var claimed = await other.ClaimAsync((await Database.SlugAsync(MpSteps.Sermuksnyte))!))
            {
                claimed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            using (var foreign = await mp.TakeOverAsync(authenticator, await Database.TakeoverTokenAsync(MpSteps.Sermuksnyte.Address)))
            {
                await foreign.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            }

            // Expired.
            (await mp.ClaimAsync(slug)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            var token = await Database.TakeoverTokenAsync(MpSteps.Eglaite.Address);
            await Database.ExecuteAsync("UPDATE identity.politician_token SET expires_at = now() - interval '1 minute' WHERE token_hash = sha256(convert_to(@t, 'UTF8'))", ("t", token));
            using (var expired = await mp.TakeOverAsync(authenticator, token))
            {
                await expired.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            }

            // Nothing was half done.
            (await Database.OpenLinksAsync(MpSteps.Eglaite.SeimasPersonId, "mp")).ShouldBe(0);
            (await mp.MeAsync()).PasskeyCount.ShouldBe(0);

            // Used once, then reused.
            (await mp.ClaimAsync(slug)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            token = await Database.TakeoverTokenAsync(MpSteps.Eglaite.Address);
            (await mp.TakeOverAsync(authenticator, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
            using var second = new SoftwareAuthenticator();
            using (var reused = await mp.TakeOverAsync(second, token))
            {
                await reused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            }

            (await mp.MeAsync()).PasskeyCount.ShouldBe(1, "the reused link added no passkey");
        }
        finally
        {
            await mp.DeleteAccountAsync();
            await other.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task An_address_that_is_no_longer_official_is_refused_and_nothing_is_half_done()
    {
        using var mp = await Factory.OfficialMemberAsync(MpSteps.Kadagyte);
        var slug = (await Database.SlugAsync(MpSteps.Kadagyte))!;
        using var authenticator = new SoftwareAuthenticator();
        const string Moved = "pavyzdys.aurelija.kadagyte.nauja@lrs.lt";
        try
        {
            (await mp.ClaimAsync(slug)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            var token = await Database.TakeoverTokenAsync(MpSteps.Kadagyte.Address);

            // Open data now lists another address for the MP.
            await Database.ExecuteAsync("UPDATE parliament.contact SET value = @moved WHERE value = @old", ("moved", Moved), ("old", MpSteps.Kadagyte.Address));
            using (var refused = await mp.TakeOverAsync(authenticator, token))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.not-official-address");
            }

            (await Database.OpenLinksAsync(MpSteps.Kadagyte.SeimasPersonId, "mp")).ShouldBe(0);
            (await mp.MeAsync()).PasskeyCount.ShouldBe(0);
            (await mp.MeAsync()).MpOffer.ShouldBeNull("the address is official for nobody now");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.politician_token WHERE token_hash = sha256(convert_to(@t, 'UTF8')) AND used_at IS NULL", ("t", token)))
                .ShouldBe(1, "the refused request used nothing up");
        }
        finally
        {
            await Database.ExecuteAsync("UPDATE parliament.contact SET value = @old WHERE value = @moved", ("moved", Moved), ("old", MpSteps.Kadagyte.Address));
            await mp.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task Signed_in_by_a_code_a_linked_account_reads_only_and_a_lost_passkey_is_replaced_by_a_new_link()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        using var codeSession = NewBrowser();
        try
        {
            (await codeSession.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = mp.Email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await codeSession.SignInWithCodeAsync(mp.Email);

            // It reads its own: the link (may not write) and the office.
            (await codeSession.MeAsync()).Mp!.MayWrite.ShouldBeFalse();
            (await codeSession.GetJsonAsync<MpOfficeResponse>(MpRoutes.Office)).Members.ShouldBeEmpty();

            // It speaks for nobody and changes no passkey.
            using var stranger = new SoftwareAuthenticator();
            await (await codeSession.AddPasskeyAsync(stranger)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            await (await codeSession.PostAsync(MpRoutes.OfficeInvitations, new { })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            await (await codeSession.SendAsync(HttpMethod.Delete, MpRoutes.Mine)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            (await mp.Browser.MeAsync()).PasskeyCount.ShouldBe(1);
            (await Database.OpenLinksAsync(MpSteps.Ievaite.SeimasPersonId, "mp")).ShouldBe(1);

            // A lost passkey: a new link to the official address and a new passkey; the old one goes, the link stays.
            var since = await Database.ScalarAsync<DateTime>("SELECT created_at FROM identity.politician_link WHERE seimas_person_id = @p AND ended_at IS NULL", ("p", MpSteps.Ievaite.SeimasPersonId));
            (await codeSession.ClaimAsync(mp.Slug)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            using var replacement = new SoftwareAuthenticator();
            using (var replaced = await codeSession.TakeOverAsync(replacement, await Database.TakeoverTokenAsync(mp.Email)))
            {
                replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
            }

            (await codeSession.MeAsync()).Mp!.MayWrite.ShouldBeTrue("the session became a passkey session");
            (await codeSession.MeAsync()).PasskeyCount.ShouldBe(1);
            (await mp.Browser.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the lost passkey's session ended with it");
            using (var lost = NewBrowser())
            {
                await (await lost.SignInWithPasskeyAsync(mp.Authenticator)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
            }

            (await Database.ScalarAsync<DateTime>("SELECT created_at FROM identity.politician_link WHERE seimas_person_id = @p AND ended_at IS NULL", ("p", MpSteps.Ievaite.SeimasPersonId)))
                .ShouldBe(since, "the link stays");
        }
        finally
        {
            await codeSession.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task The_mps_own_account_takes_no_part_and_can_still_follow()
    {
        const string Bill = AppealSteps.ThirdBill;
        using var mp = await Factory.MpAccountAsync(MpSteps.Azuolaitis);
        var moderator = (await ModeratorAsync()).Browser;
        var (writer, _) = await Factory.MemberAsync("Rūta Mockutė");
        using var _w = writer;
        try
        {
            await Database.PrepareBillAsync(writer, moderator, Bill, side: "for");
            var reason = (await mp.Browser.RecordAsync(Bill)).Reasons[0].Id;

            await (await mp.Browser.TakeSideAsync(Bill, "for")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
            await (await mp.Browser.WriteReasonAsync(Bill, "for", "Seimo nario argumentas, kurio būti neturi.")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
            await (await mp.Browser.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reason))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
            var parties = await mp.Browser.MyProfileAsync();
            await (await mp.Browser.SavePartyAsync(parties.PartyChoices[0]))
                .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
            await (await mp.Browser.PostAsync(OrganisationRoutes.Organisations, new OrganisationRequest { Name = OrganisationSteps.NewName("Seimo nario asociacija"), Description = "Išgalvota." }))
                .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
            (await mp.Browser.MineAsync(Bill)).Side.ShouldBeNull();

            // Following is open to it, as to anyone.
            await mp.Browser.FollowBillAsync(Bill);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task An_office_member_is_added_by_a_link_in_a_passkey_session_writes_freely_and_can_be_removed_or_leave()
    {
        const string Bill = AppealSteps.ThirdBill;
        await Database.FreeBillAsync(Bill);
        var noticesBefore = (await Database.MpNoticesAsync(MpSteps.Berzinyte.Address)).Count;
        using var mp = await Factory.MpAccountAsync(MpSteps.Berzinyte);
        var (member, memberKey, memberEmail) = await Factory.PasskeyMemberAsync("Biuro Narė");
        var (second, secondKey, _) = await Factory.PasskeyMemberAsync("Antra Biuro Narė");
        using var _m = member;
        using var _mk = memberKey;
        using var _s = second;
        using var _sk = secondKey;
        try
        {
            var invitation = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new { });
            invitation.Path.ShouldStartWith(MpRoutes.OfficeInvitationPage + "#", Case.Sensitive, "design 6.8: the token after the #");
            var token = Uri.UnescapeDataString(invitation.Path[(invitation.Path.IndexOf('#', StringComparison.Ordinal) + 1)..]);

            // Signed in by a code, the invited member cannot accept; the MP's own account cannot either.
            using (var codeSession = NewBrowser())
            {
                (await codeSession.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = memberEmail })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
                await codeSession.SignInWithCodeAsync(memberEmail);
                await (await codeSession.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            }

            await (await mp.Browser.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp.already-linked");

            var joined = await member.PostJsonAsync<MpLinkResponse>(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token });
            joined.ShouldBe(new MpLinkResponse(mp.Slug, "Eglė", "Beržinytė", "office", true));
            await (await second.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            (await member.MeAsync()).Mp.ShouldBe(joined);

            // The MP's account sees its office by name; nobody else does.
            var office = await mp.Browser.GetJsonAsync<MpOfficeResponse>(MpRoutes.Office);
            office.Members.Select(m => m.DisplayName).ShouldBe(["Biuro Narė"]);
            office.Limit.ShouldBe(5);
            await (await member.Client.GetAsync(MpRoutes.Office)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.not-mp");
            await (await second.Client.GetAsync(MpRoutes.Office)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");

            // An office member is otherwise an ordinary member: takes a side; but its passkeys change only in a passkey session.
            (await member.TakeSideAsync(Bill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(Bill))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using (var codeSession = NewBrowser())
            {
                (await codeSession.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = memberEmail })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
                await codeSession.SignInWithCodeAsync(memberEmail);
                using var stranger = new SoftwareAuthenticator();
                await (await codeSession.AddPasskeyAsync(stranger)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
                (await codeSession.MeAsync()).Mp!.MayWrite.ShouldBeFalse();
            }

            // A second member joins and leaves.
            var next = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new { });
            await second.PostJsonAsync<MpLinkResponse>(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = Uri.UnescapeDataString(next.Path[(next.Path.IndexOf('#', StringComparison.Ordinal) + 1)..]) });
            (await second.SendAsync(HttpMethod.Delete, MpRoutes.Mine)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await second.MeAsync()).Mp.ShouldBeNull();

            // The MP's account removes the first.
            var id = (await mp.Browser.GetJsonAsync<MpOfficeResponse>(MpRoutes.Office)).Members.Single().Id;
            (await mp.Browser.SendAsync(HttpMethod.Delete, MpRoutes.OfficeMemberOf(id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.MeAsync()).Mp.ShouldBeNull("removed at once");
            (await mp.Browser.GetJsonAsync<MpOfficeResponse>(MpRoutes.Office)).Members.ShouldBeEmpty();

            // All of it told to the official address, without names.
            (await Database.MpNoticesAsync(MpSteps.Berzinyte.Address, noticesBefore)).ShouldBe(["mp-linked", "mp-office-added", "mp-office-added", "mp-office-left", "mp-office-removed"]);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Giving_the_profile_up_and_an_administrators_end_take_the_mark_off_at_once_and_end_the_office()
    {
        var administrator = await AdministratorAsync();
        var noticesBefore = (await Database.MpNoticesAsync(MpSteps.Klevinskas.Address)).Count;
        using var mp = await Factory.MpAccountAsync(MpSteps.Klevinskas);
        var (member, memberKey, _) = await Factory.PasskeyMemberAsync("Biuro Narys");
        using var _m = member;
        using var _k = memberKey;
        try
        {
            var invitation = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new { });
            await member.PostJsonAsync<MpLinkResponse>(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = Uri.UnescapeDataString(invitation.Path[(invitation.Path.IndexOf('#', StringComparison.Ordinal) + 1)..]) });

            // An administrator ends the link with a statement and a fresh confirmation.
            var end = MpRoutes.EndLinkOf(mp.Slug);
            await (await administrator.Browser.PostAsync(end, new StatementRequest { Statement = "" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
            await Database.ExecuteAsync("UPDATE identity.session SET passkey_confirmed_at = now() - interval '1 hour' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", administrator.Email));
            await (await administrator.Browser.PostAsync(end, new StatementRequest { Statement = "Susieta per klaidą." })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
            (await administrator.Browser.ConfirmPasskeyAsync(administrator.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await administrator.Browser.PostAsync(end, new StatementRequest { Statement = "Susieta per klaidą." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // At once: no mark, no link, no office.
            using var visitor = NewBrowser();
            WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/p/{mp.Slug}")).ShouldNotContain("Profilį tvarko");
            (await mp.Browser.MeAsync()).Mp.ShouldBeNull();
            (await member.MeAsync()).Mp.ShouldBeNull();
            (await Database.ScalarAsync<string>("SELECT payload->>'statement' FROM notify.outbox WHERE payload->>'notice' = 'mp-link-ended' AND payload->>'to' = @to", ("to", mp.Email)))
                .ShouldBe("Susieta per klaidą.");

            // Taken over again, then given up by the MP's account itself.
            (await mp.Browser.ClaimAsync(mp.Slug)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            using var again = new SoftwareAuthenticator();
            (await mp.Browser.TakeOverAsync(again, await Database.TakeoverTokenAsync(mp.Email))).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await mp.Browser.SendAsync(HttpMethod.Delete, MpRoutes.Mine)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/p/{mp.Slug}")).ShouldNotContain("Profilį tvarko");
            (await mp.Browser.MeAsync()).MpOffer!.Slug.ShouldBe(mp.Slug, "offered again");
            (await Database.MpNoticesAsync(mp.Email, noticesBefore)).ShouldBe(["mp-linked", "mp-office-added", "mp-link-ended", "mp-linked", "mp-given-up"]);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task No_public_answer_page_or_log_names_the_linked_account_or_the_office_members()
    {
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        using var mp = await logged.MpAccountAsync(MpSteps.Klevinskas);
        var (member, memberKey, memberEmail) = await logged.PasskeyMemberAsync("Slaptas Biuro Narys");
        using var _m = member;
        using var _k = memberKey;
        string officeToken;
        string takeoverToken;
        try
        {
            var invitation = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new { });
            officeToken = Uri.UnescapeDataString(invitation.Path[(invitation.Path.IndexOf('#', StringComparison.Ordinal) + 1)..]);
            await member.PostJsonAsync<MpLinkResponse>(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = officeToken });
            takeoverToken = await Database.TakeoverTokenAsync(mp.Email);
            var mpAccountId = (await Database.UserIdAsync(mp.Email)).ToString();
            var memberId = (await Database.UserIdAsync(memberEmail)).ToString();
            string[] secrets = [mp.Email, memberEmail, "Slaptas Biuro Narys", mpAccountId, memberId, officeToken, takeoverToken];

            // The public answer: exactly its fields, the mark and no more.
            using var visitor = logged.NewBrowser();
            var json = await visitor.Client.GetStringAsync($"/api/v1/mps/{mp.Slug}");
            using (var document = JsonDocument.Parse(json))
            {
                document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(
                    ["mp", "term", "mandate", "memberships", "contacts", "statistics", "recentVotes", "managedSince", "followers"], ignoreOrder: true);
                document.RootElement.GetProperty("managedSince").ValueKind.ShouldBe(JsonValueKind.String, "the swept answer holds the mark");
            }

            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/p/{mp.Slug}"));
            page.ShouldContain("Profilį tvarko Seimo narys arba jo biuras", customMessage: "the swept page holds the mark");
            foreach (var secret in secrets.Skip(1))
            {
                json.ShouldNotContain(secret);
                page.ShouldNotContain(secret);
            }

            // The account's own page as the server renders it holds nothing of the link (design 8.5.4).
            var hello = WebUtility.HtmlDecode(await mp.Browser.Client.GetStringAsync("/paskyra"));
            hello.ShouldContain("Vytautas Klevinskas", customMessage: "the account page was rendered for this account");
            hello.ShouldNotContain("Tvarkote");
            hello.ShouldNotContain("Slaptas Biuro Narys");
            var state = MpSteps.StateOf(hello);
            state.ShouldContain("registrationComplete", customMessage: "the decoded state holds the account");
            state.ShouldNotContain("\"mp\":{", Case.Insensitive);
            state.ShouldNotContain(mp.Slug);

            // No log line names an address, an account, an office member or a token.
            var lines = sink.Lines;
            lines.ShouldNotBeEmpty();
            foreach (var secret in secrets)
            {
                lines.ShouldNotContain(l => l.Contains(secret, StringComparison.OrdinalIgnoreCase), secret);
            }
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    /// <summary>
    /// Design 8.5.2, 6.8 (owner, 2026-11-17): the take-over link's secret is a typeable one-time code; the email gives the page and
    /// the code beside the link, and the code works typed in any case with spaces.
    /// </summary>
    [Fact]
    public async Task The_takeover_link_carries_a_typeable_code_the_email_also_gives_and_it_works_typed()
    {
        using var mp = await Factory.OfficialMemberAsync(MpSteps.Azuolaitis);
        using var key = new SoftwareAuthenticator();
        var slug = (await Database.SlugAsync(MpSteps.Azuolaitis))!;
        try
        {
            using (var claimed = await mp.ClaimAsync(slug))
            {
                claimed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await claimed.Content.ReadAsStringAsync());
            }

            var code = await Database.TakeoverTokenAsync(MpSteps.Azuolaitis.Address);
            OneTimeCode.Normalize(code).ShouldBe(code, "a typeable one-time code in its one form");
            var payload = await Database.ScalarAsync<string>(
                "SELECT payload::text FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) ORDER BY created_at DESC, id DESC LIMIT 1",
                ("k", MessageKinds.MpTakeoverLinkEmail), ("to", MpSteps.Azuolaitis.Address));
            using (var document = JsonDocument.Parse(payload!))
            {
                document.RootElement.GetProperty("code").GetString().ShouldBe(code);
                document.RootElement.GetProperty("pagePath").GetString().ShouldBe(MpRoutes.TakeoverPage);
            }

            using var taken = await mp.TakeOverAsync(key, code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal));
            taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
            (await Database.OpenLinksAsync(MpSteps.Azuolaitis.SeimasPersonId, "mp")).ShouldBe(1);
        }
        finally
        {
            await mp.DeleteAccountAsync();
        }
    }
}