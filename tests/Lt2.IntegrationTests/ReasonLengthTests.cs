using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.2 (owner, 2026-11-16): an argument's length is the runtime setting <c>Texts:MaxLength</c> (2 000 by
/// default, 0 = no limit), checked where the argument is written for every caller; the shared validator checks only the
/// technical ceiling; a lower limit later shortens and removes nothing; the column holds any length.
/// </summary>
public sealed class ReasonLengthTests(DatabaseFixture database) : ApiTest(database)
{
    private static string Text(int length) => string.Concat(Enumerable.Repeat("Ilgas argumentas apie mokesčius. ", (length / 33) + 1))[..length].TrimEnd() + "x";

    [Fact]
    public async Task The_length_setting_decides_how_long_an_argument_may_be_and_a_lower_one_later_shortens_nothing()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
            var (writer, _) = await Factory.MemberAsync();
            using var _w = writer;

            // The default: 2 000 characters, longer than the old 500, and the rules answer says so.
            (await writer.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).TextMaxLength.ShouldBe(2000);
            var long1500 = Text(1500);
            using (var written = await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", long1500))
            {
                written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
            }

            using (var over = await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", Text(2001)))
            {
                await over.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "reason.too-long");
            }

            // A lower limit: refused above it, with the number that holds in the message; nothing written is shortened.
            await admin.SetSettingAsync("Texts:MaxLength", 1000);
            (await writer.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).TextMaxLength.ShouldBe(1000);
            using (var over = await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", Text(1001)))
            {
                over.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
                using var problem = JsonDocument.Parse(await over.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("code").GetString().ShouldBe("reason.too-long");
                problem.RootElement.GetProperty("detail").GetString().ShouldBe("Argumentas per ilgas: ne daugiau kaip 1 000 ženklų.");
            }

            // The similar-argument check is refused by the same rule, so no caller gets around it.
            using (var similar = await writer.PostAsync(AppealRoutes.SimilarReasonsOf(AppealSteps.ThirdBill), new ReasonRequest { Side = "for", Text = Text(1001) }))
            {
                await similar.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "reason.too-long");
            }

            (await writer.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldContain(r => r.Text == long1500, "a lower limit shortens nothing already written");

            // 0: no limit but the technical ceiling.
            await admin.SetSettingAsync("Texts:MaxLength", 0);
            var long5000 = Text(5000);
            (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", long5000)).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await writer.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldContain(r => r.Text == long5000);
            using (var ceiling = await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", new string('a', MemberTextLimits.Ceiling + 1)))
            {
                await ceiling.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
            }

            // The similar-argument check with a long text answers, and offers the long argument it resembles.
            using (var similar = await writer.PostAsync(AppealRoutes.SimilarReasonsOf(AppealSteps.ThirdBill), new ReasonRequest { Side = "against", Text = long5000 + " Dar vienas sakinys." }))
            {
                similar.StatusCode.ShouldBe(HttpStatusCode.OK, await similar.Content.ReadAsStringAsync());
                (await similar.ReadAsync<List<ReasonResponse>>()).ShouldContain(r => r.Text == long5000);
            }

            (await Database.ScalarAsync<string>(
                "SELECT data_type FROM information_schema.columns WHERE table_schema = 'appeals' AND table_name = 'reason' AND column_name = 'text'"))
                .ShouldBe("text", "the column gives no limit; the setting does");
        }
        finally
        {
            await admin.ResetSettingAsync("Texts:MaxLength");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    /// <summary>
    /// Owner, 2026-11-16: the same setting covers an organisation's statement (it was 1 500 fixed); the refusal names the
    /// number; the bill's page shows a statement whole up to about 1 500 characters plus the margin, longer with
    /// "Rodyti daugiau"; the moderators' screens show it whole. (An MP's response: MpResponseTests.)
    /// </summary>
    [Fact]
    public async Task An_organisations_statement_follows_the_members_texts_setting_and_a_long_one_is_shown_with_Rodyti_daugiau()
    {
        var admin = (await AdministratorAsync()).Browser;
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var created = await member.RegisterOrganisationAsync();
        try
        {
            using (var over = await member.PutStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", Text(2001)))
            {
                await over.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "statement.too-long");
            }

            await admin.SetSettingAsync("Texts:MaxLength", 0);
            var long4000 = Text(4000);
            await member.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", long4000);

            using var visitor = NewBrowser();
            using var page = await visitor.Client.GetAsync("/seimas/" + AppealSteps.ThirdBill);
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            html.ShouldContain("Rodyti daugiau");
            html.ShouldContain("statement-more-");
            html.ShouldContain(long4000[..200], Case.Sensitive, "the beginning is on the page");
        }
        finally
        {
            await admin.ResetSettingAsync("Texts:MaxLength");
            await Database.ClearStatementsAsync(AppealSteps.ThirdBill);
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}
