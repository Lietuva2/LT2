using System.Text.Json;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.24: the gathered email. A part per kind in the switches' order, each line with its day and its link, the link to
/// the switches and the choice at the end, the footer; nothing about the person's side, a bill's counts or other people.
/// </summary>
public sealed class GatheredEmailTests
{
    private const string Bill = "XVP-0412";
    private const string Title = "Gyventojų pajamų mokesčio įstatymo pataisa";
    private const string To = "ruta@pavyzdys.test";

    private static readonly IStringLocalizer<EmailTemplates> L =
        new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static GatheredTemplates Templates()
    {
        var site = Options.Create(new SiteOptions { Address = "https://lietuva2.test" });
        var follow = new FollowTemplates(L, site);
        INoticePart[] parts =
        [
            new NoticePart<FollowResponsesEmail>(MessageKinds.FollowResponsesEmail, 40, follow.ResponsesPart),
            new NoticePart<FollowPlannedEmail>(MessageKinds.FollowPlannedEmail, 10, follow.PlannedPart),
            new NoticePart<FollowStatementEmail>(MessageKinds.FollowStatementEmail, 30, follow.StatementPart),
            new NoticePart<FollowVotedEmail>(MessageKinds.FollowVotedEmail, 20, follow.VotedPart),
        ];
        return new GatheredTemplates(L, site, parts);
    }

    private static GatheredNotice Item<T>(string kind, T payload) => new(kind, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web));

    private static GatheredNoticesEmail Everything(string rhythm) => new(To, rhythm,
    [
        Item(MessageKinds.FollowResponsesEmail, new FollowResponsesEmail(To, Bill, Title, new DateOnly(2026, 11, 15),
            [new FollowedResponse("Jonas Petraitis", "Reformų partija", "against")], "/seimas/XVP-0412#atsakymai", "/sveiki#sekami")),
        Item(MessageKinds.FollowStatementEmail, new FollowStatementEmail(To, "Pavyzdžių asociacija", "domain", "pavyzdys.test", Bill, Title, "for",
            "/seimas/XVP-0412", "/sveiki#sekami", new DateOnly(2026, 11, 14))),
        Item(MessageKinds.FollowVotedEmail, new FollowVotedEmail(To, Bill, Title, "consideration", new DateOnly(2026, 11, 13), false, 74, 44, 10, null,
            "/seimas/balsavimai/99100006", [new FollowedMpChoice("Ona Jonaitienė", "for")], "/sveiki#sekami")),
        Item(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(To, Bill, Title, "adoption", new DateOnly(2026, 11, 19), "/seimas/XVP-0412", "/sveiki#sekami")),
    ], "/sveiki#sekami");

    [Fact]
    public void Each_kind_has_its_part_in_the_switches_order_whatever_order_things_were_found_in()
    {
        var text = Templates().Render(Everything("immediate")).TextBody;
        var planned = text.IndexOf("Planuojama Seime:", StringComparison.Ordinal);
        var voted = text.IndexOf("Seimas balsavo:", StringComparison.Ordinal);
        var statements = text.IndexOf("Organizacijų pozicijos:", StringComparison.Ordinal);
        var answers = text.IndexOf("Seimo narių atsakymai:", StringComparison.Ordinal);
        planned.ShouldBeGreaterThan(0);
        voted.ShouldBeGreaterThan(planned);
        statements.ShouldBeGreaterThan(voted);
        answers.ShouldBeGreaterThan(statements);
    }

    [Fact]
    public void Every_line_says_its_day_and_links_to_its_page_with_the_full_address()
    {
        var text = Templates().Render(Everything("weekly")).TextBody;
        text.ShouldContain("Planuojama 2026-11-19 (priėmimas). https://lietuva2.test/seimas/XVP-0412");
        text.ShouldContain("– 2026-11-13 XVP-0412 · " + Title + " (svarstymas). Rezultatas: už 74, prieš 44, susilaikė 10. https://lietuva2.test/seimas/balsavimai/99100006");
        text.ShouldContain("Ona Jonaitienė: už");
        text.ShouldContain("– 2026-11-14 „Pavyzdžių asociacija“ (patvirtinta per el. pašto adresą @pavyzdys.test)");
        text.ShouldContain("– 2026-11-15 XVP-0412 · " + Title + ": https://lietuva2.test/seimas/XVP-0412#atsakymai");
        text.ShouldContain("Jonas Petraitis (Reformų partija): balsuos prieš");
    }

    [Fact]
    public void It_ends_with_the_link_to_the_switches_and_the_choice_and_the_privacy_footer()
    {
        var email = Templates().Render(Everything("daily"));
        email.TextBody.ShouldContain("kaip dažnai, galite pakeisti savo paskyroje: https://lietuva2.test/sveiki#sekami");
        email.TextBody.ShouldContain("https://lietuva2.test/privatumas");
        email.HtmlBody!.ShouldNotContain("<img", Case.Insensitive, "no tracking");
    }

    [Theory]
    [InlineData("immediate", "Lietuva 2.0: naujienos apie tai, ką sekate")]
    [InlineData("daily", "Lietuva 2.0: dienos naujienos apie tai, ką sekate")]
    [InlineData("weekly", "Lietuva 2.0: savaitės naujienos apie tai, ką sekate")]
    public void The_subject_says_which_email_it_is(string rhythm, string subject) =>
        Templates().Render(Everything(rhythm)).Subject.ShouldBe(subject);

    [Fact]
    public void A_gathered_email_says_nothing_about_the_persons_side_a_bills_counts_or_other_people()
    {
        var text = Templates().Render(Everything("weekly")).TextBody;
        foreach (var word in new[] { "jūsų pozicija", "dalyvi", "Man svarbu", "argument", "naudotoj" })
        {
            text.ShouldNotContain(word, Case.Insensitive);
        }
    }

    [Fact]
    public void A_kind_without_a_part_is_refused_rather_than_left_out_silently() =>
        Should.Throw<InvalidOperationException>(() => Templates().Render(new GatheredNoticesEmail(To, "immediate",
            [Item("email.unknown", new { to = To }), Item(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(To, Bill, Title, null, new DateOnly(2026, 11, 19), "/seimas/XVP-0412", "/sveiki#sekami"))],
            "/sveiki#sekami")));
}
