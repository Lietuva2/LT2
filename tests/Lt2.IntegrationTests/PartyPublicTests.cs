using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.25.9 (owner, 2026-11-17): the supported party shown on the public profile only by its own box and consent
/// (<c>party-public</c>), only while the profile is public; unticking, removing or changing the party and switching the profile
/// off withdraw it at once, and switching on again does not bring it back; nothing for the MP's own account; the page only,
/// never beside a text or in another answer; open behind the terms to withdraw; exported; audited without the party.
/// </summary>
public sealed class PartyPublicTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Party = "Reformų partija";

    private Task<long> ShownConsentsAsync(string email) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = 'party-public' AND c.withdrawn_at IS NULL AND c.text_version = @v",
            ("e", email), ("v", PartyPublicConsent.TextVersion))!;

    private static async Task SetPartyAsync(TestBrowser member, string? party)
    {
        using var set = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = party, Consent = party is not null });
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
    }

    private static async Task ShowAsync(TestBrowser member)
    {
        using var shown = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new PartyPublicRequest { ConsentVersion = PartyPublicLimits.ConsentVersion });
        shown.StatusCode.ShouldBe(HttpStatusCode.NoContent, await shown.Content.ReadAsStringAsync());
    }

    /// <summary>The page's answer and its served HTML, as a visitor reads them.</summary>
    private async Task<(MemberProfileResponse Answer, string Html)> PageAsync(string id, string name)
    {
        using var visitor = NewBrowser();
        var answer = await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id));
        using var page = await visitor.Client.GetAsync(ProfileRoutes.Page(id, name), TestContext.Current.CancellationToken);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        html.ShouldContain($"<h1 id=\"member-name\">{name}</h1>", Case.Sensitive, "the page that is read is the member's");
        return (answer, html);
    }

    [Fact]
    public async Task The_party_is_shown_on_the_page_only_with_its_own_box_and_consent_and_nowhere_else()
    {
        const string name = "Partiją Rodanti Narė";
        var (member, email, id) = await Factory.PublicMemberAsync(name);
        using var _m = member;
        await SetPartyAsync(member, Party);

        var mine = await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport);
        (mine.Party, mine.Public, mine.ProfilePublic, mine.ConsentVersion).ShouldBe((Party, false, true, PartyPublicConsent.TextVersion));
        var (before, beforeHtml) = await PageAsync(id, name);
        before.Party.ShouldBeNull("\"Viešas profilis\" alone never shows the party");
        beforeHtml.ShouldNotContain(Party);
        beforeHtml.ShouldNotContain("id=\"member-party\"", Case.Sensitive);

        using (var outdated = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new PartyPublicRequest { ConsentVersion = "2020-01-01" }))
        {
            await outdated.ShouldBeProblemAsync(HttpStatusCode.Conflict, "party.consent-outdated");
        }

        (await ShownConsentsAsync(email)).ShouldBe(0);
        await ShowAsync(member);
        await ShowAsync(member);
        (await ShownConsentsAsync(email)).ShouldBe(1, "one consent, with its version; giving it again changes nothing");
        (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).Public.ShouldBeTrue();

        var (after, afterHtml) = await PageAsync(id, name);
        after.ShouldBe(before with { Party = Party });
        afterHtml.ShouldContain($"<p id=\"member-party\">Palaikoma partija: {Party}</p>", Case.Sensitive);

        // The page only (design 8.25.9): not in the texts' answer, not beside an argument or in its card on the bill's page.
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        try
        {
            using (var written = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Argumentas narės, rodančios partiją profilyje."))
            {
                written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
            }

            using var visitor = NewBrowser();
            var record = await visitor.Client.GetStringAsync(AppealRoutes.RecordOf(AppealSteps.ThirdBill), TestContext.Current.CancellationToken);
            record.ShouldContain(id, Case.Sensitive, "the card of the profile is in the answer swept");
            record.ShouldNotContain(Party);
            var bill = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill, TestContext.Current.CancellationToken));
            bill.ShouldContain("Argumentas narės, rodančios partiją profilyje.");
            bill.ShouldNotContain(Party);
            var texts = await visitor.Client.GetStringAsync(ProfileRoutes.TextsOf(id, 10), TestContext.Current.CancellationToken);
            texts.ShouldContain("Argumentas narės");
            texts.ShouldNotContain(Party);
        }
        finally
        {
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }

        // Audited, never with the party.
        var userId = await Database.UserIdAsync(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'party.public-on' AND target = @t", ("t", userId.ToString()))).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target = @t AND coalesce(details::text, '') LIKE '%partij%'", ("t", userId.ToString()))).ShouldBe(0);

        // The export says it.
        using var export = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export, TestContext.Current.CancellationToken));
        var party = export.RootElement.GetProperty("partySupport");
        party.GetProperty("party").GetString().ShouldBe(Party);
        party.GetProperty("shownOnPublicProfile").GetBoolean().ShouldBeTrue();
        export.RootElement.GetProperty("consents").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ShouldContain(PartyPublicConsent.Kind);
    }

    [Fact]
    public async Task Unticking_changing_or_removing_the_party_and_switching_the_profile_off_each_stop_showing_it_at_once()
    {
        const string name = "Partiją Slepianti Narė";
        var (member, email, id) = await Factory.PublicMemberAsync(name);
        using var _m = member;
        var other = (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).Choices.First(c => c != Party);
        await SetPartyAsync(member, Party);
        var userId = (await Database.UserIdAsync(email)).ToString();

        async Task GoneAsync(string why, string reason)
        {
            (await ShownConsentsAsync(email)).ShouldBe(0, why);
            (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).Public.ShouldBeFalse(why);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'party.public-off' AND target = @t AND details->>'reason' = @r", ("t", userId), ("r", reason))).ShouldBeGreaterThan(0, why);
        }

        // The box unticked.
        await ShowAsync(member);
        (await PageAsync(id, name)).Answer.Party.ShouldBe(Party);
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PartyPublic)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (unticked, untickedHtml) = await PageAsync(id, name);
        unticked.Party.ShouldBeNull();
        untickedHtml.ShouldNotContain(Party);
        await GoneAsync("unticked", "box");

        // The party changed: the consent covered the party it was given for.
        await ShowAsync(member);
        await SetPartyAsync(member, other);
        (await PageAsync(id, name)).Answer.Party.ShouldBeNull("a changed party is not shown until the box is ticked again");
        await GoneAsync("changed", "party");

        // The party removed.
        await ShowAsync(member);
        (await PageAsync(id, name)).Answer.Party.ShouldBe(other);
        await SetPartyAsync(member, null);
        (await PageAsync(id, name)).Answer.Party.ShouldBeNull();
        (await ShownConsentsAsync(email)).ShouldBe(0, "removed");

        // The profile switched off, and on again: the party stays hidden until ticked again.
        await SetPartyAsync(member, Party);
        await ShowAsync(member);
        (await member.SavedProfileAsync("Mokytoja iš Kauno.", isPublic: false)).ProfileId.ShouldBeNull();
        await GoneAsync("profile off", "profile");
        var again = (await member.SavedProfileAsync("Mokytoja iš Kauno.", isPublic: true)).ProfileId.ShouldNotBeNull();
        var (back, backHtml) = await PageAsync(again, name);
        back.Party.ShouldBeNull("switching the profile on again does not bring it back by itself");
        backHtml.ShouldNotContain(Party);
        (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).Party.ShouldBe(Party, "the party itself stays, private");
    }

    [Fact]
    public async Task It_is_refused_without_a_party_without_a_public_profile_and_for_an_mps_own_account()
    {
        var (member, email, _) = await Factory.PublicMemberAsync("Be Partijos Narė");
        using var _m = member;
        using (var none = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new PartyPublicRequest { ConsentVersion = PartyPublicLimits.ConsentVersion }))
        {
            await none.ShouldBeProblemAsync(HttpStatusCode.Conflict, "party.none");
        }

        var (closed, closedEmail) = await Factory.MemberAsync("Uždaro Profilio Narė");
        using var _c = closed;
        await SetPartyAsync(closed, Party);
        (await closed.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).ProfilePublic.ShouldBeFalse();
        using (var off = await closed.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new PartyPublicRequest { ConsentVersion = PartyPublicLimits.ConsentVersion }))
        {
            await off.ShouldBeProblemAsync(HttpStatusCode.Conflict, "party.profile-off");
        }

        (await ShownConsentsAsync(email)).ShouldBe(0);
        (await ShownConsentsAsync(closedEmail)).ShouldBe(0);

        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        try
        {
            using var refused = await mp.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new PartyPublicRequest { ConsentVersion = PartyPublicLimits.ConsentVersion });
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    /// <summary>Design 8.22.2 (owner, 2026-11-17): withdrawing works before accepting changed terms; giving it again does not.</summary>
    [Fact]
    public async Task Behind_the_terms_the_party_can_be_hidden_and_not_shown_again()
    {
        const string name = "Taisyklių Nepriėmusi Narė";
        var (member, email, id) = await Factory.PublicMemberAsync(name);
        using var _m = member;
        await SetPartyAsync(member, Party);
        await ShowAsync(member);
        await Database.ExecuteAsync("DELETE FROM identity.consent WHERE kind = 'terms' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));
        (await member.MeAsync()).TermsRequired.ShouldBeTrue();

        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PartyPublic)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ShownConsentsAsync(email)).ShouldBe(0);
        (await PageAsync(id, name)).Answer.Party.ShouldBeNull();

        using var again = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new PartyPublicRequest { ConsentVersion = PartyPublicLimits.ConsentVersion });
        await again.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);
        (await ShownConsentsAsync(email)).ShouldBe(0);
    }
}
