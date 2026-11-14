using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals.Domain;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.13 (decided by the owner on 2026-11-14): the official text's address a moderator records with a description is
/// empty, or an https address whose host is lrs.lt or under it, with no user name or password, at most 500 characters. The
/// form, the endpoint's validator and the module apply the same rule.
/// </summary>
public sealed class OfficialTextAddressTests
{
    private static readonly IStringLocalizer<Lt2.Api.Contracts.ValidationMessages> L = new StringLocalizer<Lt2.Api.Contracts.ValidationMessages>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    public static TheoryData<string?, bool> Addresses => new()
    {
        { null, true },
        { "", true },
        { "   ", true },
        { "https://lrs.lt", true },
        { "https://lrs.lt/", true },
        { "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas", true },
        { "https://www.lrs.lt/sip/portal.show?p_r=35403&p_k=1", true },
        { "HTTPS://E-SEIMAS.LRS.LT/portal/legalAct/lt/TAP/bandymas", true },
        { "  https://e-seimas.lrs.lt/bandymas  ", true },
        { "https://e-seimas.lrs.lt:443/bandymas", true },
        { "https://e-seimas.lrs.lt/bandymas#dalis", true },
        { "http://e-seimas.lrs.lt/bandymas", false },
        { "ftp://lrs.lt/bandymas", false },
        { "javascript:alert(1)", false },
        { "//e-seimas.lrs.lt/bandymas", false },
        { "e-seimas.lrs.lt/bandymas", false },
        { "https:e-seimas.lrs.lt/bandymas", false },
        { "https://evil-lrs.lt/", false },
        { "https://lrs.lt.evil.example/", false },
        { "https://lrs.lt@evil.example/", false },
        { "https://user@e-seimas.lrs.lt/", false },
        { "https://user:slaptas@e-seimas.lrs.lt/", false },
        { "https://@e-seimas.lrs.lt/", false },
        { "https://evil.example/?u=lrs.lt", false },
        { "https://evil.example/lrs.lt", false },
        { "https://evil.example\\@e-seimas.lrs.lt/", false },
        { "https://e-seimas.lrs.lt/ bandymas", false },
        { "https://lrs.lt./", false },
        { "https://xlrs.lt/", false },
        { "https://", false },
        { "nuoroda", false },
    };

    [Theory]
    [MemberData(nameof(Addresses))]
    public void Only_an_https_address_on_lrs_lt_or_under_it_without_user_info_is_accepted(string? address, bool accepted)
    {
        OfficialTextAddress.IsAcceptable(address).ShouldBe(accepted);
        (RecordRules.CheckDescriptionUrl(address) is null).ShouldBe(accepted, "the module applies the same rule as the form and the endpoint");
    }

    [Fact]
    public void At_most_500_characters()
    {
        var prefix = "https://e-seimas.lrs.lt/";
        var longest = prefix + new string('a', 500 - prefix.Length);
        OfficialTextAddress.IsAcceptable(longest).ShouldBeTrue();
        OfficialTextAddress.IsAcceptable(longest + "a").ShouldBeFalse();
        RecordRules.CheckDescriptionUrl(longest).ShouldBeNull();
        RecordRules.CheckDescriptionUrl(longest + "a").ShouldBe("description-url.invalid");
        AppealLimits.DescriptionUrlMaxLength.ShouldBe(RecordRules.DescriptionUrlMaxLength);
    }

    [Fact]
    public void An_empty_field_clears_the_address_and_a_filled_one_is_stored_trimmed()
    {
        OfficialTextAddress.Normalise("  ").ShouldBeNull();
        OfficialTextAddress.Normalise(" https://lrs.lt/a ").ShouldBe("https://lrs.lt/a");
        RecordRules.NormaliseDescriptionUrl("").ShouldBeNull();
        RecordRules.NormaliseDescriptionUrl(" https://lrs.lt/a ").ShouldBe("https://lrs.lt/a");
    }

    [Fact]
    public void The_form_says_why_an_address_is_refused_at_its_field()
    {
        var validator = new ModeratorDescriptionRequestValidator(L);
        var refused = validator.Validate(new ModeratorDescriptionRequest
        {
            Description = "Projektas keičia rinkliavas.", Statement = "Pridėtas adresas.", DescriptionUrl = "https://lrs.lt.evil.example/",
        });
        var error = refused.Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe("descriptionUrl");
        error.ErrorMessage.ShouldBe("Nurodykite oficialaus teksto adresą lrs.lt svetainėje, prasidedantį https://, arba palikite laukelį tuščią.");

        validator.Validate(new ModeratorDescriptionRequest { Description = "Projektas keičia rinkliavas.", Statement = "Bandymas." })
            .IsValid.ShouldBeTrue("the address is optional");
    }

    [Fact]
    public void Writing_the_description_may_change_the_text_or_only_its_address()
    {
        RecordRules.CheckDescriptionWrite("open", "Tekstas.", "Tekstas.", "Bandymas.", "https://lrs.lt/a", "https://lrs.lt/a").ShouldBe("description.unchanged");
        RecordRules.CheckDescriptionWrite("open", "Tekstas.", "Tekstas.", "Bandymas.", null, "https://lrs.lt/a").ShouldBeNull();
        RecordRules.CheckDescriptionWrite("open", "Tekstas.", "Tekstas.", "Bandymas.", "https://lrs.lt/a", null).ShouldBeNull();
        RecordRules.CheckDescriptionWrite("open", "Tekstas.", "Kitas tekstas.", "Bandymas.", "https://lrs.lt/a", "https://lrs.lt/a").ShouldBeNull();
    }
}
