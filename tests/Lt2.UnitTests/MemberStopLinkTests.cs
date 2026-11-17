using System.Text.Json;
using Lt2.Api.Contracts.Follows;
using Lt2.Hosting;
using Lt2.Notifications;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Rhythm;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Lt2.Usage.Counting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.24.1: every email of the member's rhythm carries the account's stop link, as the one-click stop of RFC 8058 in its
/// headers and as "atsisakyti laiškų" in the footer (owner, 2026-11-17), to the page; logs never keep the token.
/// </summary>
public sealed class MemberStopLinkTests
{
    private const string Site = "https://lietuva2.test";
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string To = "ruta@pavyzdys.test";
    private const string Bill = "XVP-0412";
    private const string Title = "Gyventojų pajamų mokesčio įstatymo pataisa";
    private const string Account = AccountScreens.EmailRhythm;
    private const string FooterStart = "<p style=\"margin-top:24px;padding-top:8px;border-top:1px solid #cbd5e1;font-size:13px;color:#64748b\">";

    /// <summary>Why each email of <see cref="EveryKind"/> came, in its order: the footer's first sentence.</summary>
    private static readonly string[] Reasons =
    [
        "Šį laišką gavote, nes sekate šį projektą Lietuva 2.0.",
        "Šį laišką gavote, nes sekate šį projektą Lietuva 2.0.",
        "Šį laišką gavote, nes sekate šią organizaciją Lietuva 2.0.",
        "Šį laišką gavote, nes sekate šį projektą Lietuva 2.0.",
        "Šį laišką gavote, nes sekate šį narį Lietuva 2.0.",
        "Šį laišką gavote, nes šis argumentas – jūsų.",
        "Šį laišką gavote pagal savo laiškų pasirinkimus Lietuva 2.0.",
    ];

    private static readonly IStringLocalizer<EmailTemplates> L =
        new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static readonly IOptions<SiteOptions> SiteOptions = Options.Create(new SiteOptions { Address = Site });

    private static FollowTemplates Follow() => new(L, SiteOptions);

    private static FollowPlannedEmail Planned(string? token) =>
        new(To, Bill, Title, "adoption", new DateOnly(2026, 11, 19), "/seimas/XVP-0412", Account, token);

    private static IEnumerable<EmailMessage> EveryKind(string? token)
    {
        var follow = Follow();
        yield return follow.Planned(Planned(token));
        yield return follow.Voted(new FollowVotedEmail(To, Bill, Title, null, new DateOnly(2026, 11, 13), true, null, null, null, null, "/seimas/balsavimai/1", [], Account, token));
        yield return follow.Statement(new FollowStatementEmail(To, "Pavyzdžių asociacija", null, null, Bill, Title, "for", "/seimas/XVP-0412", Account, null, token));
        yield return follow.Responses(new FollowResponsesEmail(To, Bill, Title, new DateOnly(2026, 11, 15), [new FollowedResponse("Jonas Petraitis", null, "against")], "/seimas/XVP-0412#atsakymai", Account, token));
        yield return follow.Member(new FollowMemberEmail(To, "Rūta Mockutė", Bill, Title, "/seimas/XVP-0412", "/nariai/ruta-mockute-abcdefghij", Account, StopToken: token));
        yield return follow.Comments(new CommentNoticeEmail(To, [new CommentedArgumentLine(Bill, Title, "/seimas/XVP-0412")], Account, token));
        yield return Gathered(follow, token);
    }

    private static EmailMessage Gathered(FollowTemplates follow, string? token)
    {
        INoticePart[] parts = [new NoticePart<FollowPlannedEmail>(MessageKinds.FollowPlannedEmail, 10, follow.PlannedPart)];
        var item = new GatheredNotice(MessageKinds.FollowPlannedEmail, JsonSerializer.SerializeToElement(Planned(token), JsonSerializerOptions.Web));
        return new GatheredTemplates(L, SiteOptions, parts).Render(new GatheredNoticesEmail(To, "daily", [item, item], Account, token));
    }

    [Fact]
    public void Every_email_of_the_rhythm_offers_the_one_click_stop_in_its_headers()
    {
        foreach (var email in EveryKind(Token))
        {
            email.Headers.ShouldNotBeNull(email.Subject);
            email.Headers!.Count.ShouldBe(2, email.Subject);
            email.Headers[MpMailTemplates.ListUnsubscribe].ShouldBe("<" + Site + "/api/v1/notices/" + Token + "/stop>", email.Subject);
            email.Headers[MpMailTemplates.ListUnsubscribePost].ShouldBe("List-Unsubscribe=One-Click", email.Subject);
            email.Stream.ShouldBeNull("the member stream, not the MPs'");
        }
    }

