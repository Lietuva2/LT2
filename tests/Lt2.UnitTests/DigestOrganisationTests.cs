using Lt2.Notifications;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.14.4, 8.14.9 (owner, 2026-11-20): the organisations' line under a bill in a summary to MPs. Who is named
/// (<see cref="DigestOrganisationRules"/>) and how the email says it: names only, each a link to its statement on the bill's
/// page, never the statement's text; nothing when none is named or the snapshot predates the line.
/// </summary>
public sealed class DigestOrganisationTests
{
    private static readonly DateTimeOffset Prepared = new(2026, 11, 15, 5, 0, 0, TimeSpan.Zero);
    private static int _n;

    private static DigestStatementFacts Statement(
        string name, string stance = "for", string statementStatus = "published", string organisationStatus = "active", bool confirmed = true,
        bool keptOut = false, DateTimeOffset? firstPublicAt = null, DateTimeOffset? publishedAt = null, string bill = "XVP-0412")
    {
        var n = Interlocked.Increment(ref _n);
        var at = publishedAt ?? Prepared.AddDays(-n);
        return new DigestStatementFacts(
            Guid.CreateVersion7(at), Guid.CreateVersion7(at), "org-" + n, name, bill, stance, statementStatus, organisationStatus, confirmed, keptOut,
            firstPublicAt ?? at, at);
    }

    [Fact]
    public void A_confirmed_active_organisations_statement_public_when_the_summary_was_prepared_is_named()
    {
        var s = Statement("Pavyzdinė mokesčių aiškumo asociacija");
        DigestOrganisationRules.Named(s, Prepared).ShouldBeTrue();
        DigestOrganisationRules.Line([s], Prepared).Single().ShouldBe(new DigestOrganisation("for", s.OrganisationId, s.Slug, s.Name, s.StatementId));
    }

    [Fact]
    public void An_unconfirmed_organisation_is_not_named() =>
        DigestOrganisationRules.Named(Statement("Nepatvirtinta", confirmed: false), Prepared).ShouldBeFalse();

    [Theory]
    [InlineData("blocked")]
    [InlineData("closed")]
    public void A_blocked_or_closed_organisation_is_not_named(string status) =>
        DigestOrganisationRules.Named(Statement("Užblokuota", organisationStatus: status), Prepared).ShouldBeFalse();

    [Fact]
    public void An_organisation_a_moderator_keeps_out_of_summaries_is_not_named() =>
        DigestOrganisationRules.Named(Statement("Neįtraukiama", keptOut: true), Prepared).ShouldBeFalse();

    [Theory]
    [InlineData("pending")]
    [InlineData("hidden")]
    [InlineData("withdrawn")]
    public void A_statement_not_published_now_is_not_named(string status) =>
        DigestOrganisationRules.Named(Statement("Pavyzdinė", statementStatus: status), Prepared).ShouldBeFalse();

    [Fact]
    public void A_statement_first_public_after_the_summary_was_prepared_is_not_named_one_public_at_that_moment_is()
    {
        DigestOrganisationRules.Named(Statement("Vėliau", firstPublicAt: Prepared.AddSeconds(1)), Prepared).ShouldBeFalse();
        DigestOrganisationRules.Named(Statement("Tuo metu", firstPublicAt: Prepared), Prepared).ShouldBeTrue();
        DigestOrganisationRules.Named(Statement("Niekada", firstPublicAt: null) with { FirstPublicAt = null }, Prepared).ShouldBeFalse();
    }

    [Fact]
    public void The_line_is_for_then_against_then_remarks_each_in_the_order_published()
    {
        var remarks = Statement("C pastabos", "remarks", publishedAt: Prepared.AddDays(-9));
        var againstLate = Statement("B prieš vėlesnė", "against", publishedAt: Prepared.AddDays(-1));
        var forLate = Statement("A už vėlesnė", "for", publishedAt: Prepared.AddDays(-2));
        var againstEarly = Statement("Ž prieš ankstesnė", "against", publishedAt: Prepared.AddDays(-5));
        var forEarly = Statement("Ž už ankstesnė", "for", publishedAt: Prepared.AddDays(-6));

        DigestOrganisationRules.Line([remarks, againstLate, forLate, againstEarly, forEarly], Prepared).Select(o => o.Name)
            .ShouldBe(["Ž už ankstesnė", "A už vėlesnė", "Ž prieš ankstesnė", "B prieš vėlesnė", "C pastabos"]);
    }

    [Fact]
    public void Lines_are_per_bill_compared_without_case_and_a_bill_with_none_named_is_missing()
    {
        var lines = DigestOrganisationRules.ByBill(
            [Statement("Pirma", bill: "XVP-0412"), Statement("Nepatvirtinta", bill: "XVP-0413", confirmed: false)], Prepared);
        lines.Keys.ShouldBe(["XVP-0412"]);
        lines["xvp-0412"].Single().Name.ShouldBe("Pirma");
    }

