using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// The one slug of public addresses (MPs' <c>/p/{slug}</c>, members' <c>/nariai/{slug}-{id}</c>, design 3, 8.25.2) and a
/// member's address (owner, 2026-11-16): the id decides, the name part comes from the current display name.
/// </summary>
public sealed class AddressSlugTests
{
    [Theory]
    [InlineData("Jonas Petraitis", "jonas-petraitis")]
    [InlineData("Žydrūnė Ąžuolaitė-Čiurlionienė", "zydrune-azuolaite-ciurlioniene")]
    [InlineData("  Ona   Jonaitienė  ", "ona-jonaitiene")]
    [InlineData("Rūta M.", "ruta-m")]
    [InlineData("Agentė 007", "agente-007")]
    [InlineData("☆ … ☆", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_name_becomes_plain_latin_lower_case_words_joined_by_hyphens(string? name, string slug) =>
        AddressSlugs.Of(name).ShouldBe(slug);

    [Fact]
    public void A_members_address_is_the_name_part_and_the_id_or_the_id_alone()
    {
        MemberAddress.Page("abcdefgh23", "Rūta Mockutė").ShouldBe("/nariai/ruta-mockute-abcdefgh23");
        MemberAddress.Page("abcdefgh23", "☆").ShouldBe("/nariai/abcdefgh23");
        MemberAddress.Page("abcdefgh23", null).ShouldBe("/nariai/abcdefgh23");
        MemberAddress.Page("abcdefgh23", "Jonas Petraitis").ShouldNotBe(MemberAddress.Page("zxcvbnmk45", "Jonas Petraitis"));
    }

    [Theory]
    [InlineData("ruta-mockute-abcdefgh23", "abcdefgh23")]
    [InlineData("abcdefgh23", "abcdefgh23")]
    [InlineData("kitas-vardas-abcdefgh23", "abcdefgh23")]
    public void The_id_is_what_follows_the_last_hyphen(string segment, string id) => MemberAddress.IdOf(segment).ShouldBe(id);

    [Fact]
    public void The_contracts_and_the_shared_kernel_compile_the_same_rule() =>
        Lt2.Api.Contracts.MemberAddress.Page("abcdefgh23", "Žydrūnė Ą.").ShouldBe(MemberAddress.Page("abcdefgh23", "Žydrūnė Ą."));
}
