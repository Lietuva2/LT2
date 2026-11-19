using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2, 8.28.5: a member changes the display name with the "Profilis" form (<c>PUT /me/profile</c>), as often as they like (owner, 2026-11-16). The name
/// is read live, so it shows beside the person's arguments at once, older ones included; the registration's rule; refused
/// under a writing restriction (the designer's proposal, awaiting the owner); audited without the names.
/// </summary>
public sealed class DisplayNameTests(DatabaseFixture database) : ApiTest(database)
{
    private static Task<HttpResponseMessage> ChangeAsync(TestBrowser browser, string? name) =>
        browser.SaveFormAsync(f => f with { DisplayName = name });

    private async Task<long> AuditCountAsync(string email) =>
        await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'profile.display-name-changed' AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)",
            ("e", email));

    [Fact]
    public async Task A_changed_name_shows_beside_the_persons_earlier_argument_at_once()
    {
        var moderator = await ModeratorAsync();
        var (member, email) = await Factory.MemberAsync("Vardas Pirmasis");
        using var _m = member;
        try
        {
            await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.SecondBill, "for");
            using var stranger = NewBrowser();
            (await stranger.RecordAsync(AppealSteps.SecondBill)).Reasons.ShouldContain(r => r.AuthorName == "Vardas Pirmasis");

            using (var changed = await ChangeAsync(member, "  Vardas Antrasis "))
            {
                changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
            }

            var reasons = (await stranger.RecordAsync(AppealSteps.SecondBill)).Reasons;
            reasons.ShouldContain(r => r.AuthorName == "Vardas Antrasis", "the name is read live, older arguments included");
            reasons.ShouldNotContain(r => r.AuthorName == "Vardas Pirmasis");
            (await member.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).DisplayName.ShouldBe("Vardas Antrasis");

            // Audited that it changed, never from what to what.
            (await AuditCountAsync(email)).ShouldBe(1);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'profile.display-name-changed' AND (details::text LIKE '%Vardas%' OR target LIKE '%@%')"))
                .ShouldBe(0, "no names or addresses in the audit log");

            // Empty removes the name, as registration allows: the argument shows without one.
            (await ChangeAsync(member, "")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await stranger.RecordAsync(AppealSteps.SecondBill)).Reasons.ShouldContain(r => r.AuthorName == null);
        }
        finally
        {
            await Database.FreeBillAsync(AppealSteps.SecondBill);
        }
    }

    /// <summary>Owner, 2026-11-16: no limit on how often; the same name again changes and audits nothing.</summary>
    [Fact]
    public async Task A_name_may_change_as_often_as_the_member_likes_and_the_same_name_again_changes_nothing()
    {
        var (member, email) = await Factory.MemberAsync("Keičiamas Vardas");
        using var _m = member;

        (await ChangeAsync(member, "Keičiamas Vardas")).StatusCode.ShouldBe(HttpStatusCode.OK, "the same name again changes nothing");
        (await AuditCountAsync(email)).ShouldBe(0);
        foreach (var name in new[] { "Vardas 1", "Vardas 2", "Vardas 3", "Vardas 4", "Vardas 5" })
        {
            using var changed = await ChangeAsync(member, name);
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        }

        (await member.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).DisplayName.ShouldBe("Vardas 5");
        (await AuditCountAsync(email)).ShouldBe(5);
    }

    [Fact]
    public async Task A_name_longer_than_60_characters_is_refused_as_at_registration()
    {
        var (member, _) = await Factory.MemberAsync("Trumpas Vardas");
        using var _m = member;

        using var refused = await ChangeAsync(member, new string('a', 61));

        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "display-name.too-long");
        (await refused.ProblemAsync()).GetProperty("errors").GetProperty("displayName").GetArrayLength().ShouldBe(1);
        (await member.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).DisplayName.ShouldBe("Trumpas Vardas");
    }

    [Fact]
    public async Task A_restricted_account_cannot_change_its_name()
    {
        var moderator = await ModeratorAsync();
        var (member, email) = await Factory.MemberAsync("Apribotas Rašytojas");
        using var _m = member;
        try
        {
            await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.SecondBill, "against");
            var reasonId = (await moderator.Browser.RecordAsync(AppealSteps.SecondBill)).Reasons.Single(r => r.AuthorName == "Apribotas Rašytojas").Id;
            using (var restricted = await moderator.Browser.PostAsync(
                       AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId),
                       new RestrictionRequest { Statement = "Įžeidžiantys argumentai kitiems naudotojams." }))
            {
                restricted.StatusCode.ShouldBe(HttpStatusCode.Created, await restricted.Content.ReadAsStringAsync());
            }

            using (var refused = await ChangeAsync(member, "Kitas Vardas"))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
            }

            (await member.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).DisplayName.ShouldBe("Apribotas Rašytojas");
            (await AuditCountAsync(email)).ShouldBe(0);
        }
        finally
        {
            await Database.FreeBillAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task A_visitor_and_a_pending_account_are_refused()
    {
        using var visitor = NewBrowser();
        using (var anonymous = await ChangeAsync(visitor, "Niekieno Vardas"))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var pending = NewBrowser();
        await pending.RegisterAndSignInAsync(complete: false);
        using var refused = await ChangeAsync(pending, "Laukiantis Vardas");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "a pending account is not a member: it gives its name when completing the registration");
    }
}
