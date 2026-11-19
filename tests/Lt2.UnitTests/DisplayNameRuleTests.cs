using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 6.2: changing the display name follows the registration's rule, as often as the member likes (owner, 2026-11-16);
/// only a different name is a change.
/// </summary>
public sealed class DisplayNameRuleTests
{
    [Fact]
    public void The_same_name_again_is_no_change()
    {
        ProfileRules.IsChange("Rūta M.", " Rūta M. ").ShouldBeFalse();
        ProfileRules.IsChange(null, "   ").ShouldBeFalse("empty stays empty");
        ProfileRules.IsChange("Rūta M.", "Rūta Mockutė").ShouldBeTrue();
        ProfileRules.IsChange("Rūta M.", "").ShouldBeTrue("removing the name is a change");
        ProfileRules.IsChange("rūta m.", "Rūta M.").ShouldBeTrue("case is part of the name shown");
    }

    [Fact]
    public void A_changed_name_follows_the_registration_rule()
    {
        ProfileRules.CheckDisplayName(new string('a', 60)).ShouldBeNull();
        ProfileRules.CheckDisplayName(new string('a', 61)).ShouldBe("display-name.too-long");
        ProfileRules.CheckDisplayName("").ShouldBeNull("empty is allowed, as at registration");
        ProfileRules.DisplayName("  Rūta M.  ").ShouldBe("Rūta M.");
    }
}
