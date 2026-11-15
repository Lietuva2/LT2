using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Feedback;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.10: feedback from members to the team. Linked to the account only when the member allows a reply; staff
/// reply through the outbox without seeing the address; an unlinked item has no path back to its author.
/// </summary>
public sealed partial class FeedbackTests(DatabaseFixture database) : ApiTest(database)
{
    private static Task<HttpResponseMessage> SendAsync(TestBrowser browser, string text, bool mayReply, string? kind = null) =>
        browser.PostAsync(FeedbackRoutes.Send, new FeedbackRequest { Text = text, Kind = kind, PagePath = "/seimas/XVP-0412?x=1", Browser = "Edge 131", MayReply = mayReply });

    [Fact]
    public async Task A_linked_item_shows_the_name_to_staff_and_the_reply_goes_to_the_members_address_which_staff_never_see()
    {
        var moderator = await ModeratorAsync();
        var (member, email) = await Factory.MemberAsync("Atsiliepianti Narė");
        using var _m = member;
        const string Text = "Susietas atsiliepimas: neaišku, kur rasti metodiką.";
        (await SendAsync(member, Text, mayReply: true, "unclear")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var list = await moderator.Browser.Client.GetAsync(FeedbackRoutes.Staff + "?kind=unclear&status=new");
        var json = await list.Content.ReadAsStringAsync();
        var item = JsonSerializer.Deserialize<List<FeedbackItemResponse>>(json, JsonSerializerOptions.Web)!.Single(i => i.Text == Text);
        (item.Linked, item.DisplayName, item.PagePath, item.Browser).ShouldBe((true, "Atsiliepianti Narė", "/seimas/XVP-0412", "Edge 131"));
        json.ShouldNotContain(email, Case.Insensitive);
        json.ShouldNotContain((await Database.UserIdAsync(email)).ToString(), Case.Insensitive);

        (await moderator.Browser.SendAsync(HttpMethod.Put, FeedbackRoutes.ItemOf(item.Id), new FeedbackUpdateRequest { Status = "done", Note = "https://github.com/Lietuva2/LT2/issues/1" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await moderator.Browser.PostAsync(FeedbackRoutes.ReplyOf(item.Id), new FeedbackReplyRequest { Text = "Metodika – meniu „Kaip skaičiuojama“." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var payload = await Database.ScalarAsync<string>("SELECT payload::text FROM notify.outbox WHERE kind = 'email.feedback-reply' AND payload->>'to' = @to", ("to", email));
        payload.ShouldNotBeNull();
        payload.ShouldContain("Kaip skaičiuojama");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'feedback.replied' AND target = @t", ("t", "feedback:" + item.Id))).ShouldBe(1);
        var after = (await moderator.Browser.GetJsonAsync<List<FeedbackItemResponse>>(FeedbackRoutes.Staff)).Single(i => i.Id == item.Id);
        (after.Status, after.Note, after.Replies.Count).ShouldBe(("done", "https://github.com/Lietuva2/LT2/issues/1", 1));

        // In the export, and gone with the account.
        using (var export = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export)))
        {
            export.RootElement.GetProperty("feedback").EnumerateArray().Select(f => f.GetProperty("text").GetString()).ShouldContain(Text);
        }
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.feedback WHERE text = @t", ("t", Text))).ShouldBe(0);
    }

    /// <summary>Design 6.9: feedback handling is audited: the item, the old and new status, who; never a text.</summary>
    [Fact]
    public async Task A_change_of_status_or_note_is_audited_with_who_and_both_statuses_and_no_text()
    {
        var moderator = await ModeratorAsync();
        var (member, _) = await Factory.MemberAsync("Audituojama Narė");
        using var _m = member;
        const string Text = "Atsiliepimas, kurio tekstas neturi patekti į auditą.";
        const string Note = "Vidinė pastaba, kurios irgi neturi būti audite.";
        (await SendAsync(member, Text, mayReply: false, "idea")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var item = (await moderator.Browser.GetJsonAsync<List<FeedbackItemResponse>>(FeedbackRoutes.Staff + "?kind=idea&status=new")).Single(i => i.Text == Text);

        (await moderator.Browser.SendAsync(HttpMethod.Put, FeedbackRoutes.ItemOf(item.Id), new FeedbackUpdateRequest { Status = "seen", Note = Note }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var moderatorId = await Database.UserIdAsync(moderator.Email);
        var details = await Database.ScalarAsync<string>(
            "SELECT details::text FROM audit.event WHERE action = 'feedback.updated' AND target = @t AND actor_user_id = @u AND actor_role = 'moderator'",
            ("t", "feedback:" + item.Id), ("u", moderatorId));
        details.ShouldNotBeNull();
        using (var parsed = JsonDocument.Parse(details))
        {
            parsed.RootElement.GetProperty("from").GetString().ShouldBe("new");
            parsed.RootElement.GetProperty("to").GetString().ShouldBe("seen");
            parsed.RootElement.GetProperty("noteChanged").GetBoolean().ShouldBeTrue();
        }

        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE target = @t AND (coalesce(details::text, '') LIKE '%' || @a || '%' OR coalesce(details::text, '') LIKE '%' || @b || '%')",
            ("t", "feedback:" + item.Id), ("a", "tekstas neturi"), ("b", "Vidinė pastaba"))).ShouldBe(0, "no text of the item or the note");
    }

    [Fact]
    public async Task An_unlinked_item_has_no_path_back_to_its_author_in_any_table_or_answer_and_no_reply()
    {
        var moderator = await ModeratorAsync();
        var (member, email) = await Factory.MemberAsync("Nesusieta Narė");
        using var _m = member;
        const string Text = "Nesusietas atsiliepimas apie spalvas.";
        var userId = (await Database.UserIdAsync(email)).ToString();
        (await SendAsync(member, Text, mayReply: false, "idea")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The row: no account, a random (version 4) id, only the day.
        var id = await Database.ScalarAsync<Guid>("SELECT id FROM identity.feedback WHERE text = @t", ("t", Text));
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.feedback WHERE text = @t AND user_id IS NULL", ("t", Text))).ShouldBe(1);
        id.Version.ShouldBe(4, "a random id: not even its id tells when it came");
        (await Database.ScalarAsync<string>(
            "SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.columns WHERE table_schema = 'identity' AND table_name = 'feedback' AND data_type LIKE 'timestamp%'"))
            .ShouldBeNull("no time of day anywhere on the item");

        // No other table holds the item's id or text next to the account: audit, outbox, anything.
        foreach (var (table, column) in new[] { ("audit.event", "target"), ("notify.outbox", "payload::text"), ("audit.event", "details::text") })
        {
            (await Database.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {column} LIKE @i OR {column} LIKE @t", ("i", "%" + id + "%"), ("t", "%spalvas%"))).ShouldBe(0, table);
        }

        // The staff answer: no author, no reply.
        using var list = await moderator.Browser.Client.GetAsync(FeedbackRoutes.Staff);
        var json = await list.Content.ReadAsStringAsync();
        var item = JsonSerializer.Deserialize<List<FeedbackItemResponse>>(json, JsonSerializerOptions.Web)!.Single(i => i.Id == id);
        (item.Linked, item.DisplayName).ShouldBe((false, (string?)null));
        json.ShouldNotContain("Nesusieta Narė");
        json.ShouldNotContain(userId, Case.Insensitive);
        using (var reply = await moderator.Browser.PostAsync(FeedbackRoutes.ReplyOf(id), new FeedbackReplyRequest { Text = "Ačiū." }))
        {
            await reply.ShouldBeProblemAsync(HttpStatusCode.Conflict, "feedback.not-linked");
        }

        // Not in the member's export; it stays when the account goes (it was never theirs on record).
        using (var export = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export)))
        {
            export.RootElement.GetProperty("feedback").GetArrayLength().ShouldBe(0, "an unlinked item is nobody's on record");
        }
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.feedback WHERE id = @i", ("i", id))).ShouldBe(1);
    }

    [Fact]
    public async Task Sending_needs_a_member_staff_pages_need_a_staff_passkey_ten_a_day_and_a_writing_restriction_does_not_stop_it()
    {
        var moderator = await ModeratorAsync();
        using var stranger = NewBrowser();
        (await SendAsync(stranger, "Be paskyros.", false)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.Client.GetAsync(FeedbackRoutes.Staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var codeSession = NewBrowser())
        {
            await codeSession.PostAsync(ApiRoutes.Auth.Code, new Lt2.Api.Contracts.Auth.RequestCodeRequest { Email = moderator.Email });
            await codeSession.SignInWithCodeAsync(moderator.Email);
            (await codeSession.Client.GetAsync(FeedbackRoutes.Staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var empty = await member.PostAsync(FeedbackRoutes.Send, new FeedbackRequest { Text = " " }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Restricted from writing (design 8.3): feedback still goes.
        await Database.ExecuteAsync(
            "INSERT INTO appeals.writing_restriction (id, user_id, statement, set_by_user_id, set_at) VALUES (gen_random_uuid(), @u, 'Bandymas.', @u, now())",
            ("u", await Database.UserIdAsync(email)));
        for (var i = 1; i <= 10; i++)
        {
            (await SendAsync(member, $"Atsiliepimas nr. {i}.", mayReply: i % 2 == 0)).StatusCode.ShouldBe(HttpStatusCode.NoContent, $"item {i}");
        }

        using var eleventh = await SendAsync(member, "Vienuoliktas.", mayReply: false);
        await eleventh.ShouldBeProblemAsync(HttpStatusCode.Conflict, "feedback.limit");
    }

    [Fact]
    public async Task The_form_works_without_javascript_on_any_page_and_a_visitor_gets_the_contact_address_instead()
    {
        using var stranger = NewBrowser();
        var visitorPage = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/atsiliepimai"));
        visitorPage.ShouldContain("Nepavyksta prisijungti? Parašykite mums:");
        visitorPage.ShouldContain("mailto:moderatoriai@lt2.test");
        visitorPage.ShouldNotContain("feedback-form-");
        WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/prisijungti")).ShouldContain("id=\"cannot-sign-in\"");

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var html = await member.Client.GetStringAsync("/metodika");
        var form = FormRegex().Match(html);
        form.Success.ShouldBeTrue("the balloon's form is on an ordinary page");
        var fields = InputRegex().Matches(form.Value)
            .Select(m => (Name: Regex.Match(m.Value, "name=\"([^\"]+)\"").Groups[1].Value, Value: Regex.Match(m.Value, "value=\"([^\"]*)\"").Groups[1].Value))
            .Where(f => f.Name.Length > 0)
            .DistinctBy(f => f.Name)
            .ToDictionary(f => WebUtility.HtmlDecode(f.Name), f => WebUtility.HtmlDecode(f.Value));
        fields.ShouldContainKey("Model.Page");
        fields["Model.Page"].ShouldBe("/metodika");
        fields.Remove("Model.IncludeBrowser"); // unticked: the browser is not sent
        fields.Remove("Model.MayReply");
        fields["Model.Text"] = "Atsiliepimas be JavaScript.";
        using var posted = await member.Client.PostAsync("/metodika", new FormUrlEncodedContent(fields));
        var answer = WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync());
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, answer[..Math.Min(500, answer.Length)]);
        answer.ShouldContain("Ačiū! Atsiliepimą gavome.");
        (await Database.ScalarAsync<string>("SELECT coalesce(page_path, '-') || '|' || coalesce(browser, '-') || '|' || coalesce(user_id::text, '-') FROM identity.feedback WHERE text = 'Atsiliepimas be JavaScript.'"))
            .ShouldBe("/metodika|-|-");
    }

    [Fact]
    public async Task One_notice_a_day_to_the_team_with_the_count_never_the_text_and_old_items_go_in_the_clean_up()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await using var scope = Factory.Services.CreateAsyncScope();
        var feedback = scope.ServiceProvider.GetRequiredService<IFeedback>();
        await feedback.SendNoticeAsync("komanda@lt2.test", CancellationToken.None); // whatever earlier tests left
        (await feedback.SendNoticeAsync("komanda@lt2.test", CancellationToken.None)).ShouldBe(0, "nothing new: no email");

        (await SendAsync(member, "Pranešimo komandai turinys.", false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await feedback.SendNoticeAsync("komanda@lt2.test", CancellationToken.None)).ShouldBe(1);
        var payload = await Database.ScalarAsync<string>("SELECT payload::text FROM notify.outbox WHERE kind = 'email.feedback-notice' ORDER BY created_at DESC LIMIT 1");
        payload.ShouldNotBeNull();
        payload.ShouldContain("\"count\": 1");
        payload.ShouldContain("/moderavimas/atsiliepimai");
        payload.ShouldNotContain("turinys");
        (await feedback.SendNoticeAsync(null, CancellationToken.None)).ShouldBe(0, "no team address configured");

        await Database.ExecuteAsync("INSERT INTO identity.feedback (id, text, status, created_on, replies, noticed) VALUES (gen_random_uuid(), 'Senas atsiliepimas.', 'done', current_date - interval '13 months', '[]', true)");
        await scope.ServiceProvider.GetRequiredService<IIdentityMaintenance>().PurgeExpiredAsync(CancellationToken.None);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.feedback WHERE text = 'Senas atsiliepimas.'")).ShouldBe(0);
    }

    [GeneratedRegex("<form[^>]*id=\"feedback-form-panel\"[\\s\\S]*?</form>")]
    private static partial Regex FormRegex();

    [GeneratedRegex("<(?:input|textarea)[^>]*>")]
    private static partial Regex InputRegex();
}
