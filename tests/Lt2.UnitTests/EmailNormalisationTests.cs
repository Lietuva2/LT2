using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 6.2: one account per normalised address — lower case, +tag removed, dots ignored where the provider ignores them.</summary>
public sealed class EmailNormalisationTests
{
    [Theory]
    [InlineData("Jonas.Petraitis@Pavyzdys.TEST", "jonas.petraitis@pavyzdys.test")]
    [InlineData("  jonas.petraitis@pavyzdys.test  ", "jonas.petraitis@pavyzdys.test")]
    public void Addresses_are_compared_in_lower_case_without_surrounding_spaces(string input, string expected) =>
        EmailAddress.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("jonas.petraitis+seimas@pavyzdys.test", "jonas.petraitis@pavyzdys.test")]
    [InlineData("jonas.petraitis+a+b@pavyzdys.test", "jonas.petraitis@pavyzdys.test")]
    public void The_plus_tag_is_removed(string input, string expected) =>
        EmailAddress.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("Jonas.Petraitis@gmail.com", "jonaspetraitis@gmail.com")]
    [InlineData("j.o.n.a.s.petraitis+x@gmail.com", "jonaspetraitis@gmail.com")]
    [InlineData("jonas.petraitis@googlemail.com", "jonaspetraitis@gmail.com")]
    public void Dots_are_ignored_where_the_provider_ignores_them(string input, string expected) =>
        EmailAddress.Normalize(input).ShouldBe(expected);

    [Fact]
    public void Dots_are_kept_for_other_providers() =>
        EmailAddress.Normalize("jonas.petraitis@pavyzdys.test").ShouldBe("jonas.petraitis@pavyzdys.test");

    [Fact]
    public void Two_spellings_of_one_gmail_address_give_one_key() =>
        EmailAddress.Normalize("Jonas.Petraitis+lt2@gmail.com").ShouldBe(EmailAddress.Normalize("jonaspetraitis@googlemail.com"));

    [Fact]
    public void Internationalised_domains_are_compared_in_their_ascii_form() =>
        EmailAddress.Normalize("jonas@ŽĄSIS.test").ShouldBe("jonas@" + new System.Globalization.IdnMapping().GetAscii("žąsis.test"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jonas")]
    [InlineData("@pavyzdys.test")]
    [InlineData("jonas@")]
    [InlineData("+tag@pavyzdys.test")]
    public void Unusable_input_has_no_key(string? input) =>
        EmailAddress.Normalize(input).ShouldBeNull();

    [Theory]
    [InlineData("jonas@mailinator.com")]
    [InlineData("jonas@yopmail.com")]
    [InlineData("jonas@sub.guerrillamail.com")]
    public void Disposable_mailbox_domains_are_recognised(string input) =>
        DisposableDomains.Contains(EmailAddress.Normalize(input)!).ShouldBeTrue();

    [Fact]
    public void Ordinary_domains_are_not_disposable() =>
        DisposableDomains.Contains("jonas@pavyzdys.test").ShouldBeFalse();
}
