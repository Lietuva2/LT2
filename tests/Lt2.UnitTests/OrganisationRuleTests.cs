using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals.Domain;
using Lt2.Web.Client.Organisations;

namespace Lt2.UnitTests;

/// <summary>Design 8.14.14 (stage A) as pure functions: organisations, their confirmation by email domain and their statements.</summary>
public sealed class OrganisationRuleTests
{
    public static TheoryData<string?, string?, string?, string?> Confirmations => new()
    {
        // website, address, expected error, expected stored domain
        { "https://lgs.lt", "info@lgs.lt", null, "lgs.lt" },
        { "https://www.lgs.lt/apie", "info@lgs.lt", null, "lgs.lt" },
        { "https://lgs.lt", "pirmininkas@mail.lgs.lt", null, "lgs.lt" },
        { "https://LGS.lt", "Info@LGS.LT", null, "lgs.lt" },
        { "https://lgs.lt", "info@evil-lgs.lt", "confirm.wrong-domain", null },
        { "https://lgs.lt", "info@lgs.lt.evil.example", "confirm.wrong-domain", null },
        { "https://lgs.lt", "info@xlgs.lt", "confirm.wrong-domain", null },
        { "https://www.lgs.lt", "info@www2.lgs.lt", null, "lgs.lt" },
        { "https://sub.lgs.lt", "info@lgs.lt", "confirm.wrong-domain", null },
        { "https://lgs.lt", "info@lgs.lt@evil.example", "confirm.address-invalid", null },
        { "https://lgs.lt", "\"info@lgs.lt\"@evil.example", "confirm.address-invalid", null },
        { "https://lgs.lt", "info%lgs.lt@evil.example", "confirm.address-invalid", null },
        { "https://lgs.lt", "evil.example!info@lgs.lt", "confirm.address-invalid", null },
        { "https://lgs.lt", "info @lgs.lt", "confirm.address-invalid", null },
        { "https://lgs.lt", "info@[192.0.2.1]", "confirm.address-invalid", null },
        { "https://lgs.lt", "info@lgs", "confirm.address-invalid", null },
        { "https://lgs.lt", "@lgs.lt", "confirm.address-invalid", null },
        { "https://lgs.lt", "info@", "confirm.address-invalid", null },
        { "https://lgs.lt", "(komentaras)info@lgs.lt", "confirm.address-invalid", null },
        { "https://lgs.lt", "info@lgs.lt.", "confirm.address-invalid", null },
        { "https://žalias-ratas.lt", "info@žalias-ratas.lt", null, "xn--alias-ratas-drc.lt" },
        { "https://xn--alias-ratas-drc.lt", "info@ŽALIAS-RATAS.LT", null, "xn--alias-ratas-drc.lt" },
        { "https://žalias-ratas.lt", "info@zalias-ratas.lt", "confirm.wrong-domain", null },
        { "https://lgs.lt", "info@gmail.com", "confirm.public-mail", null },
        { "https://lgs.lt", "info@inbox.lt", "confirm.public-mail", null },
        { "https://lgs.lt", "info@one.lt", "confirm.public-mail", null },
        { "https://lgs.lt", "info@mail.lt", "confirm.public-mail", null },
        { "https://lgs.lt", "info@takas.lt", "confirm.public-mail", null },
        { "https://lgs.lt", "info@yahoo.com", "confirm.public-mail", null },
        { "https://lgs.lt", "info@outlook.com", "confirm.public-mail", null },
        { "https://lgs.lt", "info@hotmail.com", "confirm.public-mail", null },
        { "https://lgs.lt", "info@live.com", "confirm.public-mail", null },
        { "https://lgs.lt", "info@icloud.com", "confirm.public-mail", null },
        { "https://lgs.lt", "info@proton.me", "confirm.public-mail", null },
        { "https://lgs.lt", "info@gmx.net", "confirm.public-mail", null },
        { "https://lgs.lt", "info@yandex.ru", "confirm.public-mail", null },
        { "https://lgs.lt", "info@mail.ru", "confirm.public-mail", null },
        { "https://inbox.lt", "info@inbox.lt", "confirm.shared-website", null },
        { "https://gmail.com", "kas@gmail.com", "confirm.shared-website", null },
        { "https://www.facebook.com/organizacija", "info@facebook.com", "confirm.shared-website", null },
        { "https://organizacija.wordpress.com", "info@organizacija.wordpress.com", "confirm.shared-website", null },
        { "https://organizacija.github.io", "info@organizacija.github.io", "confirm.shared-website", null },
        { "https://sites.google.com/view/organizacija", "info@google.com", "confirm.shared-website", null },
        { null, "info@lgs.lt", "confirm.no-website", null },
        { "", "info@lgs.lt", "confirm.no-website", null },
        { "http://lgs.lt", "info@lgs.lt", "confirm.no-website", null },
    };