    [Theory]
    [InlineData("active", false, true)]
    [InlineData("blocked", false, true)]
    [InlineData("closed", false, false)]
    [InlineData("active", true, false)]
    public void A_moderator_keeps_out_of_summaries_an_organisation_that_is_not_closed_and_not_kept_out_already(string status, bool keptOut, bool allowed) =>
        (Lt2.Appeals.Domain.OrganisationRules.CheckKeepOut(status, keptOut) is null).ShouldBe(allowed);

    [Theory]
    [InlineData("active", true, true)]
    [InlineData("blocked", true, true)]
    [InlineData("closed", true, false)]
    [InlineData("active", false, false)]
    public void Only_an_organisation_kept_out_of_summaries_is_taken_back_in(string status, bool keptOut, bool allowed) =>
        (Lt2.Appeals.Domain.OrganisationRules.CheckKeepIn(status, keptOut) is null).ShouldBe(allowed);

    private static MpMailTemplates Templates() => new(
        new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)),
        Options.Create(new SiteOptions { Address = "https://lt2.test" }));

    private static readonly Guid StatementA = Guid.Parse("0193a1b2-0000-7000-8000-0000000000d1");
    private static readonly Guid StatementB = Guid.Parse("0193a1b2-0000-7000-8000-0000000000d2");
    private static readonly Guid StatementC = Guid.Parse("0193a1b2-0000-7000-8000-0000000000d3");

    private static DigestOrganisation Named(string stance, string name, Guid statement) => new(stance, Guid.NewGuid(), "slug", name, statement);

    private static EmailMessage Email(IReadOnlyList<DigestOrganisation>? organisations)
    {
        var monday = new DateOnly(2026, 11, 16);
        var bill = new MpDigestBill(
            new DigestItemInfo(
                "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "adoption", monday.AddDays(3), 6800, 6150, 4280, 3900, null, null,
                [new DigestReason("for", "Gynyba finansuojama nekeliant PVM.", 1310), new DigestReason("against", "Skatina keisti rezidavimo vietą.", 970)],
                Organisations: organisations),
            null,
            null);
        return Templates().Digest(new MpDigestEmail(
            "pavyzdys@lrs.lt", "official", "Ona Jonaitienė", "weekly", monday, [bill], MpMailPaths.PublicCopy(monday),
            MpMailPaths.Preferences("tokenas"), MpMailPaths.OneClick("tokenas"), null, null));
    }

    [Fact]
    public void With_no_organisation_named_the_email_has_no_line()
    {
        foreach (var email in new[] { Email([]), Email(null) })
        {
            email.TextBody.ShouldNotContain("Organizacijos, prašančios");
            email.TextBody.ShouldNotContain("Pastabų atsiuntė");
            email.TextBody.ShouldNotContain("#statement-");
            email.HtmlBody!.ShouldNotContain("#statement-");
        }
    }

    [Fact]
    public void One_organisation_is_named_with_a_link_to_its_statement_on_the_bills_page()
    {
        var email = Email([Named("against", "Pavyzdinis darbdavių forumas", StatementA)]);
        var link = "https://lt2.test/seimas/XVP-0412#statement-" + StatementA;

        email.TextBody.ShouldContain("Organizacijos, prašančios balsuoti prieš: Pavyzdinis darbdavių forumas.");
        email.TextBody.ShouldContain("Pavyzdinis darbdavių forumas: " + link);
        email.TextBody.ShouldNotContain("prašančios balsuoti už:");
        email.TextBody.ShouldNotContain("Pastabų atsiuntė");
        email.HtmlBody.ShouldNotBeNull().ShouldContain("<a href=\"" + link + "\" style=\"color:#1e293b\">Pavyzdinis darbdavių forumas</a>.");
    }

    [Fact]
    public void Several_organisations_are_named_by_stance_after_the_arguments_and_before_the_answer()
    {
        var email = Email(
        [
            Named("for", "Pavyzdinė mokesčių aiškumo asociacija", StatementA),
            Named("for", "Pavyzdinė dirbančiųjų bendrija", StatementB),
            Named("remarks", "Pavyzdinė viešųjų finansų stebėsena", StatementC),
        ]);
        var text = email.TextBody;

        text.ShouldContain("Organizacijos, prašančios balsuoti už: Pavyzdinė mokesčių aiškumo asociacija, Pavyzdinė dirbančiųjų bendrija.");
        text.ShouldContain("Pastabų atsiuntė: Pavyzdinė viešųjų finansų stebėsena.");
        text.ShouldNotContain("prašančios balsuoti prieš:");
        text.IndexOf("Skatina keisti rezidavimo vietą.", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("Organizacijos, prašančios", StringComparison.Ordinal));
        text.IndexOf("prašančios balsuoti už:", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("Pastabų atsiuntė", StringComparison.Ordinal));
        text.IndexOf("Pastabų atsiuntė", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("Atsakyti", StringComparison.Ordinal));
        foreach (var statement in new[] { StatementA, StatementB, StatementC })
        {
            text.ShouldContain("https://lt2.test/seimas/XVP-0412#statement-" + statement);
            email.HtmlBody!.ShouldContain("https://lt2.test/seimas/XVP-0412#statement-" + statement);
        }

        email.HtmlBody!.ShouldContain("Pavyzdinė mokesčių aiškumo asociacija</a>, <a href=");
    }
}
