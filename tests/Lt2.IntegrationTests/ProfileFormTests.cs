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
/// Design 8.28.5: "Profilis" is one form saved by one <c>PUT /me/profile</c>, whole or not at all. Every part is checked before
/// anything is written; a refusal saves nothing and names every refused field; an unchanged part is not checked, written or
/// audited; what changes is written together. The separate writes are gone; the three withdrawals stay
/// (<see cref="WithdrawalTests"/> and <see cref="PartyPublicTests"/> use them behind the terms).
/// </summary>
public sealed class ProfileFormTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Party = "Reformų partija";
    private const string PersonalCode = "Asmens kodas 39001010000";

    private async Task<long> AuditsAsync(string email, string action) =>
        await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = @a AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)", ("a", action), ("e", email));

    private Task<long> ConsentsAsync(string email, string kind) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = @k AND c.withdrawn_at IS NULL",
            ("e", email), ("k", kind))!;

    /// <summary>What the form holds (the dates aside, read back from the database at its precision).</summary>
    private static (string? Name, string? About, string? Id, string? Address, string? Party, bool Shown) Of(MyProfileResponse s) =>
        (s.DisplayName, s.About, s.ProfileId, s.Address, s.Party, s.PartyPublic);

    /// <summary>The fields the answer names, in order.</summary>
    private static async Task<IReadOnlyList<string>> RefusedFieldsAsync(HttpResponseMessage response) =>
        (await response.ProblemAsync()).GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task A_valid_whole_form_is_saved_by_one_request_with_each_consent_and_audit()
    {
        var (member, email) = await Factory.MemberAsync("Pirmas Vardas");
        using var _m = member;
        var form = await member.FormAsync();

        using var saved = await member.SendAsync(HttpMethod.Put, ProfileRoutes.Mine, form with
        {
            DisplayName = "Rūta Mockutė",
            About = "Mokytoja iš Kauno.",
            Public = true,
            PublicConsentVersion = ProfileLimits.ConsentVersion,
            Party = Party,
            PartyConsent = true,
            PartyPublic = true,
            PartyPublicConsentVersion = PartyPublicLimits.ConsentVersion,
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var state = (await saved.Content.ReadFromJsonAsync<MyProfileResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!;
        (state.DisplayName, state.About, state.Party, state.PartyPublic).ShouldBe(("Rūta Mockutė", "Mokytoja iš Kauno.", Party, true));
        var id = state.ProfileId.ShouldNotBeNull();
        state.Address.ShouldBe($"/nariai/ruta-mockute-{id}");
        state.PartyChoices.ShouldContain(Party);
        Of(await member.MyProfileAsync()).ShouldBe(Of(state));

        foreach (var kind in new[] { PublicProfileConsent.Kind, "party-support", PartyPublicConsent.Kind })
        {
            (await ConsentsAsync(email, kind)).ShouldBe(1, kind);
        }

        foreach (var action in new[] { "profile.display-name-changed", "profile.about-changed", "profile.public-on", "party.public-on" })
        {
            (await AuditsAsync(email, action)).ShouldBe(1, action);
        }

        using var visitor = NewBrowser();
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).Party.ShouldBe(Party, "the profile switched on and the party shown in one save");

        // The same form again changes and audits nothing.
        Of(await member.SavedFormAsync(f => f)).ShouldBe(Of(state));
        (await AuditsAsync(email, "profile.display-name-changed")).ShouldBe(1);
        (await AuditsAsync(email, "party.public-on")).ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_form_of_a_new_member_saves_and_changes_nothing()
    {
        var (member, email) = await Factory.MemberAsync(displayName: null);
        using var _m = member;

        var state = await member.SavedFormAsync(f => f);

        (state.DisplayName, state.About, state.ProfileId, state.Party).ShouldBe(((string?)null, (string?)null, (string?)null, (string?)null));
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE target = (SELECT id::text FROM identity.\"user\" WHERE email = @e) AND action LIKE 'profile.%'", ("e", email)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task A_form_with_one_refused_field_saves_nothing_and_names_that_field()
    {
        var (member, email) = await Factory.MemberAsync("Senas Vardas");
        using var _m = member;

        using var refused = await member.SaveFormAsync(f => f with
        {
            DisplayName = "Naujas Vardas",
            About = PersonalCode,
            Party = Party,
            PartyConsent = true,
        });

        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "about.personal-code");
        (await RefusedFieldsAsync(refused)).ShouldBe(["about"]);
        var after = await member.MyProfileAsync();
        (after.DisplayName, after.About, after.Party).ShouldBe(("Senas Vardas", (string?)null, (string?)null), "nothing saved, the valid parts neither");
        (await AuditsAsync(email, "profile.display-name-changed")).ShouldBe(0);
        (await ConsentsAsync(email, "party-support")).ShouldBe(0);
    }

    [Fact]
    public async Task Every_refused_field_is_named_with_its_own_reason()
    {
        var (member, email) = await Factory.MemberAsync(displayName: null);
        using var _m = member;

        using var refused = await member.SaveFormAsync(f => f with
        {
            DisplayName = new string('a', 61),
            About = PersonalCode,
            Public = true,
            PublicConsentVersion = "2020-01-01",
            Party = "Išgalvota partija",
            PartyConsent = true,
            PartyPublic = true,
            PartyPublicConsentVersion = "2020-01-01",
        });

        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        (await RefusedFieldsAsync(refused)).ShouldBe(["about", "displayName", "party", "partyPublic", "public"]);
        var errors = (await refused.ProblemAsync()).GetProperty("errors");
        errors.GetProperty("displayName")[0].GetString().ShouldBe("Vardas per ilgas: ne daugiau kaip 60 ženklų.");
        errors.GetProperty("about")[0].GetString()!.ShouldContain("asmens kodas");
        errors.GetProperty("public")[0].GetString()!.ShouldContain("Sutikimo tekstas pasikeitė");
        errors.GetProperty("party")[0].GetString().ShouldBe("Tokios partijos sąraše nėra.");
        errors.GetProperty("partyPublic")[0].GetString()!.ShouldContain("Sutikimo tekstas pasikeitė");

        var after = await member.MyProfileAsync();
        (after.DisplayName, after.About, after.ProfileId, after.Party).ShouldBe(((string?)null, (string?)null, (string?)null, (string?)null));
        (await ConsentsAsync(email, PublicProfileConsent.Kind)).ShouldBe(0);

        // A name taken away while the profile is on, and a party chosen without its box: both named.
        var (open, _, _) = await Factory.PublicMemberAsync("Atviro Profilio Narė");
        using var _o = open;
        using var two = await open.SaveFormAsync(f => f with { DisplayName = "", Party = Party, PartyConsent = false });
        await two.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        (await RefusedFieldsAsync(two)).ShouldBe(["displayName", "partyConsent"]);
        (await open.MyProfileAsync()).DisplayName.ShouldBe("Atviro Profilio Narė");
    }

    [Fact]
    public async Task Under_a_writing_restriction_unchanged_parts_are_not_refused_and_a_changed_text_is_named()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, email) = await Factory.MemberAsync("Apribota Profilio Narė");
        using var _m = member;
        (await member.SavedProfileAsync("Senas tekstas.", isPublic: false)).About.ShouldBe("Senas tekstas.");
        try
        {
            using (var written = await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Argumentas, dėl kurio apribota."))
            {
                var reasonId = (await written.Content.ReadFromJsonAsync<ReasonCreatedResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!.Id;
                using var restricted = await (await ModeratorAsync()).Browser.PostAsync(
                    AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), new RestrictionRequest { Statement = "Bandymas: rašymas apribotas." });
                restricted.StatusCode.ShouldBe(HttpStatusCode.Created, await restricted.Content.ReadAsStringAsync());
            }

            // The name and "Apie mane" sent unchanged are no writing: the party is saved beside them.
            var saved = await member.SavedFormAsync(f => f with { Party = Party, PartyConsent = true });
            (saved.DisplayName, saved.About, saved.Party).ShouldBe(("Apribota Profilio Narė", "Senas tekstas.", Party));

            // A changed text alone is refused under its field, with the restriction's own status.
            using (var text = await member.SaveFormAsync(f => f with { About = "Naujas tekstas." }))
            {
                await text.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
                (await RefusedFieldsAsync(text)).ShouldBe(["about"]);
            }

            // A changed name and text: both named, nothing saved.
            using (var both = await member.SaveFormAsync(f => f with { DisplayName = "Kitas Vardas", About = "Naujas tekstas.", Party = null }))
            {
                await both.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
                (await RefusedFieldsAsync(both)).ShouldBe(["about", "displayName"]);
            }

            var after = await member.MyProfileAsync();
            (after.DisplayName, after.About, after.Party).ShouldBe(("Apribota Profilio Narė", "Senas tekstas.", Party), "the party's removal in the refused form was not saved either");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM appeals.writing_restriction WHERE user_id = @u", ("u", await Database.UserIdAsync(email)));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    [Fact]
    public async Task The_separate_writes_are_gone_and_the_withdrawals_stay()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;

        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.Root + "/display-name", new { displayName = "Kitas" })).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new { party = Party, consent = true })).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartyPublic, new { consentVersion = PartyPublicLimits.ConsentVersion })).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await member.Client.GetAsync(ApiRoutes.Me.PartySupport, TestContext.Current.CancellationToken)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await member.MyProfileAsync()).DisplayName.ShouldNotBe("Kitas");

        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PartySupport)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PartyPublic)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Delete, ProfileRoutes.MinePublic)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
