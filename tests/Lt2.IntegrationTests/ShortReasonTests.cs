using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Owner, 2026-11-17 (design 8.3): an argument with fewer words than <c>Appeals:MinReasonWords</c> (3) is flagged <c>too-short</c>
/// where it is written and waits for a moderator, listed in the queue; one long enough is published at once; the setting at 0
/// switches the check off. Comments are not checked. The test frees its bill and puts the setting back.
/// </summary>
public sealed class ShortReasonTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private static async Task<ReasonCreatedResponse> WriteAsync(TestBrowser author, string text)
    {
        using var written = await author.WriteReasonAsync(Bill, "for", text);
        written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        return await written.ReadAsync<ReasonCreatedResponse>();
    }

    [Fact]
    public async Task A_one_word_argument_waits_for_a_moderator_flagged_too_short_and_the_setting_at_0_switches_it_off()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var (author, _) = await Factory.MemberAsync("Trumpai Rašanti");
        using var _a = author;
        try
        {
            await Database.PrepareBillAsync(initiator, moderator, Bill, "against");
            (await author.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).MinReasonWords.ShouldBe(3, "the default");

            var short1 = await WriteAsync(author, "Pritariu!");
            short1.Status.ShouldBe("pending");
            var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.Single(r => r.Id == short1.Id);
            queued.Flags.ShouldBe(["too-short"]);
            (await author.RecordAsync(Bill)).Reasons.ShouldNotContain(r => r.Id == short1.Id, "not public while it waits");

            (await WriteAsync(author, "Mažesni mokesčiai šeimoms.")).Status.ShouldBe("published", "three words are enough");

            // A comment is never checked: "Sutinku" alone is normal there.
            var reasonId = (await author.RecordAsync(Bill)).Reasons[0].Id;
            await author.CommentOkAsync("reason", reasonId, "Sutinku.");

            await admin.SetSettingAsync("Appeals:MinReasonWords", 0);
            (await WriteAsync(author, "Pritariu.")).Status.ShouldBe("published", "0 switches the check off");
        }
        finally
        {
            await admin.ResetSettingAsync("Appeals:MinReasonWords");
            await Database.FreeBillAsync(Bill);
        }
    }
}
