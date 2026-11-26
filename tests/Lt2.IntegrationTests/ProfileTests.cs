using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.25: a member's public profile by their own choice ("Viešas profilis", the consent public-profile), "Apie mane",
/// the page /nariai/{id} (410 once switched off, 404 never issued), the name beside arguments, following a member and
/// "Sekėjai: n", reports and moderation, the export and deletion; and what never shows.
/// </summary>
public sealed class ProfileTests(DatabaseFixture database) : ApiTest(database)
{
    private const string MemberEmail = "email.follow-member";

    private Task<long> ConsentsAsync(string email, bool current) =>
        Database.ScalarAsync<long>(
            $"SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = 'public-profile' AND c.text_version = @v AND c.withdrawn_at IS {(current ? "" : "NOT ")}NULL",
            ("e", email), ("v", PublicProfileConsent.TextVersion))!;

    private async Task<FollowRunOutcome> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_member_switches_the_profile_on_with_the_consent_and_gets_an_indexed_page_with_name_and_about_me()
    {
        var (member, email) = await Factory.MemberAsync("Rūta Mockutė");
        using var _ = member;
        var off = await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine);
        off.ProfileId.ShouldBeNull("nobody has a public profile until they switch it on");
        off.PublicConsentVersion.ShouldBe(PublicProfileConsent.TextVersion);

        var on = await member.SavedProfileAsync("Mokytoja iš Kauno. https://pavyzdys.test/ruta", isPublic: true);
        var id = on.ProfileId.ShouldNotBeNull();
        id.Length.ShouldBe(10);
        (await ConsentsAsync(email, current: true)).ShouldBe(1);

