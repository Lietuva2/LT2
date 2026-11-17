using Lt2.Appeals.Domain;
using Lt2.Appeals.Screening;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 8.1: a new argument similar to an existing one is offered the existing one first.</summary>
public sealed class ReasonSimilarityTests
{
    private const string Existing = "Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.";

    [Fact]
    public void The_same_argument_written_again_is_similar() =>
        ReasonSimilarity.Score(Existing, Existing).ShouldBe(1);

    [Fact]
    public void Case_punctuation_and_lithuanian_letters_do_not_make_an_argument_different() =>
        ReasonSimilarity.Score(Existing, "didziausias pajamas gaunantys dabar moka mazesni efektyvuji tarifa nei samdomi darbuotojai").ShouldBeGreaterThanOrEqualTo(ReasonSimilarity.Threshold);

    [Fact]
    public void Other_word_endings_and_small_rewordings_are_still_similar() =>
        ReasonSimilarity.Score(Existing, "Daugiausia pajamų gaunantieji moka mažesnį efektyvų tarifą negu samdomi darbuotojai").ShouldBeGreaterThanOrEqualTo(ReasonSimilarity.Threshold);

    [Fact]
    public void A_different_argument_is_not_similar() =>
        ReasonSimilarity.Score(Existing, "Gynyba finansuojama nekeliant PVM.").ShouldBeLessThan(ReasonSimilarity.Threshold);

    [Fact]
    public void At_most_three_similar_arguments_are_offered_most_similar_first()
    {
        var existing = new[]
        {
            "Gynyba finansuojama nekeliant PVM.",
            Existing,
            "Didžiausias pajamas gaunantys moka mažesnį tarifą nei darbuotojai.",
            "Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą.",
            "Didžiausias pajamas gaunantys dabar moka mažesnį tarifą nei samdomi.",
        };

        var offered = ReasonSimilarity.MostSimilar(Existing, existing, x => x);

        offered.Count.ShouldBe(3);
        offered[0].ShouldBe(Existing);
        offered.ShouldNotContain("Gynyba finansuojama nekeliant PVM.");
    }

    [Fact]
    public void Nothing_is_offered_for_an_empty_text() =>
        ReasonSimilarity.MostSimilar("  ", [Existing], x => x).ShouldBeEmpty();
}

/// <summary>Design 8.1, 8.3: pre-screening flags what rules can catch, for a moderator. Nothing is sent anywhere.</summary>
public sealed class PreScreeningTests
{
    [Fact]
    public void An_email_address_is_flagged() =>
        RuleBasedPreScreening.Screen("Rašykite ruta.mockute@pavyzdys.test").ShouldContain(ScreeningFlags.Email);

    /// <summary>The owner, 2026-11-17: a Lithuanian number, international, with the trunk 0 in use, or the old 8.</summary>
    [Theory]
    [InlineData("Skambinkite +370 612 34567")]
    [InlineData("+37061234567")]
    [InlineData("00370 612 34567")]
    [InlineData("+370 (5) 212 3456")]
    [InlineData("+370 37 123 456")]
    [InlineData("Tel. 0612 34567")]
    [InlineData("tel 061234567")]
    [InlineData("Vilniuje 05 212 3456")]
    [InlineData("(0 5) 212-3456")]
    [InlineData("Tel. 8 612 34567")]
    [InlineData("tel 861234567")]
    [InlineData("8 5 212 3456")]
    [InlineData("(8 37) 123 456")]
    public void A_lithuanian_phone_number_international_with_the_trunk_0_or_the_old_8_is_flagged(string text) =>
        RuleBasedPreScreening.Screen(text).ShouldContain(ScreeningFlags.Phone);

    /// <summary>The owner, 2026-11-17: dates, year spans, sums, laws' and documents' numbers, percentages and link paths are no phone.</summary>
    [Theory]
    [InlineData("Įsigalioja 2026-11-20.")]
    [InlineData("Kadencija 2024-2028")]
    [InlineData("Kadencija 2024–2028")]
    [InlineData("Skirta 25 000 000 eurų")]
    [InlineData("Iš viso 1 234 567 gyventojų")]
    [InlineData("Įstatymas Nr. XIV-1234")]
    [InlineData("Sprendimas Nr. 12-345")]
    [InlineData("Pritaria 86 % apklaustųjų")]
    [InlineData("Žr. https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/861234567")]
    [InlineData("Lentelė 0,5 ir 85 000")]
    [InlineData("Nr. 2026-861234")]
    public void A_date_a_year_span_a_sum_a_document_number_a_percentage_or_a_link_path_is_no_phone_number(string text) =>
        RuleBasedPreScreening.ContainsPhoneNumber(text).ShouldBeFalse(text);

    /// <summary>The owner, 2026-11-17: a mobile number without a prefix, eight digits starting with 6, together or grouped as people write it.</summary>
    [Theory]
    [InlineData("Skambinkite 61234567")]
    [InlineData("Skambinkite 612 34567")]
    [InlineData("Skambinkite 6123 4567")]
    [InlineData("Skambinkite 6 1234 567")]
    [InlineData("tel.: 612-34567")]
    [InlineData("Mano numeris (61234567), skambinkite vakare.")]
    [InlineData("61234567")]
    [InlineData("Rašykite arba skambinkite: 6123 4567.")]
    public void A_mobile_number_without_a_prefix_is_flagged(string text) =>
        RuleBasedPreScreening.Screen(text).ShouldContain(ScreeningFlags.Phone);

