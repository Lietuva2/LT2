using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>Design 5.6: links in emails start with the site's configured address; without one they stay paths.</summary>
public sealed class SiteLinksTests
{
    [Theory]
    [InlineData("https://lietuva2.lt", "/seimas/XVP-0412", "https://lietuva2.lt/seimas/XVP-0412")]
    [InlineData("https://lietuva2.lt/", "/sveiki#sekami", "https://lietuva2.lt/sveiki#sekami")]
    [InlineData("https://lietuva2.lt:443", "/suvestines/2026-11-16", "https://lietuva2.lt/suvestines/2026-11-16")]
    [InlineData("https://lt2.localhost:8443", "/seimas/balsavimai/1", "https://lt2.localhost:8443/seimas/balsavimai/1")]
    [InlineData(" https://Lietuva2.LT ", "/", "https://lietuva2.lt/")]
    public void A_path_gets_the_configured_address(string address, string path, string expected) =>
        SiteLinks.Absolute(address, path).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_an_address_the_path_stays(string? address) =>
        SiteLinks.Absolute(address, "/seimas/XVP-0412").ShouldBe("/seimas/XVP-0412");

    [Theory]
    [InlineData("//piktas.example/seimas")]
    [InlineData("https://piktas.example/seimas")]
    [InlineData("seimas/XVP-0412")]
    [InlineData("/\\piktas.example")]
    public void What_is_not_a_path_on_this_site_is_left_alone(string path) =>
        SiteLinks.Absolute("https://lietuva2.lt", path).ShouldBe(path);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://lietuva2.lt")]
    [InlineData("https://lt2.localhost:8443/")]
    public void Empty_or_an_https_host_is_a_valid_address(string? address) =>
        SiteLinks.IsValid(address).ShouldBeTrue();

    [Theory]
    [InlineData("http://lietuva2.lt")]
    [InlineData("lietuva2.lt")]
    [InlineData("https://lietuva2.lt/kelias")]
    [InlineData("https://lietuva2.lt/?a=1")]
    [InlineData("https://lietuva2.lt/#x")]
    [InlineData("https://kas@lietuva2.lt")]
    [InlineData("/seimas")]
    public void Anything_else_is_refused_and_never_used(string address)
    {
        SiteLinks.IsValid(address).ShouldBeFalse();
        SiteLinks.Absolute(address, "/seimas/XVP-0412").ShouldBe("/seimas/XVP-0412");
    }

    [Fact]
    public void The_follow_emails_carry_full_addresses_when_the_site_address_is_configured()
    {
        var localizer = new StringLocalizer<EmailTemplates>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));
        var templates = new FollowTemplates(localizer, Options.Create(new SiteOptions { Address = "https://lietuva2.lt" }));

        var planned = templates.Planned(new FollowPlannedEmail(
            "ruta@pavyzdys.test", "XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "adoption", new DateOnly(2026, 11, 19), "/seimas/XVP-0412", "/sveiki#sekami"));
        planned.TextBody.ShouldContain("Projektas: https://lietuva2.lt/seimas/XVP-0412");
        planned.TextBody.ShouldContain("https://lietuva2.lt/sveiki#sekami");

        var voted = templates.Voted(new FollowVotedEmail(
            "ruta@pavyzdys.test", "XVP-0412", "Pataisa", "adoption", new DateOnly(2026, 5, 19), false, 74, 44, 10, null, "/seimas/balsavimai/99100006", [], "/sveiki#sekami"));
        voted.TextBody.ShouldContain("https://lietuva2.lt/seimas/balsavimai/99100006");
    }
}
