using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 6.2: one account per normalised address — lower case, +tag removed, dots ignored where the provider ignores them.</summary>
public sealed class EmailNormalisationTests
{
    [Theory]
    [InlineData("Ruta.Mockute@Pavyzdys.TEST", "ruta.mockute@pavyzdys.test")]
    [InlineData("  ruta.mockute@pavyzdys.test  ", "ruta.mockute@pavyzdys.test")]
    public void Addresses_are_compared_in_lower_case_without_surrounding_spaces(string input, string expected) =>
        EmailAddress.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("ruta.mockute+seimas@pavyzdys.test", "ruta.mockute@pavyzdys.test")]
    [InlineData("ruta.mockute+a+b@pavyzdys.test", "ruta.mockute@pavyzdys.test")]
    public void The_plus_tag_is_removed(string input, string expected) =>
        EmailAddress.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("Ruta.Mockute@gmail.com", "rutamockute@gmail.com")]
    [InlineData("r.u.t.a.mockute+x@gmail.com", "rutamockute@gmail.com")]
    [InlineData("ruta.mockute@googlemail.com", "rutamockute@gmail.com")]
    public void Dots_are_ignored_where_the_provider_ignores_them(string input, string expected) =>
        EmailAddress.Normalize(input).ShouldBe(expected);

    [Fact]
    public void Dots_are_kept_for_other_providers() =>
        EmailAddress.Normalize("ruta.mockute@pavyzdys.test").ShouldBe("ruta.mockute@pavyzdys.test");

    [Fact]
    public void Two_spellings_of_one_gmail_address_give_one_key() =>
        EmailAddress.Normalize("Ruta.Mockute+lt2@gmail.com").ShouldBe(EmailAddress.Normalize("rutamockute@googlemail.com"));

    [Fact]
    public void Internationalised_domains_are_compared_in_their_ascii_form() =>
        EmailAddress.Normalize("ruta@ŽĄSIS.test").ShouldBe("ruta@" + new System.Globalization.IdnMapping().GetAscii("žąsis.test"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ruta")]
    [InlineData("@pavyzdys.test")]
    [InlineData("ruta@")]
    [InlineData("+tag@pavyzdys.test")]
    public void Unusable_input_has_no_key(string? input) =>
        EmailAddress.Normalize(input).ShouldBeNull();

    [Theory]
    [InlineData("ruta@mailinator.com")]
    [InlineData("ruta@yopmail.com")]
    [InlineData("ruta@sub.guerrillamail.com")]
    public void Disposable_mailbox_domains_are_recognised(string input) =>
        DisposableDomains.Contains(EmailAddress.Normalize(input)!).ShouldBeTrue();

    [Fact]
    public void Ordinary_domains_are_not_disposable() =>
        DisposableDomains.Contains("ruta@pavyzdys.test").ShouldBeFalse();
}
