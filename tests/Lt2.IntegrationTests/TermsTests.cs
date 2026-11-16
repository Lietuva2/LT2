using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.22.2: the terms of use. A registration is not completed without accepting them, checked on the server; the accepted
/// version is a consent of kind <c>terms</c>. An account older than the terms (or than their newest version) reads, exports,
/// deletes and signs out, and every other write is refused with <c>terms.required</c> until it accepts.
/// </summary>
public sealed partial class TermsTests(DatabaseFixture database) : ApiTest(database)
{
    [GeneratedRegex(@"\{(?<name>\w+)(?<constraint>:[^}]+)?\}")]
    private static partial Regex RouteParameter();

    /// <summary>A member made like any other, then left without the terms, as every account from before them is.</summary>
    private async Task<(TestBrowser Browser, string Email)> OlderMemberAsync()
    {
        var (member, email) = await Factory.MemberAsync();
        await Database.ExecuteAsync(
            "DELETE FROM identity.consent WHERE kind = 'terms' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));
        return (member, email);
    }

    private Task<long> TermsConsentsAsync(string email) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = 'terms'",
            ("e", email));

    [Fact]
    public async Task A_registration_without_the_terms_box_is_refused_by_the_server_and_stays_pending()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync(complete: false);

        using (var refused = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = "Rūta M.", ConfirmsAdult = true }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
            (await refused.ProblemAsync()).GetProperty("errors").GetProperty("acceptsTerms")[0].GetString()
                .ShouldBe("Kad galėtumėte užsiregistruoti, sutikite su naudojimo taisyklėmis.");
        }

        (await Database.ScalarAsync<DateTime?>("SELECT registered_at FROM identity.profile WHERE normalized_email = @e", ("e", email.ToLowerInvariant()))).ShouldBeNull();
        (await TermsConsentsAsync(email)).ShouldBe(0);
        (await browser.MeAsync()).RegistrationComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task The_module_itself_refuses_a_completion_without_the_terms_for_callers_that_skip_the_validator()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync(complete: false);
        var userId = await Database.UserIdAsync(email);

        using var scope = Factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityModule>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
        var result = await identity.CompleteRegistrationAsync(principal, new RegistrationCompletion("Rūta M.", true, null, AcceptsTerms: false), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("terms.not-accepted");
        result.Error.Field.ShouldBe("acceptsTerms");
        (await TermsConsentsAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task Completing_a_registration_stores_the_accepted_version_as_a_consent_of_kind_terms()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync();

        (await Database.ScalarAsync<string>(
            "SELECT c.text_version FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = 'terms' AND c.withdrawn_at IS NULL",
            ("e", email))).ShouldBe(TermsOfUse.Version);
        (await TermsConsentsAsync(email)).ShouldBe(1);
        (await browser.MeAsync()).TermsRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task A_pending_account_is_never_told_to_accept_the_terms_apart_from_its_registration()
    {
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync(complete: false);

        (await browser.MeAsync()).TermsRequired.ShouldBeFalse();
        using var accept = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Terms, new TermsAcceptanceRequest { TextVersion = TermsText.Version });
        accept.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "only a member accepts on its own; a pending account does it when completing");
    }

    [Fact]
    public async Task An_older_account_reads_and_exports_but_cannot_write_until_it_accepts_and_then_can()
    {
        var (member, email) = await OlderMemberAsync();
        using var _m = member;

        // GET /me says so, for the pages and islands to show the screen.
        (await member.MeAsync()).TermsRequired.ShouldBeTrue();

        // Reading works: its own answers, public answers, the export.
        (await member.Client.GetAsync(ApiRoutes.Me.Sessions)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.Client.GetAsync(AppealRoutes.MineOf(AppealSteps.ThirdBill))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.Client.GetAsync(ParliamentRoutes.Bills)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var export = await member.Client.GetAsync(ApiRoutes.Me.Export))
        {
            export.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await export.Content.ReadAsStringAsync()).ShouldContain(email);
        }

        // Writing is refused, whatever it is.
        using (var consent = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion }))
        {
            await consent.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);
            (await consent.ProblemAsync()).GetProperty("detail").GetString()
                .ShouldBe("Naudojimo taisyklės pasikeitė. Kol su jomis nesutiksite, galite tik skaityti, atsisiųsti savo duomenis, ištrinti paskyrą ir atsijungti.");
        }

        using (var side = await member.TakeSideAsync(AppealSteps.ThirdBill, "for"))
        {
            await side.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);
        }

        // Accepting another version than the one in force is refused; the one in force is accepted once.
        using (var outdated = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.Terms, new TermsAcceptanceRequest { TextVersion = "2020-01-01" }))
        {
            await outdated.ShouldBeProblemAsync(HttpStatusCode.Conflict, "terms.outdated");
        }

        (await TermsConsentsAsync(email)).ShouldBe(0);
        using (var accepted = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.Terms, new TermsAcceptanceRequest { TextVersion = TermsText.Version }))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await accepted.Content.ReadAsStringAsync());
        }

        using (var again = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.Terms, new TermsAcceptanceRequest { TextVersion = TermsText.Version }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await TermsConsentsAsync(email)).ShouldBe(1, "accepting again adds nothing");
        (await member.MeAsync()).TermsRequired.ShouldBeFalse();
        using (var consent = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion }))
        {
            consent.StatusCode.ShouldBe(HttpStatusCode.NoContent, await consent.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task An_account_that_accepted_an_earlier_version_is_asked_again()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        await Database.ExecuteAsync(
            "UPDATE identity.consent SET text_version = '2026-01-01' WHERE kind = 'terms' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));

        (await member.MeAsync()).TermsRequired.ShouldBeTrue();
        using var refused = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion });
        await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TermsOfUse.RequiredCode);
    }

    [Fact]
    public async Task An_older_account_signs_out_and_deletes_itself_without_accepting()
    {
        var (leaving, _) = await OlderMemberAsync();
        using var _l = leaving;
        using (var signOut = await leaving.PostAsync(ApiRoutes.Auth.SignOut))
        {
            signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await leaving.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (deleting, email) = await OlderMemberAsync();
        using var _d = deleting;
        using (var deleted = await deleting.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.\"user\" WHERE email = @e", ("e", email))).ShouldBe(0);
    }

    [Fact]
    public async Task Every_write_route_of_the_api_refuses_an_older_account_unless_it_is_on_the_exempt_list()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Route: "/" + e.RoutePattern.RawText!.TrimStart('/'))))
            .Where(r => TermsExemptions.IsWrite(r.Method))
            .Distinct()
            .ToList();

        // Every exempt route is a route that exists: a typo in the list would leave the real route refused.
        foreach (var (method, route) in TermsExemptions.Routes)
        {
            endpoints.ShouldContain(e => e.Method == method && e.Route == route, $"{method} {route} is mapped");
        }

        var covered = endpoints.Where(e => !TermsExemptions.IsExempt(e.Method, e.Route)).ToList();
        covered.Count.ShouldBeGreaterThan(80, "the sweep holds the API's writes");

        var (member, _) = await OlderMemberAsync();
        using var _m = member;
        var notRefused = new List<string>();
        foreach (var (method, route) in covered)
        {
            var path = RouteParameter().Replace(route, m => m.Groups["constraint"].Value switch
            {
                ":guid" => Guid.CreateVersion7().ToString(),
                ":long" or ":int" => "1",
                _ => m.Groups["name"].Value switch
                {
                    "number" => AppealSteps.ThirdBill,
                    "token" => new string('a', 43),
                    _ => "nera",
                },
            });
            using var response = await member.SendAsync(new HttpMethod(method), path, "{}");
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains($"\"code\":\"{TermsOfUse.RequiredCode}\"", StringComparison.Ordinal))
            {
                notRefused.Add($"{method} {route}: {(int)response.StatusCode} {body[..Math.Min(body.Length, 120)]}");
            }
        }

        notRefused.ShouldBeEmpty();
        (await member.MeAsync()).TermsRequired.ShouldBeTrue("nothing the sweep sent accepted the terms");
    }

    [Fact]
    public async Task Invented_members_accept_the_terms_like_their_other_consents_and_running_again_gives_nothing_twice()
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var members = Enumerable.Range(0, 3).Select(_ => new InventedMember(Guid.NewGuid(), $"sutartis.{Guid.NewGuid():N}@pavyzdys.test", null, since)).ToList();
        try
        {
            (await InventedMembers.CreateAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(3);
            foreach (var m in members)
            {
                (await TermsConsentsAsync(m.Email)).ShouldBe(1, "a new invented member is made with the terms");
            }

            // Members made before the terms (an older local database): removing their terms, then running the command again.
            await Database.ExecuteAsync("DELETE FROM identity.consent WHERE kind = 'terms' AND user_id = ANY(@u)", ("u", members.Select(m => m.Id).ToArray()));
            (await InventedMembers.CreateAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(0);
            (await InventedMembers.AcceptTermsAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(3);
            (await InventedMembers.AcceptTermsAsync(Database.MigratorConnectionString, members, CancellationToken.None)).ShouldBe(0, "repeatable");
            foreach (var m in members)
            {
                (await TermsConsentsAsync(m.Email)).ShouldBe(1);
                (await Database.ScalarAsync<string>(
                    "SELECT text_version FROM identity.consent WHERE kind = 'terms' AND user_id = @u", ("u", m.Id))).ShouldBe(TermsOfUse.Version);
            }
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM identity.\"user\" WHERE id = ANY(@u)", ("u", members.Select(m => m.Id).ToArray()));
        }
    }
}