    [Theory]
    [MemberData(nameof(Confirmations))]
    public void A_code_goes_only_to_an_address_on_the_websites_host_or_under_it_and_never_to_a_public_mail_service(
        string? website, string? address, string? error, string? domain)
    {
        var (refused, stored) = OrganisationRules.CheckConfirmationAddress(website, address);
        refused.ShouldBe(error);
        stored.ShouldBe(domain);
    }

    [Theory]
    [InlineData("lgs.lt", "lgs.lt", true)]
    [InlineData("mail.lgs.lt", "lgs.lt", true)]
    [InlineData("evil-lgs.lt", "lgs.lt", false)]
    [InlineData("lgs.lt.evil.example", "lgs.lt", false)]
    [InlineData("xlgs.lt", "lgs.lt", false)]
    [InlineData("lt", "lgs.lt", false)]
    public void A_domain_is_at_or_under_a_host_only_as_the_host_itself_or_a_name_under_it(string domain, string host, bool under) =>
        OrganisationRules.IsAtOrUnder(domain, host).ShouldBe(under);

    [Theory]
    [InlineData("https://www.lgs.lt", "lgs.lt")]
    [InlineData("https://LGS.lt/apie", "lgs.lt")]
    [InlineData("https://www.www.lgs.lt", "www.lgs.lt")]
    [InlineData("https://žalias-ratas.lt", "xn--alias-ratas-drc.lt")]
    [InlineData("http://lgs.lt", null)]
    [InlineData("https://user@lgs.lt", null)]
    public void The_websites_host_is_its_idn_form_in_lower_case_without_a_leading_www(string website, string? host) =>
        OrganisationRules.WebsiteHost(website).ShouldBe(host);

    [Theory]
    [InlineData("active", null, false, null)]
    [InlineData("active", "domain", false, "confirm.already")]
    [InlineData("active", "moderator", false, "confirm.already")]
    [InlineData("active", null, true, "confirm.barred")]
    [InlineData("blocked", null, false, "organisation.not-active")]
    [InlineData("closed", null, false, "organisation.not-active")]
    public void After_a_moderator_withdrew_a_confirmation_a_code_confirms_nothing(string status, string? confirmedBy, bool barred, string? error) =>
        OrganisationRules.CheckDomainConfirmation(status, confirmedBy, barred).ShouldBe(error);

    [Theory]
    [InlineData("domain", "lgs.lt", "https://www.lgs.lt/nauja", true)]
    [InlineData("domain", "lgs.lt", "https://kitas.lt", false)]
    [InlineData("domain", "lgs.lt", null, false)]
    [InlineData("domain", "lgs.lt", "https://sub.lgs.lt", false)]
    [InlineData("moderator", null, "https://kitas.lt", true)]
    [InlineData(null, null, "https://kitas.lt", true)]
    public void A_new_website_removes_a_confirmation_by_domain_unless_its_host_is_the_confirmed_domain(string? by, string? domain, string? website, bool keeps) =>
        OrganisationRules.KeepsConfirmation(by, domain, website).ShouldBe(keeps);

