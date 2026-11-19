using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>
/// The owner, 2026-11-17: (a) withdrawing what one consented to works before accepting changed terms (switching the public
/// profile off, the positions consent, the party support consent, the party shown on the profile), and nothing else does, the
/// "Profilis" form included (design 8.28.5); (b) a restricted member may clear
/// "Apie mane" and switch the profile off, and still not write or change the text.
/// </summary>
public sealed class WithdrawalTests(DatabaseFixture database) : ApiTest(database)
{
    private Task<long> ConsentsAsync(string email, string kind) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = @k AND c.withdrawn_at IS NULL",
            ("e", email), ("k", kind))!;

    [Fact]
    public async Task Behind_the_terms_a_member_can_switch_the_profile_off_and_withdraw_consents_and_cannot_change_or_switch_on_through_them()
    {
        var (member, email, id) = await Factory.PublicMemberAsync("Atsiimanti Narė", "Apie mane.");
        using var _m = member;
        var party = (await member.MyProfileAsync()).PartyChoices[0];
        await member.SetPartyAsync(party);
        using (var shown = await member.ShowPartyAsync())
        {
            shown.StatusCode.ShouldBe(HttpStatusCode.OK, await shown.Content.ReadAsStringAsync());
        }

        (await ConsentsAsync(email, PartyPublicConsent.Kind)).ShouldBe(1, "the party is shown before");
        (await ConsentsAsync(email, "party-support")).ShouldBe(1, "the party consent exists before");
        (await ConsentsAsync(email, "positions")).ShouldBe(1, "the positions consent exists before");
        await Database.ExecuteAsync("DELETE FROM identity.consent WHERE kind = 'terms' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));
        (await member.MeAsync()).TermsRequired.ShouldBeTrue();

        // Changing "Apie mane" goes through the form's route, which stays refused; so does switching on, and so does a form
        // that only unticks a box (design 8.28.5: the withdrawals are requests of their own).
        await (await member.SaveProfileAsync("Kitas tekstas.", isPublic: true)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);
        await (await member.SaveFormAsync(f => f with { PartyPublic = false })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);

        // The party no longer shown: open behind the terms.
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PartyPublic)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConsentsAsync(email, PartyPublicConsent.Kind)).ShouldBe(0);
        (await member.MyProfileAsync()).PartyPublic.ShouldBeFalse();

        using (var off = await member.SendAsync(HttpMethod.Delete, ProfileRoutes.MinePublic))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
            var state = (await off.Content.ReadFromJsonAsync<MyProfileResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!;
            (state.ProfileId, state.About).ShouldBe(((string?)null, "Apie mane."), "off, and nothing else changed");
        }

        (await ConsentsAsync(email, PublicProfileConsent.Kind)).ShouldBe(0);
        (await NewBrowser().Client.GetAsync("/nariai/" + id)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await (await member.SaveProfileAsync("Apie mane.", isPublic: true)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);

        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConsentsAsync(email, "positions")).ShouldBe(0);
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PartySupport)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConsentsAsync(email, "party-support")).ShouldBe(0);
        (await member.MyProfileAsync()).Party.ShouldBeNull();

        // Giving a consent again stays refused: the form cannot be saved behind the terms.
        await (await member.SavePartyAsync(party)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);
    }

    [Fact]
    public async Task A_restricted_member_may_clear_about_me_and_switch_the_profile_off_and_not_write_or_change_it()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email, _) = await Factory.PublicMemberAsync("Apribota Narė", "Senas tekstas.");
        using var _m = member;
        try
        {
            using (var written = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Argumentas, dėl kurio apribota."))
            {
                var reasonId = (await written.Content.ReadFromJsonAsync<ReasonCreatedResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!.Id;
                using var restricted = await (await ModeratorAsync()).Browser.PostAsync(
                    AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Statement = "Bandymas: rašymas apribotas." });
                restricted.StatusCode.ShouldBe(HttpStatusCode.Created, await restricted.Content.ReadAsStringAsync());
            }

            await (await member.SaveProfileAsync("Naujas tekstas.", isPublic: true)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
            (await member.SavedProfileAsync(null, isPublic: true)).About.ShouldBeNull("clearing is erasure, not writing");
            (await member.SendAsync(HttpMethod.Delete, ProfileRoutes.MinePublic)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).ProfileId.ShouldBeNull();
            await (await member.SaveProfileAsync("Dar kitas.", isPublic: false)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM appeals.writing_restriction WHERE user_id = @u", ("u", await Database.UserIdAsync(email)));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}