        using var visitor = NewBrowser();
        var page = await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id));
        page.ShouldBe(new MemberProfileResponse(id, "Rūta Mockutė", "Mokytoja iš Kauno. https://pavyzdys.test/ruta", 0, $"/nariai/ruta-mockute-{id}"));

        using var html = await visitor.Client.GetAsync(ProfileRoutes.Page(id, "Rūta Mockutė"), TestContext.Current.CancellationToken);
        html.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.Headers.Contains("X-Robots-Tag").ShouldBeFalse("a public profile is indexed (design 8.25.2)");
        html.Headers.CacheControl?.NoCache.ShouldBe(true);
        var markup = WebUtility.HtmlDecode(await html.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        markup.ShouldContain("<h1 id=\"member-name\">Rūta Mockutė</h1>");
        markup.ShouldContain($"<div class=\"member-head\"><span class=\"initials initials--{Lt2.Web.Client.Components.MemberBadges.Colour(id)}\" aria-hidden=\"true\">RM</span><h1", Case.Sensitive, "the badge stands beside the heading (design 8.25.4)");
        markup.ShouldContain("href=\"https://pavyzdys.test/ruta\" rel=\"nofollow ugc noopener noreferrer\" target=\"_blank\"");
        markup.ShouldNotContain("id=\"followers\"", Case.Sensitive, "nothing with no followers");
    }

    [Fact]
    public async Task The_page_renders_for_signed_in_members_too_and_offers_no_report_on_the_members_own_page()
    {
        // Browser look, 2026-11-17: "Sekti" and "Pranešti apie profilį" prerendered side by side read the person's own profile
        // at the same time, and the page answered 500 to a signed-in member now and then. Several loads make the race show.
        var (owner, _, id) = await Factory.PublicMemberAsync("Puslapio Savininkė");
        var (reader, _) = await Factory.MemberAsync("Puslapio Skaitytoja");
        using var _o = owner;
        using var _r = reader;
        var path = ProfileRoutes.Page(id, "Puslapio Savininkė");

        for (var i = 0; i < 10; i++)
        {
            foreach (var (who, client) in new[] { ("another member", reader), ("the member", owner) })
            {
                using var html = await client.Client.GetAsync(path, TestContext.Current.CancellationToken);
                html.StatusCode.ShouldBe(HttpStatusCode.OK, $"{who}, load {i + 1}");
                var markup = WebUtility.HtmlDecode(await html.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                markup.ShouldContain("<h1 id=\"member-name\">Puslapio Savininkė</h1>");
                if (client == owner)
                {
                    markup.ShouldNotContain("Pranešti apie profilį", Case.Sensitive, "nothing to report on one's own page");
                    markup.ShouldContain("id=\"follow-own-link\"");
                }
                else
                {
                    markup.ShouldContain("Pranešti apie profilį");
                    markup.ShouldContain("id=\"follow-toggle-member\"");
                }
            }
        }
    }

    [Fact]
    public async Task A_profile_switched_off_answers_410_for_ever_and_a_new_one_gets_a_new_address_while_an_id_never_issued_is_404()
    {
        var (member, email, first) = await Factory.PublicMemberAsync();
        using var _ = member;
        using var visitor = NewBrowser();

        var unknown = ProfileSteps.UnknownProfileId();
        using (var never = await visitor.Client.GetAsync(ProfileRoutes.MemberOf(unknown), TestContext.Current.CancellationToken))
        {
            await never.ShouldBeProblemAsync(HttpStatusCode.NotFound, "member.not-found");
        }

        using (var neverPage = await visitor.Client.GetAsync(ProfileRoutes.Page(unknown, null), TestContext.Current.CancellationToken))
        {
            neverPage.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            neverPage.Headers.GetValues("X-Robots-Tag").ShouldContain("noindex");
        }

        var off = await member.SavedProfileAsync("Mokytoja iš Kauno.", isPublic: false);
        off.ProfileId.ShouldBeNull();
        (await ConsentsAsync(email, current: true)).ShouldBe(0);
        (await ConsentsAsync(email, current: false)).ShouldBe(1, "switching off withdraws the consent");

        using (var gone = await visitor.Client.GetAsync(ProfileRoutes.MemberOf(first), TestContext.Current.CancellationToken))
        {
            await gone.ShouldBeProblemAsync(HttpStatusCode.Gone, "member.gone");
        }

        using (var gonePage = await visitor.Client.GetAsync(ProfileRoutes.Page(first, null), TestContext.Current.CancellationToken))
        {
            gonePage.StatusCode.ShouldBe(HttpStatusCode.Gone);
            gonePage.Headers.GetValues("X-Robots-Tag").ShouldContain("noindex");
            (await gonePage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("Rūta Mockutė");
        }

        var again = await member.SavedProfileAsync("Mokytoja iš Kauno.", isPublic: true);
        again.ProfileId.ShouldNotBeNull().ShouldNotBe(first);
        using (var stillGone = await visitor.Client.GetAsync(ProfileRoutes.MemberOf(first), TestContext.Current.CancellationToken))
        {
            stillGone.StatusCode.ShouldBe(HttpStatusCode.Gone, "a retired address is never used again");
        }
    }

    [Fact]
    public async Task Switching_on_needs_the_text_in_force_and_a_name_and_the_name_cannot_be_emptied_while_public()
    {
        var (nameless, _) = await Factory.MemberAsync(displayName: null);
        using var _n = nameless;
        using (var outdated = await nameless.SaveProfileAsync(null, isPublic: true, consentVersion: "2020-01-01"))
        {
            await outdated.ShouldBeProblemAsync(HttpStatusCode.Conflict, "profile.consent-outdated");
        }

        using (var noName = await nameless.SaveProfileAsync(null, isPublic: true))
        {
            await noName.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "profile.name-required");
        }

        (await nameless.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).ProfileId.ShouldBeNull("nothing was switched on");

        var (member, _, _) = await Factory.PublicMemberAsync();
        using var _m = member;
        using var emptied = await member.SaveFormAsync(f => f with { DisplayName = null });
        await emptied.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "profile.name-required");
    }

    [Fact]
    public async Task About_me_allows_links_and_ones_own_contacts_refuses_a_personal_code_and_is_blocked_by_a_writing_restriction()
    {
        var (member, memberEmail) = await Factory.MemberAsync();
        using var _ = member;
        (await member.SavedProfileAsync("Rašykite: ruta@pavyzdys.test, +370 600 00000, https://pavyzdys.test", isPublic: false)).About
            .ShouldBe("Rašykite: ruta@pavyzdys.test, +370 600 00000, https://pavyzdys.test");
        using (var code = await member.SaveProfileAsync("Asmens kodas 39001010000", isPublic: false))
        {
            await code.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "about.personal-code");
        }

        using (var tooLong = await member.SaveProfileAsync(new string('a', 2001), isPublic: false))
        {
            await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "about.too-long");
        }

        // A writing restriction (design 8.3) blocks changing the text; the existing one stays.
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        using var written = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Argumentas, dėl kurio apribota.");
        written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        var reasonId = (await written.Content.ReadFromJsonAsync<ReasonCreatedResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!.Id;
        var moderator = (await ModeratorAsync()).Browser;
        using (var restricted = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Statement = "Bandymas: rašymas apribotas." }))
        {
            restricted.StatusCode.ShouldBe(HttpStatusCode.Created, await restricted.Content.ReadAsStringAsync());
        }

        using (var refused = await member.SaveProfileAsync("Naujas tekstas", isPublic: false))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }

        (await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).About.ShouldBe("Rašykite: ruta@pavyzdys.test, +370 600 00000, https://pavyzdys.test");
        await Database.ExecuteAsync("DELETE FROM appeals.writing_restriction WHERE user_id = @u", ("u", await Database.UserIdAsync(memberEmail)));
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
    }

    /// <summary>Design 8.25.2: the page holds nothing the member did not publish; the check proves each value exists first.</summary>
    [Fact]
    public async Task The_public_page_holds_nothing_the_member_did_not_publish()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email, id) = await Factory.PublicMemberAsync("Ona Privatumaitė", "Apie mane tekstas.");
        var (other, _) = await Factory.MemberAsync("Kitas Rašytojas");
        using var _ = member;
        using var _o = other;

        // What the member did that is not published: a side on another bill, "Man svarbu" on someone else's argument there, a
        // follow of a third bill, "Sutinku" on someone else's comment, a party, an invitation code.
        (await member.TakeSideAsync(AppealSteps.SecondBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        const string Endorsed = "Kito nario argumentas, kurį narė pažymėjo „Man svarbu“.";
        using (var written = await other.WriteReasonAsync(AppealSteps.SecondBill, "against", Endorsed))
        {
            written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        }

        var endorsedId = (await member.RecordAsync(AppealSteps.SecondBill)).Reasons.Single(r => r.Text == Endorsed).Id;
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(endorsedId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.RecordAsync(AppealSteps.SecondBill)).Reasons.Single(r => r.Id == endorsedId).Endorsements.ShouldBe(1, "the mark exists");
        const string Agreed = "Kito nario komentaras, su kuriuo narė sutiko.";
        var comment = await other.CommentOkAsync("reason", endorsedId, Agreed);
        (await member.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await member.FollowBillAsync(AppealSteps.ThirdBill);
        var party = (await member.MyProfileAsync()).PartyChoices[0];
        await member.SetPartyAsync(party);
        (await member.MyProfileAsync()).Party.ShouldBe(party);
        var code = (await member.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation)).Code;
        code.ShouldNotBeNullOrWhiteSpace();

        // What is published: an argument "for" on the tax bill.
        (await member.WriteReasonAsync(AppealSteps.TaxBill, "for", AppealSteps.ForReason)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var visitor = NewBrowser();
        using var answer = await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken);
        using (var json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            json.RootElement.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["about", "address", "displayName", "followers", "party", "profileId"]);
            json.RootElement.GetProperty("party").ValueKind.ShouldBe(JsonValueKind.Null, "the party only with its own box (design 8.25.9)");
        }

        var texts = await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(id, 10));
        texts.Items.ShouldHaveSingleItem().Bill.Number.ShouldBe(AppealSteps.TaxBill);
        texts.Items[0].Side.ShouldBe("for");

        var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync(ProfileRoutes.Page(id, "Ona Privatumaitė"), TestContext.Current.CancellationToken));
        page.ShouldContain(AppealSteps.ForReason[..40], Case.Sensitive, "the swept page holds what it is about");
        foreach (var never in new[] { email, party, code, AppealSteps.SecondBill, AppealSteps.ThirdBill, "Man svarbu", Endorsed, Agreed, "Sutinku", "Sutinka" })
        {
            page.ShouldNotContain(never, Case.Insensitive, never);
        }

        (await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(id, 10))).Items.ShouldHaveSingleItem("no other text of anyone's");
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
    }

    /// <summary>Design 8.25.2: only published arguments on bills whose record is not hidden; 10, then 50 more by the address (owner, 2026-11-19).</summary>
    [Fact]
    public async Task The_page_lists_published_arguments_newest_first_ten_then_more_and_never_waiting_hidden_or_withdrawn_ones()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var (member, _, id) = await Factory.PublicMemberAsync();
        using var _ = member;
        for (var i = 1; i <= 11; i++)
        {
            (await member.WriteReasonAsync(AppealSteps.TaxBill, i % 2 == 0 ? "for" : "against", $"Argumentas numeris {i} apie mokesčius ir jų poveikį.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var visitor = NewBrowser();
        var first = await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(id, 10));
        first.Items.Count.ShouldBe(10);
        first.More.ShouldBeTrue();
        first.Items[0].Text.ShouldContain("numeris 11");
        var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync(ProfileRoutes.Page(id, "Rūta Mockutė"), TestContext.Current.CancellationToken));
        page.ShouldContain($"href=\"/nariai/ruta-mockute-{id}?rodyti=60\"");
        (await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(id, 60))).Items.Count.ShouldBe(11);

        // A waiting (flagged) argument and a withdrawn one are not listed.
        (await member.WriteReasonAsync(AppealSteps.TaxBill, "for", "Rašykite man: laukiantis@pavyzdys.test")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var withdrawn = first.Items[0].Id;
        (await member.SendAsync(HttpMethod.Delete, AppealRoutes.ReasonOf(withdrawn))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(id, 20));
        after.Items.Count.ShouldBe(10);
        after.Items.ShouldNotContain(a => a.Id == withdrawn);
        after.Items.ShouldNotContain(a => a.Text.Contains("laukiantis", StringComparison.Ordinal));

        // Hiding the bill's record hides its arguments here too.
        var moderator = (await ModeratorAsync()).Browser;
        var recordId = await moderator.RecordIdAsync(AppealSteps.TaxBill);
        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, recordId), new StatementRequest { Statement = "Bandymas: paslėpta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(id, 20))).Items.ShouldBeEmpty();
        await Database.FreeBillAsync(AppealSteps.TaxBill);
    }

    /// <summary>Design 8.25.4: beside an argument, a public profile's id and one card per person; nothing for anyone else.</summary>
    [Fact]
    public async Task The_name_beside_an_argument_leads_to_the_page_only_for_a_member_with_a_public_profile()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var (open, _, id) = await Factory.PublicMemberAsync("Atvira Narė", "Trumpai apie mane.");
        var (closed, _) = await Factory.MemberAsync("Uždara Narė");
        using var _o = open;
        using var _c = closed;
        (await open.WriteReasonAsync(AppealSteps.TaxBill, "for", AppealSteps.ForReason)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await open.WriteReasonAsync(AppealSteps.TaxBill, "against", AppealSteps.AgainstReason)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await closed.WriteReasonAsync(AppealSteps.TaxBill, "for", "Uždaros narės argumentas apie mokesčius.")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var visitor = NewBrowser();
        var record = await visitor.RecordAsync(AppealSteps.TaxBill);
        record.Reasons.Where(r => r.AuthorName == "Atvira Narė").ShouldAllBe(r => r.AuthorProfileId == id);
        record.Reasons.Single(r => r.AuthorName == "Uždara Narė").AuthorProfileId.ShouldBeNull();
        record.Cards.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new AuthorCardResponse(id, "Atvira Narė", "Trumpai apie mane."));

        var page = await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.TaxBill, TestContext.Current.CancellationToken);
        page.Split($"<template id=\"card-{id}\"").Length.ShouldBe(2, "the card is in the page once per person");
        page.ShouldContain($"data-card=\"card-{id}\"");

        // Design 8.25.4 (owner, 2026-11-19): an initials badge beside the name that leads to the page, hidden from assistive
        // technology, in the colour its public id gives; none beside a plain name.
        var badge = $"<span class=\"initials initials--{Lt2.Web.Client.Components.MemberBadges.Colour(id)}\" aria-hidden=\"true\">AN</span>";
        page.Split(badge).Length.ShouldBe(3, "one badge beside each of the member's two arguments");
        page.ShouldNotContain(">UN</span>", Case.Sensitive, "no badge beside a member without a public profile");
        page.Split("class=\"initials ").Length.ShouldBe(3, "no other badge on the page");

        // Switched off: the link and the card go at once.
        await open.SavedProfileAsync("Trumpai apie mane.", isPublic: false);
        var after = await visitor.RecordAsync(AppealSteps.TaxBill);
        after.Reasons.ShouldAllBe(r => r.AuthorProfileId == null);
        after.Cards.ShouldNotBeNull().ShouldBeEmpty();
        await Database.FreeBillAsync(AppealSteps.TaxBill);
    }

    /// <summary>Design 8.25.5, 8.11.2: following a member, the shown number, and that nobody learns who follows.</summary>
    [Fact]
    public async Task A_member_is_followed_the_number_rises_and_falls_nobody_learns_who_and_switching_off_ends_every_follow()
    {
        var (followed, followedEmail, id) = await Factory.PublicMemberAsync("Sekama Narė");
        var (follower, followerAddress) = await Factory.MemberAsync("Sekanti Narė");
        var (other, _) = await Factory.MemberAsync("Kita Narė");
        using var _f = followed;
        using var _r = follower;
        using var _o = other;

        using (var own = await followed.SendAsync(HttpMethod.Put, ProfileRoutes.FollowOf(id)))
        {
            await own.ShouldBeProblemAsync(HttpStatusCode.NotFound, "member.not-found");
        }

        using (var notPublic = await follower.SendAsync(HttpMethod.Put, ProfileRoutes.FollowOf(ProfileSteps.UnknownProfileId())))
        {
            await notPublic.ShouldBeProblemAsync(HttpStatusCode.NotFound, "member.not-found");
        }

        await follower.FollowMemberAsync(id);
        await follower.FollowMemberAsync(id);
        await other.FollowMemberAsync(id);
        using var visitor = NewBrowser();
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).Followers.ShouldBe(2);
        (await follower.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Members.ShouldNotBeNull().ShouldHaveSingleItem()
            .ShouldBe(new FollowedMemberResponse(id, "Sekama Narė"));
        var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync(ProfileRoutes.Page(id, "Sekama Narė"), TestContext.Current.CancellationToken));
        page.ShouldContain("Sekėjai: 2");

        // The followed member's own answers and export name no follower.
        var export = await followed.Client.GetStringAsync(ApiRoutes.Me.Export, TestContext.Current.CancellationToken);
        export.ShouldContain(id, Case.Sensitive, "the export holds the profile");
        export.ShouldNotContain("Sekanti Narė");
        export.ShouldNotContain((await Database.UserIdAsync(followerAddress)).ToString());

        (await other.SendAsync(HttpMethod.Delete, ProfileRoutes.FollowOf(id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).Followers.ShouldBe(1);

        await followed.SavedProfileAsync("Mokytoja iš Kauno.", isPublic: false);
        (await follower.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Members.ShouldNotBeNull().ShouldBeEmpty();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.follow WHERE kind = 'member' AND key = @k", ("k", id))).ShouldBe(0);
        var again = (await followed.SavedProfileAsync("Mokytoja iš Kauno.", isPublic: true)).ProfileId!;
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(again))).Followers.ShouldBe(0, "a new profile starts with none");
        followedEmail.ShouldNotBeNull();
    }

    /// <summary>Design 8.25.5: a line by email for each argument the followed member publishes, never its text.</summary>
    [Fact]
    public async Task A_follower_gets_one_email_for_a_published_argument_without_its_text_and_none_once_it_is_hidden()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ForgetEventsAsync(AppealSteps.SecondBill);
        var (author, _, id) = await Factory.PublicMemberAsync("Autorė Pavyzdytė");
        var (follower, followerEmail) = await Factory.MemberAsync("Skaitytoja Pavyzdytė");
        using var _a = author;
        using var _f = follower;
        await follower.FollowMemberAsync(id);
        await Database.AgeFollowsAsync(followerEmail, TimeSpan.FromHours(1));

        const string Text = "Slaptas argumento tekstas, kurio laiške neturi būti.";
        (await author.WriteReasonAsync(AppealSteps.SecondBill, "against", Text)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await RunJobAsync();
        await RunJobAsync();
        (await Database.EmailsAsync(MemberEmail, followerEmail)).ShouldBe(1);
        using (var payload = JsonDocument.Parse((await Database.LatestEmailAsync(MemberEmail, followerEmail))!))
        {
            var root = payload.RootElement;
            root.EnumerateObject().Select(p => p.Name).Order()
                .ShouldBe(["accountPath", "billNumber", "billTitle", "day", "memberName", "profilePath", "stopToken", "textPath", "to", "what"]);
            root.GetProperty("memberName").GetString().ShouldBe("Autorė Pavyzdytė");
            root.GetProperty("profilePath").GetString().ShouldBe("/nariai/autore-pavyzdyte-" + id);
            root.GetProperty("accountPath").GetString().ShouldBe(AccountScreens.EmailRhythm);
            root.GetRawText().ShouldNotContain("Slaptas");
        }

        // An argument hidden before the job sends it is never sent.
        var moderator = (await ModeratorAsync()).Browser;
        using var second = await author.WriteReasonAsync(AppealSteps.SecondBill, "for", "Antras argumentas, kurį paslėps moderatorius.");
        var secondId = (await second.Content.ReadFromJsonAsync<ReasonCreatedResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!.Id;
        (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, secondId), new StatementRequest { Statement = "Bandymas: paslėpta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await RunJobAsync();
        (await Database.EmailsAsync(MemberEmail, followerEmail)).ShouldBe(1);
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ForgetEventsAsync(AppealSteps.SecondBill);
    }

    /// <summary>Design 8.25.6: a report, the moderators' hide with a statement emailed, restore, "Palikti"; staff only.</summary>
    [Fact]
    public async Task A_reported_about_me_is_hidden_by_a_moderator_shown_nowhere_until_replaced_and_restorable()
    {
        var (member, email, id) = await Factory.PublicMemberAsync("Pranešta Narė", "Esu Seimo narys.");
        var (reporter, _) = await Factory.MemberAsync("Pranešėja");
        using var _m = member;
        using var _r = reporter;
        using (var reported = await reporter.PostAsync(ProfileRoutes.ReportOf(id), new MemberReportRequest { Ground = "impersonation", Text = "Ne Seimo narys." }))
        {
            reported.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reported.Content.ReadAsStringAsync());
        }

        (await member.Client.GetAsync(ProfileRoutes.Moderation, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var moderator = (await ModeratorAsync()).Browser;
        var item = (await moderator.GetJsonAsync<List<ProfileModerationResponse>>(ProfileRoutes.Moderation)).Single(p => p.ProfileId == id);
        item.Reports.ShouldHaveSingleItem().Ground.ShouldBe("impersonation");
        item.About.ShouldBe("Esu Seimo narys.");

        (await moderator.PostAsync(ProfileRoutes.ActionOf(item.Id, "hide"), new StatementRequest { Statement = "Apsimetinėjimas Seimo nariu." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var visitor = NewBrowser();
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).About.ShouldBeNull();
        var mine = await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine);
        (mine.AboutHidden, mine.AboutHiddenStatement).ShouldBe((true, "Apsimetinėjimas Seimo nariu."));
        (await Database.EmailsAsync("email.profile-notice", email)).ShouldBe(1);
        (await moderator.GetJsonAsync<List<ProfileModerationResponse>>(ProfileRoutes.Moderation)).Single(p => p.Id == item.Id).Reports.ShouldBeEmpty("the hide answers the report");

        (await moderator.PostAsync(ProfileRoutes.ActionOf(item.Id, "restore"), new StatementRequest { Statement = "Pasitvirtino, kad tai tiesa." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).About.ShouldBe("Esu Seimo narys.");

        // Hidden again, then replaced by the person: the new text shows at once and cannot be "restored".
        (await moderator.PostAsync(ProfileRoutes.ActionOf(item.Id, "hide"), new StatementRequest { Statement = "Vėl paslėpta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await member.SavedProfileAsync("Mokytoja.", isPublic: true);
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).About.ShouldBe("Mokytoja.");
        using var wrong = await moderator.PostAsync(ProfileRoutes.ActionOf(item.Id, "restore"), new StatementRequest { Statement = "Bandymas." });
        await wrong.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");

        (await reporter.PostAsync(ProfileRoutes.ReportOf(id), new MemberReportRequest { Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.PostAsync(ProfileRoutes.ActionOf(item.Id, "keep"), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.GetJsonAsync<List<ProfileModerationResponse>>(ProfileRoutes.Moderation)).ShouldNotContain(p => p.Id == item.Id && p.Reports.Count > 0);
    }

    /// <summary>Design 8.5.4: the MP's own account takes no part, so it cannot switch a public profile on.</summary>
    [Fact]
    public async Task An_mps_own_account_cannot_switch_a_public_profile_on()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        try
        {
            using var refused = await mp.Browser.SaveProfileAsync(null, isPublic: true);
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
            (await mp.Browser.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).ProfileId.ShouldBeNull();
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    /// <summary>Design 8.25.6, 6.5: the export holds the profile and the consent; deleting the account retires the address.</summary>
    [Fact]
    public async Task The_export_holds_the_profile_and_deleting_the_account_retires_its_address_and_its_follows()
    {
        var (member, _, id) = await Factory.PublicMemberAsync("Išeinanti Narė", "Tekstas eksportui.");
        var (follower, _) = await Factory.MemberAsync("Sekėja");
        using var _f = follower;
        await follower.FollowMemberAsync(id);

        using (var json = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export, TestContext.Current.CancellationToken)))
        {
            var profile = json.RootElement.GetProperty("profile");
            profile.GetProperty("about").GetString().ShouldBe("Tekstas eksportui.");
            profile.GetProperty("publicProfileId").GetString().ShouldBe(id);
            json.RootElement.GetProperty("consents").EnumerateArray().ShouldContain(c => c.GetProperty("kind").GetString() == PublicProfileConsent.Kind);
        }

        await member.DeleteAccountAsync();
        member.Dispose();
        using var visitor = NewBrowser();
        (await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await follower.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Members.ShouldNotBeNull().ShouldBeEmpty();
    }

    /// <summary>Design 8.11.2: how many follow a bill, an MP and an organisation, a number on each answer and page.</summary>
    [Fact]
    public async Task Bills_mps_and_organisations_show_how_many_follow_them_and_nothing_with_none()
    {
        var bill = AppealSteps.ThirdBill;
        await Database.UnfollowBillAsync(bill);
        using var visitor = NewBrowser();
        (await visitor.GetJsonAsync<FollowersResponse>(FollowRoutes.BillFollowersOf(bill))).Followers.ShouldBe(0);
        (await visitor.Client.GetStringAsync("/seimas/" + bill, TestContext.Current.CancellationToken)).ShouldNotContain("id=\"followers\"");

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await member.FollowBillAsync(bill);
        (await visitor.GetJsonAsync<FollowersResponse>(FollowRoutes.BillFollowersOf(bill))).Followers.ShouldBe(1);
        WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + bill, TestContext.Current.CancellationToken)).ShouldContain("Sekėjai: 1");

        var slug = (await Database.SlugAsync(MpSteps.Kadagyte))!;
        var before = (await visitor.GetJsonAsync<Lt2.Api.Contracts.Parliament.MpProfileResponse>(ParliamentRoutes.Mp.Replace("{slug}", slug, StringComparison.Ordinal))).Followers;
        await member.FollowMpAsync(slug);
        using var fresh = NewBrowser();
        (await member.GetJsonAsync<Lt2.Api.Contracts.Parliament.MpProfileResponse>(ParliamentRoutes.Mp.Replace("{slug}", slug, StringComparison.Ordinal) + "?n=" + Guid.NewGuid().ToString("N"))).Followers.ShouldBe(before + 1);

        var (rep, _) = await Factory.MemberAsync("Atstovė");
        using var _rep = rep;
        var org = await rep.RegisterOrganisationAsync();
        (await visitor.GetJsonAsync<Lt2.Api.Contracts.Organisations.OrganisationProfileResponse>(Lt2.Api.Contracts.Organisations.OrganisationRoutes.Of(org.Slug))).Followers.ShouldBe(0);
        (await member.SendAsync(HttpMethod.Put, FollowRoutes.OrganisationOf(org.Slug))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fresh.GetJsonAsync<Lt2.Api.Contracts.Organisations.OrganisationProfileResponse>(Lt2.Api.Contracts.Organisations.OrganisationRoutes.Of(org.Slug) + "?n=1")).Followers.ShouldBe(1);
        (await member.SendAsync(HttpMethod.Delete, FollowRoutes.MpOf(slug))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Delete, FollowRoutes.BillOf(bill))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