    [Theory]
    [InlineData("Lietuvos gyventojų sąjunga", "lietuvos-gyventoju-sajunga")]
    [InlineData("  Ąžuolų   bendruomenė  ", "azuolu-bendruomene")]
    [InlineData("VšĮ „Žalias ratas“", "vsi-zalias-ratas")]
    [InlineData("A.B.C. 2026", "a-b-c-2026")]
    [InlineData("„“–", "")]
    public void The_address_is_made_from_the_name_in_plain_letters(string name, string slug) =>
        OrganisationRules.Slug(name).ShouldBe(slug);

    [Fact]
    public void A_long_name_makes_an_address_of_at_most_sixty_characters_without_a_trailing_hyphen()
    {
        var slug = OrganisationRules.Slug(string.Join(' ', Enumerable.Repeat("Asociacija", 20)));
        slug.Length.ShouldBeLessThanOrEqualTo(OrganisationRules.SlugMaxLength);
        slug.ShouldNotEndWith("-");
        OrganisationRules.SlugCandidate(slug, 1).ShouldBe(slug);
        OrganisationRules.SlugCandidate(slug, 3).ShouldBe(slug + "-3");
    }

    [Theory]
    [InlineData("Ab", "organisation.name-length")]
    [InlineData("   ", "organisation.name-empty")]
    [InlineData("Abc", null)]
    [InlineData("  Pavyzdinė \n  asociacija ", null)]
    public void A_name_is_three_to_one_hundred_and_twenty_characters_on_one_line(string name, string? error) =>
        OrganisationRules.CheckName(name).ShouldBe(error);

    [Fact]
    public void Names_are_compared_without_case_and_extra_spaces()
    {
        OrganisationRules.NameKey("  Lietuvos  GYVENTOJŲ sąjunga ").ShouldBe(OrganisationRules.NameKey("lietuvos gyventojų Sąjunga"));
        OrganisationRules.CheckName(new string('a', 121)).ShouldBe("organisation.name-length");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("1234567", null)]
    [InlineData("123456789", null)]
    [InlineData("123456", "organisation.legal-code-invalid")]
    [InlineData("1234567890", "organisation.legal-code-invalid")]
    [InlineData("12345a7", "organisation.legal-code-invalid")]
    public void A_legal_entity_code_is_seven_to_nine_digits(string? code, string? error) =>
        OrganisationRules.CheckLegalCode(code).ShouldBe(error);

    [Theory]
    [InlineData(2, 3, null)]
    [InlineData(3, 3, "organisation.limit")]
    [InlineData(0, 1, null)]
    public void An_account_registers_at_most_the_settings_number_of_organisations(int registered, int limit, string? error) =>
        OrganisationRules.CheckRegistering(registered, limit).ShouldBe(error);

    [Theory]
    [InlineData("for", "Tekstas", null, 1000, null)]
    [InlineData("against", "Tekstas", "https://lgs.lt/pozicija", null, null)]
    [InlineData("remarks", "Tekstas", null, null, null)]
    [InlineData("remarks", "Tekstas", null, 10, "statement.goal-with-remarks")]
    [InlineData("for", "Tekstas", null, 0, "statement.goal-invalid")]
    [InlineData("for", "Tekstas", null, 1_000_001, "statement.goal-invalid")]
    [InlineData("for", "Tekstas", null, 1_000_000, null)]
    [InlineData("abstain", "Tekstas", null, null, "statement.stance-invalid")]
    [InlineData("for", "   ", null, null, "statement.empty")]
    [InlineData("for", "Tekstas", "http://lgs.lt", null, "statement.link-invalid")]
    [InlineData("for", "Tekstas", "javascript:alert(1)", null, "statement.link-invalid")]
    [InlineData("for", "Tekstas", "https://user@lgs.lt", null, "statement.link-invalid")]
    public void A_statement_has_a_stance_a_text_an_optional_https_link_and_a_goal_only_on_a_side(string stance, string text, string? link, int? goal, string? error) =>
        OrganisationRules.CheckStatement(stance, text, link, goal).ShouldBe(error);

