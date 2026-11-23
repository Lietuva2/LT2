using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 8.11.2, 8.14.14, 8.20.1: the follow emails (planned, voted, an organisation's statement, MPs' answers). No tracking, nobody named.</summary>
public sealed class FollowTemplateTests
{
    private static FollowTemplates Templates() =>
        new(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions()));

    [Fact]
    public void The_planned_email_says_planuojama_with_the_date_that_dates_move_and_links_to_the_account_page()
    {
        var email = Templates().Planned(new FollowPlannedEmail(
            "ruta@pavyzdys.test", "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "adoption", new DateOnly(2026, 11, 19), "/seimas/XVP-0412", "/paskyra#followed-items"));
        email.Subject.ShouldBe("Lietuva 2.0: XVP-0412 planuojamas Seime 2026-11-19");
        email.TextBody.ShouldContain("Planuojama 2026-11-19.");
        email.TextBody.ShouldContain("Etapas: priėmimas.");
        email.TextBody.ShouldContain("data dar gali pasikeisti");
        email.TextBody.ShouldContain("/seimas/XVP-0412");
        email.TextBody.ShouldContain("/paskyra#followed-items");
        email.HtmlBody!.ShouldNotContain("<img", Case.Insensitive);
    }

    [Fact]
    public void The_voted_email_has_the_result_the_note_and_how_the_followed_MPs_voted()
    {
        var email = Templates().Voted(new FollowVotedEmail(
            "ruta@pavyzdys.test", "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "consideration", new DateOnly(2026, 5, 19), false, 74, 44, 10,
            "Pavardinio balsavimo rezultatai nesutampa su protokolu.", "/seimas/balsavimai/99100006",
            [new FollowedMpChoice("Jonas Petraitis", "against"), new FollowedMpChoice("Ona Jonaitienė", "did-not-vote")], "/paskyra#followed-items"));
        email.TextBody.ShouldContain("Rezultatas: už 74, prieš 44, susilaikė 10.");
        email.TextBody.ShouldContain("Seimo pastaba: Pavardinio");
        email.TextBody.ShouldContain("– Jonas Petraitis: prieš");
        email.TextBody.ShouldContain("– Ona Jonaitienė: nebalsavo");
        email.TextBody.ShouldContain("/seimas/balsavimai/99100006");
        email.TextBody.ShouldNotContain("kreipim", Case.Insensitive, "nothing about the person's side or the record");
    }

    /// <summary>
    /// Design 8.1 step 6, 8.6: a followed bill rejected or withdrawn, as a moderator recorded: what happened and when, that
    /// taking a side has ended, the source on lrs.lt and the bill's page; never the counts, a side or the moderator.
    /// </summary>
    [Theory]
    [InlineData("rejected", "Lietuva 2.0: Seimas atmetė XVP-0618", "2026-11-17 Seimas atmetė jūsų sekamą projektą XVP-0618 · Pataisa.", "Seimas projektą atmetė.")]
    [InlineData("withdrawn", "Lietuva 2.0: XVP-0618 atsiimtas", "2026-11-17 jūsų sekamą projektą XVP-0618 · Pataisa atsiėmė jo iniciatoriai.", "Iniciatoriai projektą atsiėmė.")]
    public void The_closed_email_says_what_happened_the_day_the_source_and_the_bill_and_nothing_else(string reason, string subject, string body, string line)
    {
        var templates = new FollowTemplates(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = "https://lt2.pavyzdys.test" }));
        const string Source = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas";
        var model = new FollowClosedEmail("ruta@pavyzdys.test", "XVP-0618", "Pataisa", reason, new DateOnly(2026, 11, 17), Source, "/seimas/XVP-0618", "/paskyra/sekimas#email-frequency");
        var email = templates.Closed(model);
        email.Subject.ShouldBe(subject);
        email.TextBody.ShouldContain(body);
        email.TextBody.ShouldContain("Nurodyti, ko prašote dėl šio projekto, nebegalima.");
        email.TextBody.ShouldContain("Šaltinis: " + Source);
        email.TextBody.ShouldContain("https://lt2.pavyzdys.test/seimas/XVP-0618");
        email.TextBody.ShouldNotContain("{", Case.Sensitive, "every placeholder is filled");
        email.TextBody.ShouldNotContain("moderator", Case.Insensitive);
        email.HtmlBody!.ShouldNotContain("<img", Case.Insensitive);

        var part = templates.ClosedPart([model]);
        part.ShouldStartWith("Seimas nebesvarsto:");
        part.ShouldContain($"– 2026-11-17 XVP-0618 · Pataisa. {line}\n  Projektas: https://lt2.pavyzdys.test/seimas/XVP-0618\n  Šaltinis: " + Source);
        part.ShouldContain("Šaltinis: " + Source);
    }

    [Theory]
    [InlineData("domain", "lgs.pavyzdys.test", "patvirtinta per el. pašto adresą @lgs.pavyzdys.test")]
    [InlineData("moderator", null, "patvirtino moderatorius")]
    [InlineData(null, null, "nepatvirtinta")]
    public void The_statement_email_has_the_organisation_its_label_the_bill_the_stance_and_a_full_link_and_nothing_else(string? by, string? domain, string label)
    {
        var templates = new FollowTemplates(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = "https://lt2.pavyzdys.test" }));
        var email = templates.Statement(new FollowStatementEmail(
            "ruta@pavyzdys.test", "Pavyzdinė asociacija", by, domain, "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "against", "/seimas/XVP-0412", "/paskyra#followed-items"));
        email.Subject.ShouldBe("Lietuva 2.0: „Pavyzdinė asociacija“ paskelbė poziciją dėl XVP-0412");
        email.TextBody.ShouldContain($"„Pavyzdinė asociacija“ ({label})");
        email.TextBody.ShouldContain("XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa");
        email.TextBody.ShouldContain("Pozicija: prašo balsuoti prieš.");
        email.TextBody.ShouldContain("https://lt2.pavyzdys.test/seimas/XVP-0412");
        email.TextBody.ShouldContain("https://lt2.pavyzdys.test/paskyra#followed-items");
        email.TextBody.ShouldNotContain("{", Case.Sensitive, "every placeholder is filled");
        email.HtmlBody!.ShouldNotContain("<img", Case.Insensitive);
    }

    [Fact]
    public void Every_stance_has_its_words_in_the_statement_email()
    {
        foreach (var stance in new[] { "for", "against", "remarks" })
        {
            var email = Templates().Statement(new FollowStatementEmail("r@pavyzdys.test", "Asociacija", null, null, "XVP-0412", "Pataisa", stance, "/seimas/XVP-0412", "/paskyra#followed-items"));
            email.TextBody.ShouldNotContain("Follow.Stance", Case.Sensitive, stance);
        }
    }

    [Fact]
    public void The_responses_email_lists_each_MP_with_faction_and_stance_links_to_the_answers_and_holds_no_explanation()
    {
        var templates = new FollowTemplates(
            new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
            Options.Create(new SiteOptions { Address = "https://lt2.pavyzdys.test" }));
        var email = templates.Responses(new FollowResponsesEmail(
            "ruta@pavyzdys.test", "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", new DateOnly(2026, 11, 16),
            [
                new FollowedResponse("Ona Jonaitienė", "Pilietinės sąjungos frakcija", "for"),
                new FollowedResponse("Jonas Petraitis", null, "against"),
            ],
            "/seimas/XVP-0412#responses", "/paskyra#followed-items"));
        email.Subject.ShouldBe("Lietuva 2.0: Seimo narių atsakymai dėl XVP-0412");
        email.TextBody.ShouldContain("2026-11-16 dėl jūsų sekamo projekto XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa atsakė:\n– Ona Jonaitienė");
        email.TextBody.ShouldContain("– Ona Jonaitienė (Pilietinės sąjungos frakcija): balsuos už");
        email.TextBody.ShouldContain("– Jonas Petraitis: balsuos prieš");
        email.TextBody.ShouldContain("https://lt2.pavyzdys.test/seimas/XVP-0412#responses");
        email.TextBody.ShouldContain("https://lt2.pavyzdys.test/paskyra#followed-items");
        email.TextBody.ShouldNotContain("{", Case.Sensitive, "every placeholder is filled");
        email.HtmlBody!.ShouldNotContain("<img", Case.Insensitive);
    }

    [Theory]
    [InlineData("for", "balsuos už")]
    [InlineData("against", "balsuos prieš")]
    [InlineData("undecided", "dar neapsisprendė")]
    public void Every_stance_of_an_answer_has_its_words_in_the_responses_email(string stance, string words) =>
        Templates().Responses(new FollowResponsesEmail(
                "r@pavyzdys.test", "XVP-0412", "Pataisa", new DateOnly(2026, 11, 16), [new FollowedResponse("Jonas Petraitis", "Reformų partijos frakcija", stance)],
                "/seimas/XVP-0412#responses", "/paskyra#followed-items"))
            .TextBody.ShouldContain("– Jonas Petraitis (Reformų partijos frakcija): " + words);

    [Fact]
    public void A_consensus_vote_says_so_without_numbers() =>
        Templates().Voted(new FollowVotedEmail("r@pavyzdys.test", "XVP-0533", "Pataisa", "presentation", new DateOnly(2026, 3, 17), true, null, null, null, null, "/seimas/balsavimai/1", [], "/paskyra#followed-items"))
            .TextBody.ShouldContain("bendru sutarimu");
}
