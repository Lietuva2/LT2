using System.Text.Json;
using Lt2.Api.Contracts.Follows;
using Lt2.Hosting;
using Lt2.Notifications;
using Lt2.Notifications.MpMail;
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
/// headers and as a line "Nebegauti šių laiškų" under the account line, to the page; logs never keep the token.
/// </summary>
public sealed class MemberStopLinkTests
{
    private const string Site = "https://lietuva2.test";
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string To = "ruta@pavyzdys.test";
    private const string Bill = "XVP-0412";
    private const string Title = "Gyventojų pajamų mokesčio įstatymo pataisa";
    private const string Account = "/paskyra/sekimas#sekami";

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

    [Fact]
    public void Every_email_of_the_rhythm_has_the_stop_line_under_the_account_line_pointing_at_the_page_not_the_api()
    {
        foreach (var email in EveryKind(Token))
        {
            var lines = email.TextBody.Split('\n');
            var stop = Array.IndexOf(lines, "Nebegauti šių laiškų: " + Site + "/laiskai/nebegauti/" + Token);
            stop.ShouldBeGreaterThan(0, email.Subject);
            lines[stop - 1].ShouldEndWith("savo paskyroje: " + Site + Account, Case.Sensitive, email.Subject);
            email.TextBody.ShouldNotContain("/api/v1/notices/", Case.Sensitive, email.Subject);

            // The HTML part makes the line a link with its address under it, as every other action line (owner, 2026-11-17).
            email.HtmlBody.ShouldNotBeNull();
            email.HtmlBody!.ShouldContain($"<a href=\"{Site}/laiskai/nebegauti/{Token}\" style=\"font-weight:600;color:#1f6b3a\">Nebegauti šių laiškų</a>", Case.Sensitive, email.Subject);
        }
    }

    [Fact]
    public void Without_a_token_the_email_is_as_it_was_with_neither_headers_nor_line()
    {
        var with = EveryKind(Token).ToList();
        var without = EveryKind(null).ToList();
        for (var i = 0; i < without.Count; i++)
        {
            without[i].Headers.ShouldBeNull(without[i].Subject);
            without[i].TextBody.ShouldNotContain("Nebegauti", Case.Sensitive);
            with[i].TextBody.Replace("\nNebegauti šių laiškų: " + Site + "/laiskai/nebegauti/" + Token, "", StringComparison.Ordinal)
                .ShouldBe(without[i].TextBody, "the plain part gains the one line and nothing else");
        }
    }

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