    [Fact]
    public void A_statement_is_at_most_one_thousand_five_hundred_characters()
    {
        OrganisationRules.CheckStatement("for", new string('a', 1500), null, null).ShouldBeNull();
        OrganisationRules.CheckStatement("for", new string('a', 1501), null, null).ShouldBe("statement.too-long");
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("open", false, null)]
    [InlineData("closed", true, "statement.bill-not-open")]
    [InlineData("rejected", false, "statement.bill-not-open")]
    [InlineData(null, true, "statement.bill-not-open")]
    [InlineData("open", true, "statement.bill-not-open")]
    public void A_statement_is_taken_only_on_an_active_bill_whose_record_is_not_hidden_or_closed(string? record, bool finished, string? error) =>
        OrganisationRules.CheckBillOpen(record, finished).ShouldBe(error);

    [Theory]
    [InlineData("active", "published", true)]
    [InlineData("active", "pending", false)]
    [InlineData("active", "hidden", false)]
    [InlineData("active", "withdrawn", false)]
    [InlineData("blocked", "published", false)]
    [InlineData("closed", "published", false)]
    public void A_blocked_or_closed_organisations_statements_are_shown_nowhere(string organisation, string statement, bool shown) =>
        OrganisationRules.IsShown(organisation, statement).ShouldBe(shown);

    [Theory]
    [InlineData("for", 1000, 120, 80, 120)]
    [InlineData("against", 1000, 120, 80, 80)]
    [InlineData("remarks", null, 120, 80, null)]
    [InlineData("for", null, 120, 80, null)]
    public void The_goal_counts_the_participants_on_the_organisations_side(string stance, int? goal, int forCount, int against, int? progress) =>
        OrganisationRules.Progress(stance, goal, forCount, against).ShouldBe(progress);

    [Theory]
    [InlineData(1, null)]
    [InlineData(9, null)]
    [InlineData(10, "organisation.representatives-full")]
    public void An_organisation_has_at_most_ten_representatives(int now, string? error) =>
        OrganisationRules.CheckNewRepresentative(now).ShouldBe(error);

