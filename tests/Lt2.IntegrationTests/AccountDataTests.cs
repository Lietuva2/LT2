using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 6.5: a person downloads everything they gave and deletes the account.</summary>
public sealed class AccountDataTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task The_export_holds_everything_the_person_gave_and_nothing_of_anyone_else()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var (initiator, initiatorEmail) = await Factory.MemberAsync("Rūta Mockutė");
        using var _i = initiator;
        await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var othersReason = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single();
        var (member, email) = await Factory.MemberAsync("Giedrė L.");
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Mano paties argumentas eksportui.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(othersReason.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = "Reformų partija", Consent = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await member.Client.GetAsync(ApiRoutes.Me.Export);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var json = await response.Content.ReadAsStringAsync();
        using var export = JsonDocument.Parse(json);
        var root = export.RootElement;
        root.GetProperty("account").GetProperty("email").GetString().ShouldBe(email);
        root.GetProperty("partySupport").GetProperty("party").GetString().ShouldBe("Reformų partija");
        root.GetProperty("consents").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ShouldBe(["age-18-or-older", "positions", "party-support"], ignoreOrder: true);
        var appeals = root.GetProperty("appeals");
        appeals.GetProperty("positions").EnumerateArray().Single().GetProperty("side").GetString().ShouldBe("against");
        appeals.GetProperty("reasons").EnumerateArray().Single().GetProperty("text").GetString().ShouldBe("Mano paties argumentas eksportui.");
        appeals.GetProperty("endorsements").EnumerateArray().Single().GetProperty("reason").GetGuid().ShouldBe(othersReason.Id);

        // Nothing of the initiator: not their address, name, side or words.
        json.ShouldNotContain(initiatorEmail);
        json.ShouldNotContain("Rūta Mockutė");
        json.ShouldNotContain(AppealSteps.AgainstReason);
        json.ShouldNotContain((await Database.UserIdAsync(initiatorEmail)).ToString());

        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(ApiRoutes.Me.Export)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deleting_the_account_removes_positions_marks_consents_and_counts_but_not_frozen_summaries_and_logs_nothing()
    {
        var sink = new AppealPrivacyTests.CollectingSink();
        await using var logged = new AppealPrivacyTests.LoggingFactory(Database, sink);
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using (var minimum = await admin.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Digest:ThresholdMinimum"), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(1) }))
        {
            minimum.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var appeal = await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            var reasonId = (await initiator.AppealAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
            var (member, email) = await logged.MemberAsync("Ištrinamas Narys");
            using var _m = member;
            var userId = await Database.UserIdAsync(email);
            (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Šis argumentas dings su paskyra.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await member.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, new PartySupportRequest { Party = "Reformų partija", Consent = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.AppealAsync(AppealSteps.ThirdBill)).Counts.Against.ShouldBe(2);

            using var urgent = await moderator.Browser.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, appeal.Id), new UrgentDigestRequest { Statement = "Prieš ištrynimą." });
            var frozen = await urgent.ReadAsync<DigestRunResponse>();
            frozen.Items.Single().Against.ShouldBe(2);

            using (var deleted = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root))
            {
                deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            foreach (var (table, column) in new[]
            {
                ("appeals.position", "user_id"), ("appeals.endorsement", "user_id"), ("appeals.reason", "author_user_id"), ("appeals.writing_restriction", "user_id"), ("identity.consent", "user_id"),
                ("identity.party_support", "user_id"), ("identity.profile", "user_id"), ("identity.session", "user_id"), ("identity.\"user\"", "id"),
            })
            {
                (await Database.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {column} = @u", ("u", userId))).ShouldBe(0, table);
            }

            using var stranger = NewBrowser();
            var after = await stranger.AppealAsync(AppealSteps.ThirdBill);
            after.Counts.Against.ShouldBe(1, "the count falls");
            after.Reasons.Single().Endorsements.ShouldBe(0);
            (await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(frozen.RunDate))).Single(r => r.Id == frozen.Id).Items.Single().Against
                .ShouldBe(2, "a frozen summary keeps its totals");
            (await member.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "signed out");

            foreach (var line in sink.Lines)
            {
                line.ShouldNotContain(email, Case.Insensitive);
                line.ShouldNotContain("Ištrinamas Narys");
            }
        }
        finally
        {
            using var restore = await admin.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Digest:ThresholdMinimum"), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(50) });
        }
    }
}