using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 5.2, 6.3, 8.4: party support is private and optional, and a group under 20 never shows a number anywhere,
/// including by subtraction from the side's total shown beside it.
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

    [Fact]
    public async Task A_group_under_20_shows_no_number_anywhere_not_even_by_subtraction()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using (var set = await admin.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Digest:ThresholdMinimum"), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(1) }))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var appeal = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);

            // Invented members: "for" 19 of the party and 30 others; "against" (with the initiator) 25 of the party and 3 others.
            var since = DateTimeOffset.UtcNow.AddDays(-90);
            var members = Enumerable.Range(0, 77).Select(i => new InventedMember(Guid.NewGuid(), $"grupe.{Guid.NewGuid():N}@pavyzdys.test", null, since)).ToList();
            await InventedMembers.CreateAsync(Database.MigratorConnectionString, members, CancellationToken.None);
            async Task AddAsync(IEnumerable<InventedMember> who, string side, bool party)
            {
                foreach (var m in who)
                {
                    await Database.ExecuteAsync(
                        "INSERT INTO appeals.position (appeal_id, user_id, side, created_at, changed_at, member_since) VALUES (@a, @u, @s, now(), now(), @m)",
                        ("a", appeal.Id), ("u", m.Id), ("s", side), ("m", since));
                    if (party)
                    {
                        await Database.ExecuteAsync("INSERT INTO identity.party_support (user_id, party, given_at) VALUES (@u, @p, now())", ("u", m.Id), ("p", Party));
                    }
                }
            }

            await AddAsync(members.Take(19), "for", party: true);
            await AddAsync(members.Skip(19).Take(30), "for", party: false);
            await AddAsync(members.Skip(49).Take(25), "against", party: true);
            await AddAsync(members.Skip(74).Take(2), "against", party: false);

            await using var scope = Factory.Services.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IDigestSource>();
            (await source.PartyNumbersAsync([appeal.Id], CancellationToken.None))[appeal.Id].ShouldBeEmpty(
                "19 for is under 20; 25 against would leave 3 others (2 + the initiator) to be read by subtraction");

            await AddAsync(members.Skip(76).Take(1), "for", party: true);
            var numbers = (await source.PartyNumbersAsync([appeal.Id], CancellationToken.None))[appeal.Id].Single();
            numbers.ShouldBe(new PartyNumbers(Party, 20, null));

            // An urgent summary (dry run): the MP of that party would read the 20; nobody else, and no public copy, sees a party.
            using var urgent = await moderator.Browser.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, appeal.Id), new UrgentDigestRequest { Statement = "Bandymas." });
            urgent.StatusCode.ShouldBe(HttpStatusCode.Created, await urgent.Content.ReadAsStringAsync());
            var run = await urgent.ReadAsync<DigestRunResponse>();
            var preview = await moderator.Browser.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(run.Id, "jonas-petraitis"));
            preview.Text.ShouldContain("Iš nurodžiusių, kad palaiko Reformų partija: 20 prašo balsuoti už.");
            preview.Text.ShouldNotContain("prašo balsuoti prieš.\n", Case.Sensitive);
            preview.Text.ShouldNotContain(": 25 ");

            using var stranger = NewBrowser();
            var publicJson = await stranger.Client.GetStringAsync(DigestRoutes.DigestOn(run.RunDate));
            publicJson.ShouldNotContain(Party);
            var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + run.RunDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            page.ShouldNotContain("palaiko");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE party_numbers::text LIKE '%25%'")).ShouldBe(0);
        }
        finally
        {
            using var restore = await admin.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Digest:ThresholdMinimum"), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(50) });
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}