    [Theory]
    [InlineData("active", 1, null)]
    [InlineData("active", 2, "organisation.not-last")]
    [InlineData("blocked", 1, "organisation.blocked")]
    [InlineData("closed", 1, "organisation.closed")]
    public void Only_the_last_representative_closes_and_not_while_blocked(string status, int representatives, string? error)
    {
        OrganisationRules.CheckClosing(status, representatives).ShouldBe(error);
        OrganisationRules.CheckLeaving(1).ShouldBe("organisation.last-representative");
        OrganisationRules.CheckLeaving(2).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://lgs.lt", true)]
    [InlineData("https://www.lgs.lt/pozicija?id=1#a", true)]
    [InlineData("https://žalias-ratas.lt/", true)]
    [InlineData("http://lgs.lt", false)]
    [InlineData("https://localhost/", false)]
    [InlineData("https://192.0.2.1/", false)]
    [InlineData("https://lgs.lt@evil.example", false)]
    [InlineData("https://evil.example\\@lgs.lt", false)]
    [InlineData("https://lgs.lt/ a", false)]
    [InlineData("lgs.lt", false)]
    [InlineData("", false)]
    public void Links_users_give_are_https_on_a_named_host_without_user_info_in_the_module_and_the_contracts_alike(string link, bool accepted)
    {
        HttpsAddress.IsAcceptable(link, 500).ShouldBe(accepted);
        UserLink.IsAcceptable(link, 500).ShouldBe(accepted, "the forms and the endpoints apply the same rule");
    }

    [Fact]
    public void A_users_link_is_shown_with_its_host_without_www()
    {
        HttpsAddress.Host("https://www.lgs.lt/pozicija").ShouldBe("lgs.lt");
        UserLink.Host("https://www.lgs.lt/pozicija").ShouldBe("lgs.lt");
        HttpsAddress.IsAcceptable("https://lgs.lt/" + new string('a', 200), OrganisationRules.WebsiteMaxLength).ShouldBeFalse();
    }

    [Fact]
    public void A_confirmation_code_is_six_digits_kept_only_as_a_hash_and_matches_once_within_ten_minutes_and_five_attempts()
    {
        var now = DateTimeOffset.Parse("2026-11-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var id = Guid.CreateVersion7(now);
        var code = OrganisationCodeRules.Generate();
        code.Length.ShouldBe(6);
        code.ShouldAllBe(c => char.IsAsciiDigit(c));
        var row = new OrganisationCode { Id = id, CodeHash = OrganisationCodeRules.Hash(id, code), CreatedAt = now, ExpiresAt = now + OrganisationCodeRules.Lifetime };
        row.CodeHash.ShouldNotBe(System.Text.Encoding.UTF8.GetBytes(code));
        OrganisationCodeRules.Matches(row, code, now).ShouldBeTrue();
        OrganisationCodeRules.Matches(row, " " + code + " ", now).ShouldBeTrue();
        OrganisationCodeRules.Matches(row, code == "000000" ? "111111" : "000000", now).ShouldBeFalse();
        OrganisationCodeRules.Matches(row, code, now.AddMinutes(10)).ShouldBeFalse("expired");
        row.FailedAttempts = OrganisationCodeRules.MaxAttempts;
        OrganisationCodeRules.Matches(row, code, now).ShouldBeFalse("exhausted");
        row.FailedAttempts = 0;
        row.ConsumedAt = now;
        OrganisationCodeRules.Matches(row, code, now).ShouldBeFalse("used");
        OrganisationCodeRules.MayIssue(4, 9, 5, 10).ShouldBeTrue();
        OrganisationCodeRules.MayIssue(5, 9, 5, 10).ShouldBeFalse();
        OrganisationCodeRules.MayIssue(1, 10, 5, 10).ShouldBeFalse();
    }

    [Fact]
    public void A_one_time_link_works_once_within_seven_days()
    {
        var now = DateTimeOffset.UtcNow;
        var token = InviteTokens.New();
        token.Length.ShouldBeGreaterThanOrEqualTo(30);
        InviteTokens.Hash(token).ShouldBe(InviteTokens.Hash(token));
        InviteTokens.IsUsable(now + OrganisationRules.InviteLifetime, null, now).ShouldBeTrue();
        InviteTokens.IsUsable(now.AddSeconds(-1), null, now).ShouldBeFalse();
        InviteTokens.IsUsable(now.AddDays(1), now, now).ShouldBeFalse();
        OrganisationRules.InviteLifetime.ShouldBe(TimeSpan.FromDays(7));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(21, true)]
    [InlineData(101, true)]
    [InlineData(11, false)]
    [InlineData(111, false)]
    [InlineData(2, false)]
    [InlineData(1000, false)]
    public void The_goal_says_dalyvio_after_a_number_ending_in_one_but_not_eleven(int goal, bool singular)
    {
        OrganisationWords.GenitiveSingular(goal).ShouldBe(singular);
        OrganisationWords.GoalKey(goal).ShouldBe(singular ? "Org.Goal.One" : "Org.Goal.Many");
    }

    [Fact]
    public void The_label_says_exactly_how_an_organisation_was_confirmed()
    {
        OrganisationWords.LabelKey(new OrganisationLabelResponse(null, null)).ShouldBe("Org.Label.none");
        OrganisationWords.LabelKey(new OrganisationLabelResponse("domain", "lgs.lt")).ShouldBe("Org.Label.domain");
        OrganisationWords.LabelKey(new OrganisationLabelResponse("moderator", null)).ShouldBe("Org.Label.moderator");
    }
}
