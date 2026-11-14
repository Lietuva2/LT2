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
        ReasonSimilarity.AreSimilar(Existing, "didziausias pajamas gaunantys dabar moka mazesni efektyvuji tarifa nei samdomi darbuotojai").ShouldBeTrue();

    [Fact]
    public void Other_word_endings_and_small_rewordings_are_still_similar() =>
        ReasonSimilarity.AreSimilar(Existing, "Daugiausia pajamų gaunantieji moka mažesnį efektyvų tarifą negu samdomi darbuotojai").ShouldBeTrue();

    [Fact]
    public void A_different_argument_is_not_similar() =>
        ReasonSimilarity.AreSimilar(Existing, "Gynyba finansuojama nekeliant PVM.").ShouldBeFalse();

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

    [Theory]
    [InlineData("Skambinkite +370 612 34567")]
    [InlineData("Tel. 8 612 34567")]
    [InlineData("tel 861234567")]
    public void A_phone_number_is_flagged(string text) =>
        RuleBasedPreScreening.Screen(text).ShouldContain(ScreeningFlags.Phone);

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
}
