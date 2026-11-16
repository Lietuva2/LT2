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
            new NoticePart<CommentNoticeEmail>(MessageKinds.CommentNoticeEmail, 60, follow.CommentsPart),
            new NoticePart<FollowMemberEmail>(MessageKinds.FollowMemberEmail, 50, follow.MemberPart),
        ];
        return new GatheredTemplates(L, site, parts);
    }

    private static GatheredNotice Item<T>(string kind, T payload) => new(kind, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web));

    private static GatheredNoticesEmail Everything(string rhythm) => new(To, rhythm,
    [
        Item(MessageKinds.FollowResponsesEmail, new FollowResponsesEmail(To, Bill, Title, new DateOnly(2026, 11, 15),
            [new FollowedResponse("Jonas Petraitis", "Reformų partija", "against")], "/seimas/XVP-0412#atsakymai", "/paskyra/sekimas#sekami")),
        Item(MessageKinds.FollowStatementEmail, new FollowStatementEmail(To, "Pavyzdžių asociacija", "domain", "pavyzdys.test", Bill, Title, "for",
            "/seimas/XVP-0412", "/paskyra/sekimas#sekami", new DateOnly(2026, 11, 14))),
        Item(MessageKinds.FollowVotedEmail, new FollowVotedEmail(To, Bill, Title, "consideration", new DateOnly(2026, 11, 13), false, 74, 44, 10, null,
            "/seimas/balsavimai/99100006", [new FollowedMpChoice("Ona Jonaitienė", "for")], "/paskyra/sekimas#sekami")),
        Item(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(To, Bill, Title, "adoption", new DateOnly(2026, 11, 19), "/seimas/XVP-0412", "/paskyra/sekimas#sekami")),
    ], "/paskyra/sekimas#sekami");

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
        email.TextBody.ShouldContain("kaip dažnai, galite pakeisti savo paskyroje: https://lietuva2.test/paskyra/sekimas#sekami");
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
            [Item("email.unknown", new { to = To }), Item(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(To, Bill, Title, null, new DateOnly(2026, 11, 19), "/seimas/XVP-0412", "/paskyra/sekimas#sekami"))],
            "/paskyra/sekimas#sekami")));

    /// <summary>
    /// The kinds that joined from the profiles and the comments (design 8.25.5, 8.23.9): their parts come after the four follow
    /// parts, a line per argument with the bill and the link; never an argument's or a comment's text, a writer, a count or
    /// "Sutinku". Comments of two runs for the same person come in one part.
    /// </summary>
    [Fact]
    public void A_followed_members_argument_and_comments_under_ones_own_have_their_own_parts_last_with_only_names_bills_and_links()
    {
        var everything = Everything("daily");
        var email = everything with
        {
            Notices =
            [
                Item(MessageKinds.CommentNoticeEmail, new CommentNoticeEmail(To, [new CommentedArgumentLine(Bill, Title, "/seimas/XVP-0412#reason-a")], "/paskyra/sekimas#sekami")),
                .. everything.Notices,
                Item(MessageKinds.FollowMemberEmail, new FollowMemberEmail(To, "Rūta Mockutė", Bill, Title, "/seimas/XVP-0412#reason-b", "/nariai/abcd1234", "/paskyra/sekimas#sekami")),
                Item(MessageKinds.CommentNoticeEmail, new CommentNoticeEmail(To, [new CommentedArgumentLine("XVP-0533", "Kita pataisa", "/seimas/XVP-0533#reason-c")], "/paskyra/sekimas#sekami")),
            ],
        };
        var text = Templates().Render(email).TextBody;
        var answers = text.IndexOf("Seimo narių atsakymai:", StringComparison.Ordinal);
        var members = text.IndexOf("Ką paskelbė sekami nariai:", StringComparison.Ordinal);
        var comments = text.IndexOf("Nauji komentarai po jūsų argumentais:", StringComparison.Ordinal);
        members.ShouldBeGreaterThan(answers);
        comments.ShouldBeGreaterThan(members);
        text.IndexOf("Nauji komentarai", comments + 1, StringComparison.Ordinal).ShouldBe(-1, "one part for the comments");
        text.ShouldContain("– Rūta Mockutė paskelbė argumentą dėl XVP-0412 · " + Title + ". https://lietuva2.test/seimas/XVP-0412#reason-b");
        text.ShouldContain("– XVP-0412 · " + Title + ": https://lietuva2.test/seimas/XVP-0412#reason-a");
        text.ShouldContain("– XVP-0533 · Kita pataisa: https://lietuva2.test/seimas/XVP-0533#reason-c");
        text.ShouldNotContain("Sutinku", Case.Insensitive);
    }
}