using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 5.2, 6.3, 8.4: party support is private and optional, and a group under <c>Digest:PartyGroupMinimum</c> (owner,
/// 2026-11-17; 0 = no floor) never shows a number anywhere, including by subtraction from the side's total shown beside it.
/// </summary>
public sealed class PartySupportTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Party = "Reformų partija";

    [Fact]
    public async Task A_member_sets_and_removes_their_party_with_consent_and_sees_only_their_own()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var initial = await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport);
        initial.Party.ShouldBeNull();
        initial.Choices.ShouldContain(Party);

        using (var noConsent = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = Party }))
        {
            await noConsent.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var unknown = await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = "Išgalvota partija", Consent = true }))
        {
            await unknown.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "party.unknown");
        }

        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = Party, Consent = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).Party.ShouldBe(Party);
        var userId = await Database.UserIdAsync(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.consent WHERE user_id = @u AND kind = 'party-support' AND withdrawn_at IS NULL", ("u", userId))).ShouldBe(1);

        var (other, _) = await Factory.MemberAsync();
        using var _o = other;
        (await other.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).Party.ShouldBeNull("only one's own");

        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = null })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.party_support WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.consent WHERE user_id = @u AND kind = 'party-support' AND withdrawn_at IS NULL", ("u", userId))).ShouldBe(0);
    }

    /// <summary>Design 6.8: the consent is checked where the party is stored, so a caller inside the server cannot skip it.</summary>
    [Fact]
    public async Task Without_consent_no_party_is_stored_through_the_in_process_client_either()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var userId = await Database.UserIdAsync(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "member")], "test")),
            RequestServices = scope.ServiceProvider,
        };

        var result = await scope.ServiceProvider.GetRequiredService<IMeApi>().SetPartySupportAsync(new PartySupportRequest { Party = Party, Consent = false });

        result.IsSuccess.ShouldBeFalse();
        result.Problem!.Code.ShouldBe("party.consent-required");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.party_support WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.consent WHERE user_id = @u AND kind = 'party-support'", ("u", userId))).ShouldBe(0);
    }

    /// <summary>
    /// Design 6.3, 8.4 (owner, 2026-11-17): at the setting's default, a group one under <c>Digest:PartyGroupMinimum</c> shows
    /// no number, a group of exactly the minimum does, and a group whose side's rest is under the minimum shows none either.
    /// </summary>
    [Fact]
    public async Task A_group_under_the_minimum_shows_no_number_anywhere_not_even_by_subtraction()
    {
        var moderator = await ModeratorAsync();
        var admin = await AdministratorAsync();
        await admin.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);
        await admin.Browser.ResetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name);
        var minimum = SettingKeys.DigestPartyGroupMinimum.Default;
        minimum.ShouldBeGreaterThan(3, "the steps below need a minimum above the rest of the against side (the initiator and two others)");

        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            // "for": one under the minimum of the party and more than the minimum of others; "against" (with the initiator):
            // more than the minimum of the party and three others, a rest that subtraction would reveal; then one more "for".
            var forParty = minimum - 1;
            var forOthers = minimum + 5;
            var againstParty = minimum + 5;
            var group = await InventedGroupAsync(record.Id, forParty + forOthers + againstParty + 2 + 1);
            await group.AddAsync(0, forParty, "for", party: true);
            await group.AddAsync(forParty, forOthers, "for", party: false);
            await group.AddAsync(forParty + forOthers, againstParty, "against", party: true);
            await group.AddAsync(forParty + forOthers + againstParty, 2, "against", party: false);

            await using var scope = Factory.Services.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IDigestSource>();
            (await source.PartyNumbersAsync([record.Id], CancellationToken.None))[record.Id].ShouldBeEmpty(
                "one under the minimum for; the against group would leave 3 others (2 + the initiator) to be read by subtraction");

            await group.AddAsync(forParty + forOthers + againstParty + 2, 1, "for", party: true);
            var numbers = (await source.PartyNumbersAsync([record.Id], CancellationToken.None))[record.Id].Single();
            numbers.ShouldBe(new PartyNumbers(Party, minimum, null));

            // An urgent summary (dry run): the MP of that party would read the minimum; nobody else, and no public copy, sees a party.
            var run = await UrgentAsync(moderator, record.Id);
            // (The email's line "Iš nurodžiusių, kad palaiko …" is the template's, MpMailTemplateTests.)
            var petraitis = (await PetraitisNumbersAsync(run.Id)).Replace(" ", "", StringComparison.Ordinal);
            petraitis.ShouldContain($"\"party\":\"{Party.Replace(" ", "", StringComparison.Ordinal)}\"");
            petraitis.ShouldContain($"\"for\":{minimum},");
            petraitis.ShouldContain("\"against\":null");

            using var stranger = NewBrowser();
            var publicJson = await stranger.Client.GetStringAsync(DigestRoutes.DigestOn(run.RunDate));
            publicJson.ShouldNotContain(Party);
            var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + run.RunDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            page.ShouldNotContain("palaiko");
            var stored = (await Database.ScalarAsync<string>(
                "SELECT string_agg(party_numbers::text, ' ') FROM notify.digest_delivery WHERE run_id = @r", ("r", run.Id)))
                .ShouldNotBeNull("the dry run stored a delivery per MP").Replace(" ", "", StringComparison.Ordinal);
            stored.ShouldContain($"\"for\":{minimum},", customMessage: "the stored snapshot holds what the email showed");
            stored.ShouldContain("\"against\":null");
            stored.ShouldNotContain($":{againstParty}");
        }
        finally
        {
            await admin.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    /// <summary>
    /// Design 8.4 (owner, 2026-11-17): with <c>Digest:PartyGroupMinimum</c> at 0 there is no floor, so a group of one is
    /// shown in the summary; raised again, the same group is not. An empty group is never shown.
    /// </summary>
    [Fact]
    public async Task With_the_minimum_at_0_a_group_of_one_is_shown_and_raising_it_hides_that_group()
    {
        var moderator = await ModeratorAsync();
        var admin = await AdministratorAsync();
        await admin.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);
        await admin.Browser.SetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name, 0);

        try
        {
            using (var reader = NewBrowser())
            {
                WebUtility.HtmlDecode(await reader.Client.GetStringAsync("/metodika")).ShouldContain("rodomi, kad ir kiek žmonių grupėje");
            }

            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            var group = await InventedGroupAsync(record.Id, 4);
            await group.AddAsync(0, 1, "for", party: true);
            await group.AddAsync(1, 3, "for", party: false);

            await using var scope = Factory.Services.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IDigestSource>();
            (await source.PartyNumbersAsync([record.Id], CancellationToken.None))[record.Id].Single()
                .ShouldBe(new PartyNumbers(Party, 1, null), "no party member against: an empty group is no number");

            var run = await UrgentAsync(moderator, record.Id);
            var petraitis = (await PetraitisNumbersAsync(run.Id)).Replace(" ", "", StringComparison.Ordinal);
            petraitis.ShouldContain("\"for\":1,");
            petraitis.ShouldContain("\"against\":null", customMessage: "an empty group is no number");

            await admin.Browser.SetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name, 2);
            (await source.PartyNumbersAsync([record.Id], CancellationToken.None))[record.Id].ShouldBeEmpty("the setting is read when the numbers are made");
            using var reader2 = NewBrowser();
            WebUtility.HtmlDecode(await reader2.Client.GetStringAsync("/metodika")).ShouldContain("rodomi tik kai grupėje žmonių yra bent 2");
        }
        finally
        {
            await admin.Browser.ResetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name);
            await admin.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    /// <summary>
    /// Design 8.4: the party form states the minimum as it holds now (the number, or in words without a floor), from
    /// <c>GET /me/party-support</c>.
    /// </summary>
    [Fact]
    public async Task The_party_form_is_told_the_minimum_as_it_holds_now()
    {
        var admin = await AdministratorAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await admin.Browser.ResetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name);
        (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).GroupMinimum.ShouldBe(SettingKeys.DigestPartyGroupMinimum.Default);

        try
        {
            await admin.Browser.SetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name, 0);
            (await member.GetJsonAsync<PartySupportResponse>(ApiRoutes.Me.PartySupport)).GroupMinimum.ShouldBe(0);
        }
        finally
        {
            await admin.Browser.ResetSettingAsync(SettingKeys.DigestPartyGroupMinimum.Name);
        }
    }

    /// <summary>
    /// The settings screen refuses a negative minimum (design 5.6: validated like every setting) and keeps the value.
    /// </summary>
    [Fact]
    public async Task A_negative_minimum_is_refused()
    {
        var admin = await AdministratorAsync();
        using var refused = await admin.Browser.SendAsync(
            HttpMethod.Put, ApiRoutes.Admin.SettingByKey(SettingKeys.DigestPartyGroupMinimum.Name), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(-1) });
        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "setting.invalid");
    }

    /// <summary>The party numbers stored with Jonas Petraitis's (Reformų partija) delivery of a run: what his email showed.</summary>
    private async Task<string> PetraitisNumbersAsync(Guid runId) =>
        (await Database.ScalarAsync<string>(
            "SELECT party_numbers::text FROM notify.digest_delivery WHERE run_id = @r AND mp_slug = 'jonas-petraitis' AND recipient = 'official'", ("r", runId)))
        .ShouldNotBeNull("the run stored Jonas Petraitis's delivery");

    private static async Task<DigestRunResponse> UrgentAsync(StaffMember moderator, Guid recordId)
    {
        using var urgent = await moderator.Browser.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, recordId), new UrgentDigestRequest { Statement = "Bandymas." });
        urgent.StatusCode.ShouldBe(HttpStatusCode.Created, await urgent.Content.ReadAsStringAsync());
        return await urgent.ReadAsync<DigestRunResponse>();
    }

    /// <summary>Invented members who joined 90 days ago, whose sides on the record are written straight into the tables.</summary>
    private async Task<InventedGroup> InventedGroupAsync(Guid recordId, int count)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var members = Enumerable.Range(0, count).Select(_ => new InventedMember(Guid.NewGuid(), $"grupe.{Guid.NewGuid():N}@pavyzdys.test", null, since)).ToList();
        await InventedMembers.CreateAsync(Database.MigratorConnectionString, members, CancellationToken.None);
        return new InventedGroup(Database, recordId, members, since);
    }

    private sealed record InventedGroup(DatabaseFixture Database, Guid RecordId, IReadOnlyList<InventedMember> Members, DateTimeOffset Since)
    {
        public async Task AddAsync(int skip, int take, string side, bool party)
        {
            (skip + take).ShouldBeLessThanOrEqualTo(Members.Count, "the group has too few invented members for this step");
            foreach (var m in Members.Skip(skip).Take(take))
            {
                await Database.ExecuteAsync(
                    "INSERT INTO appeals.position (record_id, user_id, side, created_at, changed_at, member_since) VALUES (@a, @u, @s, now(), now(), @m)",
                    ("a", RecordId), ("u", m.Id), ("s", side), ("m", Since));
                if (party)
                {
                    await Database.ExecuteAsync("INSERT INTO identity.party_support (user_id, party, given_at) VALUES (@u, @p, now())", ("u", m.Id), ("p", Party));
                }
            }
        }
    }
}
