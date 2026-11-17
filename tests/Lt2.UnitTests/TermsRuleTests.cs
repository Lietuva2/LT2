using Lt2.Api.Contracts.Me;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.22.2: which accepted version of the terms of use still lets a member write, which writes need no acceptance, and
/// where the acceptance screen goes back to.
/// </summary>
public sealed class TermsRuleTests
{
    [Fact]
    public void A_member_who_never_accepted_the_terms_must_accept_them()
    {
        TermsOfUse.NeedsAcceptance(null).ShouldBeTrue();
        TermsOfUse.NeedsAcceptance("").ShouldBeTrue();
    }

    [Fact]
    public void A_member_who_accepted_an_earlier_version_must_accept_the_one_in_force() =>
        TermsOfUse.NeedsAcceptance("2026-11-15", current: "2026-11-16").ShouldBeTrue();

    [Fact]
    public void A_member_who_accepted_the_version_in_force_need_not_accept_again()
    {
        TermsOfUse.NeedsAcceptance(TermsOfUse.Version).ShouldBeFalse();
        TermsOfUse.NeedsAcceptance("2026-11-16", current: "2026-11-16").ShouldBeFalse();
    }

    [Fact]
    public void A_version_later_than_the_one_in_force_counts_as_accepted() =>
        TermsOfUse.NeedsAcceptance("2027-01-01", current: "2026-11-16").ShouldBeFalse();

    [Fact]
    public void Versions_are_dates_and_compare_as_dates()
    {
        TermsOfUse.NeedsAcceptance("2026-09-30", current: "2026-10-01").ShouldBeTrue();
        DateOnly.TryParseExact(TermsOfUse.Version, "yyyy-MM-dd", out _).ShouldBeTrue("the version is the day it applies from");
    }

    [Fact]
    public void The_browser_accepts_the_version_the_server_holds_in_force() =>
        TermsText.Version.ShouldBe(TermsOfUse.Version);

    [Fact]
    public void The_acceptance_screen_goes_back_only_to_a_path_on_this_site()
    {
        TermsText.Back("/seimas/XVP-0412").ShouldBe("/seimas/XVP-0412");
        TermsText.Back("/paskyra?x=1").ShouldBe("/paskyra?x=1");
        TermsText.Back("https://kitas.pavyzdys.test/").ShouldBeNull();
        TermsText.Back("//kitas.pavyzdys.test").ShouldBeNull();
        TermsText.Back("/\\kitas.pavyzdys.test").ShouldBeNull();
        TermsText.Back("seimas").ShouldBeNull();
        TermsText.Back(null).ShouldBeNull();
        TermsText.Back(TermsText.AcceptancePage).ShouldBeNull("never back to itself");
    }

    /// <summary>Design 6.2, 8.21: the staff passkey page goes back the same way, after the "#", never to itself.</summary>
    [Fact]
    public void The_staff_passkey_page_goes_back_only_to_a_path_on_this_site_other_than_itself()
    {
        StaffSessionPage.Link("/moderavimas").ShouldBe("/prisijungti/raktas#grizti=%2Fmoderavimas");
        StaffSessionPage.Link("/projektai?rikiuoti=dalyviai").ShouldBe("/prisijungti/raktas#grizti=%2Fprojektai%3Frikiuoti%3Ddalyviai");
        StaffSessionPage.Back("/seimas/XVP-0412").ShouldBe("/seimas/XVP-0412");
        StaffSessionPage.Back("https://kitas.pavyzdys.test/").ShouldBeNull();
        StaffSessionPage.Back("//kitas.pavyzdys.test").ShouldBeNull();
        StaffSessionPage.Back("/\\kitas.pavyzdys.test").ShouldBeNull();
        StaffSessionPage.Back(StaffSessionPage.Path).ShouldBeNull("never back to itself");
        StaffSessionPage.Link("https://kitas.pavyzdys.test/").ShouldBe(StaffSessionPage.Path);
        StaffSessionPage.Back(InvitationLink.FromFragment(StaffSessionPage.Link("/moderavimas/paslepti"), StaffSessionPage.BackParameter))
            .ShouldBe("/moderavimas/paslepti", "what the link carries, the page reads back");
    }

    [Fact]
    public void The_acceptance_link_carries_the_page_to_go_back_to()
    {
        TermsText.AcceptanceLink("/seimas/XVP-0412").ShouldBe("/taisykles/sutikimas?grizti=%2Fseimas%2FXVP-0412");
        TermsText.AcceptanceLink("https://kitas.pavyzdys.test/").ShouldBe("/taisykles/sutikimas");
    }
}

/// <summary>
/// Design 8.22.2: the writes an account that has not accepted the terms may still make, listed in one place; every other
/// write is refused by the web host's TermsGate, so a new route is covered without being listed.
/// </summary>
public sealed class TermsExemptionTests
{
    [Fact]
    public void Only_accepting_signing_out_deleting_withdrawals_and_the_mail_links_are_exempt() =>
        TermsExemptions.Routes.Select(r => $"{r.Method} {r.Route}").Order(StringComparer.Ordinal).ShouldBe(
        [
            "DELETE /api/v1/me",
            "DELETE /api/v1/me/consents/positions",
            "DELETE /api/v1/me/party-support",
            "DELETE /api/v1/me/party-support/public",
            "DELETE /api/v1/me/profile/public",
            "DELETE /api/v1/mp-mail/{token}/extra",
            "POST /api/v1/auth/sign-out",
            "POST /api/v1/mp-mail/extra/confirm",
            "POST /api/v1/mp-mail/extra/stop",
            "POST /api/v1/mp-mail/{token}/renew",
            "POST /api/v1/mp-mail/{token}/stop",
            "POST /api/v1/notices/{token}/stop",
            "PUT /api/v1/me/consents/terms",
            "PUT /api/v1/mp-mail/{token}",
            "PUT /api/v1/mp-mail/{token}/extra",
        ]);

    [Theory]
    [InlineData("PUT", "/api/v1/me/consents/terms")]
    [InlineData("put", "api/v1/me/consents/terms")]
    [InlineData("POST", "/api/v1/auth/sign-out")]
    [InlineData("DELETE", "/api/v1/me")]
    public void An_exempt_route_is_recognised_however_the_route_pattern_writes_it(string method, string route) =>
        TermsExemptions.IsExempt(method, route).ShouldBeTrue();

    [Theory]
    [InlineData("PUT", "/api/v1/bills/{number}/record/position")]
    [InlineData("POST", "/api/v1/bills/{number}/record/reasons")]
    [InlineData("PUT", "/api/v1/me/consents/positions")]
    [InlineData("POST", "/api/v1/feedback")]
    [InlineData("POST", "/api/v1/naujas/marsrutas")]
    [InlineData("GET", "/api/v1/me/consents/terms")]
    [InlineData("POST", "/api/v1/me")]
    public void Any_other_write_is_not_exempt(string method, string route) =>
        TermsExemptions.IsExempt(method, route).ShouldBeFalse();

    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    public void Reads_are_never_refused_and_everything_else_is_a_write(string method, bool write) =>
        TermsExemptions.IsWrite(method).ShouldBe(write);
}