    /// <summary>
    /// Owner, 2026-11-17: the footer, under the rule in small grey type, says why the email came (one sentence, no link), then
    /// "Keisti periodiškumą arba atsisakyti laiškų" with the two links, then the privacy line; the body holds neither link.
    /// </summary>
    [Fact]
    public void Every_email_of_the_rhythm_ends_with_the_footer_linking_how_often_and_the_stop_page_not_the_api()
    {
        var emails = EveryKind(Token).ToList();
        emails.Count.ShouldBe(Reasons.Length);
        for (var i = 0; i < emails.Count; i++)
        {
            var email = emails[i];
            var lines = email.TextBody.TrimEnd('\n').Split('\n');
            lines[^3].ShouldBe(Reasons[i], email.Subject);
            lines[^2].ShouldBe($"Keisti periodiškumą: {Site}{Account} · Atsisakyti laiškų: {Site}/laiskai/nebegauti/{Token}", email.Subject);
            lines[^1].ShouldBe($"Kaip tvarkome duomenis, rašoma privatumo pranešime: {Site}/privatumas", email.Subject);
            email.TextBody.ShouldNotContain("/api/v1/notices/", Case.Sensitive, email.Subject);
            email.TextBody.ShouldNotContain("Nebegauti šių laiškų", Case.Insensitive, "the old line is gone");
            email.TextBody.ShouldNotContain("paskyroje", Case.Insensitive, "the old link clause is gone");
            email.TextBody.IndexOf(Site + "/laiskai/nebegauti/", StringComparison.Ordinal)
                .ShouldBe(email.TextBody.LastIndexOf(Site + "/laiskai/nebegauti/", StringComparison.Ordinal), "the stop page once, in the footer");

            var html = email.HtmlBody.ShouldNotBeNull();
            var footerAt = html.IndexOf(FooterStart, StringComparison.Ordinal);
            footerAt.ShouldBeGreaterThan(0, email.Subject);
            var footer = html[footerAt..];
            footer.ShouldBe(
                FooterStart + System.Net.WebUtility.HtmlEncode(Reasons[i])
                + $"<br><a href=\"{System.Net.WebUtility.HtmlEncode(Site + Account)}\">Keisti periodiškumą</a> arba <a href=\"{Site}/laiskai/nebegauti/{Token}\">atsisakyti laiškų</a>"
                + $"<br><a href=\"{Site}/privatumas\" style=\"font-weight:600;color:#1f6b3a\">Kaip tvarkome duomenis, rašoma privatumo pranešime</a></p></body></html>",
                email.Subject);
            html[..footerAt].ShouldNotContain("laiskai/nebegauti", Case.Sensitive, "only the footer links the stop page");
            html[..footerAt].ShouldNotContain("Keisti periodiškumą", Case.Sensitive);
        }
    }

    [Fact]
    public void Without_a_token_the_footer_links_only_how_often_and_there_are_no_headers()
    {
        var with = EveryKind(Token).ToList();
        var without = EveryKind(null).ToList();
        for (var i = 0; i < without.Count; i++)
        {
            without[i].Headers.ShouldBeNull(without[i].Subject);
            without[i].TextBody.ShouldNotContain("laiskai/nebegauti", Case.Sensitive);
            without[i].TextBody.ShouldContain($"\nKeisti periodiškumą: {Site}{Account}\n", Case.Sensitive, without[i].Subject);
            without[i].HtmlBody.ShouldNotBeNull().ShouldContain($"<br><a href=\"{System.Net.WebUtility.HtmlEncode(Site + Account)}\">Keisti periodiškumą</a><br>");
            without[i].HtmlBody!.ShouldNotContain("atsisakyti", Case.Insensitive);
            with[i].TextBody.Replace($" · Atsisakyti laiškų: {Site}/laiskai/nebegauti/{Token}", "", StringComparison.Ordinal)
                .ShouldBe(without[i].TextBody, "the token adds the stop link and nothing else");
        }
    }

    /// <summary>The follows screen renders <c>#laisku-daznis</c> (the account screens' test in <c>SignInComponentTests</c>).</summary>
    [Fact]
    public void The_footers_link_to_how_often_is_the_follows_screens_part_on_the_choice() =>
        NoticeDelivery.AccountPath.ShouldBe("/paskyra/sekimas#laisku-daznis");

    [Fact]
    public void The_modules_links_and_the_contracts_routes_name_the_same_addresses()
    {
        MemberMailLinks.OneClick(Token).ShouldBe(NoticeStopRoutes.StopOf(Token));
        MemberMailLinks.Page(Token).ShouldBe(NoticeStopRoutes.PageOf(Token));
        NoticeStopRoutes.Stop.Replace("{token}", Token, StringComparison.Ordinal).ShouldBe(NoticeStopRoutes.StopOf(Token));
    }

    [Fact]
    public void The_token_is_one_per_account_its_own_kind_and_a_token_the_routes_accept()
    {
        var keys = new MailKeys(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [MailKeys.ConfigurationKey] = "test-mail-hash-key" }).Build());
        var account = Guid.Parse("01890000-0000-7000-8000-000000000001");
        var token = keys.MemberStopToken(account, 0);

        token.ShouldBe(keys.MemberStopToken(account, 0), "every email carries the same link");
        MailKeys.LooksLikeToken(token).ShouldBeTrue();
        keys.MemberStopToken(Guid.Parse("01890000-0000-7000-8000-000000000002"), 0).ShouldNotBe(token);
        keys.MemberStopToken(account, 1).ShouldNotBe(token, "a new generation is an unrelated link");
        keys.DerivedToken(1, "preferences", 0).ShouldNotBe(token);
    }

    [Theory]
    [InlineData("/laiskai/nebegauti/" + Token, "/laiskai/nebegauti/***")]
    [InlineData("POST /api/v1/notices/" + Token + "/stop responded 204", "POST /api/v1/notices/***/stop responded 204")]
    [InlineData("https://lietuva2.test/laiskai/nebegauti/" + Token + "?x=1", "https://lietuva2.test/laiskai/nebegauti/***?x=1")]
    [InlineData("/api/v1/me/notices/frequency", "/api/v1/me/notices/frequency")]
    [InlineData("/api/v1/me/notices", "/api/v1/me/notices")]
    public void Logs_keep_the_stop_links_paths_without_the_token(string text, string logged) =>
        SecretPaths.Redact(text).ShouldBe(logged);

    [Fact]
    public void The_stop_page_is_left_out_of_the_usage_list_and_its_token_out_of_the_counted_path()
    {
        UsageRules.IsListed("/laiskai/nebegauti/***").ShouldBeFalse();
        var counted = UsageRules.CountedPath("/laiskai/nebegauti/{Token}", new Dictionary<string, string?> { ["Token"] = Token });
        counted.ShouldNotContain(Token);
    }
}