    /// <summary>The owner, 2026-11-17: near misses of the mobile rule are no phone number, and a landline without a prefix is not flagged.</summary>
    [Theory]
    [InlineData("Skirta 60 000 000 eurų")]
    [InlineData("Skirta 6 000 000 eurų")]
    [InlineData("Iš viso 612 345 678 eurų")]
    [InlineData("Suma 612 345 eurų")]
    [InlineData("Kodas 612345678")]
    [InlineData("Kodas 1612345678")]
    [InlineData("Įstatymas Nr. XIV-61234567")]
    [InlineData("Dokumentas Nr. 61234567")]
    [InlineData("dokumentas nr.61234567")]
    [InlineData("Kaina 61234567 €")]
    [InlineData("Kaina €61234567")]
    [InlineData("Pritaria 61234567 % visų")]
    [InlineData("Žr. https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/61234567")]
    [InlineData("Vilniuje 5 212 3456")]
    [InlineData("Kaune 37 123 456")]
    [InlineData("Lentelė 6123,4567")]
    [InlineData("Data 2026-11-20, 6123")]
    [InlineData("A61234567")]
    public void Near_misses_of_a_mobile_number_and_a_landline_without_a_prefix_are_no_phone_number(string text) =>
        RuleBasedPreScreening.ContainsPhoneNumber(text).ShouldBeFalse(text);

    [Fact]
    public void A_valid_lithuanian_personal_code_is_flagged()
    {
        // A made-up code with a valid check digit (the standard's own textbook example).
        RuleBasedPreScreening.IsPersonalCode("33309240064").ShouldBeTrue();
        RuleBasedPreScreening.Screen("Mano kodas 33309240064").ShouldBe([ScreeningFlags.PersonalCode]);
    }

    [Theory]
    [InlineData("33309240065")]
    [InlineData("73309240064")]
    [InlineData("33313240064")]
    public void Eleven_digits_that_cannot_be_a_personal_code_are_not_one(string digits) =>
        RuleBasedPreScreening.IsPersonalCode(digits).ShouldBeFalse();

    [Theory]
    [InlineData("Žr. https://pavyzdys.test/straipsnis")]
    [InlineData("Daugiau www.pavyzdys.test")]
    [InlineData("Skaitykite pavyzdys.lt/tekstas")]
    public void A_link_is_flagged(string text) =>
        RuleBasedPreScreening.Screen(text).ShouldContain(ScreeningFlags.Link);

    [Theory]
    [InlineData("Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.")]
    [InlineData("Akcijų pardavimo įskaičiavimas paliečia vienkartinius įvykius, pvz., šeimos verslo pardavimą.")]
    [InlineData("Metinėms pajamoms, viršijančioms 60 vidutinių darbo užmokesčių, būtų taikomas 32 % tarifas nuo 2027 m. sausio 1 d.")]
    [InlineData("Prašo 6 800, prieš 4 280.")]
    public void Ordinary_arguments_with_numbers_are_not_flagged(string text) =>
        RuleBasedPreScreening.Screen(text).ShouldBeEmpty();
}

/// <summary>Design 8.3: the author receives the statement of reasons; the email never names a side.</summary>
public sealed class ModerationNoticeTests
{
    private static ModerationNoticeTemplate Template()
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new ModerationNoticeTemplate(new StringLocalizer<EmailTemplates>(factory), Options.Create(new SiteOptions()));
    }

    public static TheoryData<string> Notices() => new(ModerationNotices.All);

    [Theory]
    [MemberData(nameof(Notices))]
    public void Every_moderation_outcome_has_a_lithuanian_email_naming_the_bill_and_the_statement(string notice)
    {
        var email = Template().Render(new ModerationNoticeEmail(
            "ruta.mockute@pavyzdys.test", notice, "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "Aprašymas nėra neutralus.", "Akcijų pardavimo įskaičiavimas…"));

        email.To.ShouldBe("ruta.mockute@pavyzdys.test");
        email.Subject.ShouldStartWith("Lietuva 2.0: ");
        email.TextBody.ShouldContain("XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa");
        email.TextBody.ShouldContain("Moderatoriaus paaiškinimas: Aprašymas nėra neutralus.");
        email.TextBody.ShouldNotContain("{");
        email.HtmlBody.ShouldNotBeNull().ShouldContain("lang=\"lt\"");
    }

    [Theory]
    [MemberData(nameof(Notices))]
    public void A_moderation_email_never_names_a_side(string notice)
    {
        var text = Template().Render(new ModerationNoticeEmail("ruta@pavyzdys.test", notice, "XVP-0412", "Pataisa", null)).TextBody;
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?i)(?<!\p{L})(už|prieš|for|against)(?!\p{L})").ShouldBeFalse(text);
    }

    [Fact]
    public void The_line_breaks_of_a_statement_are_kept_in_the_html_part_and_its_text_is_encoded()
    {
        var email = Template().Render(new ModerationNoticeEmail(
            "ruta@pavyzdys.test", ModerationNotices.ReasonHidden, "XVP-0412", "Pataisa", "Pirma eilutė.\r\nAntra <eilutė>.\nTrečia."));

        email.HtmlBody.ShouldNotBeNull().ShouldContain("Pirma eilutė.<br>Antra &lt;eilutė&gt;.<br>Trečia.");
        email.TextBody.ShouldContain("Pirma eilutė.\r\nAntra <eilutė>.\nTrečia.");
    }
}
