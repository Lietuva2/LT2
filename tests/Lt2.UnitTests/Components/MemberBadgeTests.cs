using Bunit;
using Lt2.Web.Client.Components;

namespace Lt2.UnitTests.Components;

/// <summary>Design 8.25.4 (owner, 2026-11-19): the initials badge beside a name that leads to a public profile.</summary>
public sealed class MemberBadgeTests : BunitContext
{
    [Fact]
    public void A_member_with_a_public_profile_has_a_badge_with_the_initials_hidden_from_assistive_technology()
    {
        var badge = Render<MemberBadge>(p => p.Add(c => c.ProfileId, "7k3qm2x9ab").Add(c => c.Name, "Rūta Mockutė"));
        var span = badge.Find("span.initials");
        span.TextContent.ShouldBe("RM");
        span.GetAttribute("aria-hidden").ShouldBe("true");
        span.ClassList.ShouldContain($"initials--{MemberBadges.Colour("7k3qm2x9ab")}");
        badge.FindAll("img").ShouldBeEmpty("no picture, only letters");
    }

    [Fact]
    public void A_member_without_a_public_profile_has_no_badge()
    {
        Render<MemberBadge>(p => p.Add(c => c.ProfileId, null).Add(c => c.Name, "Rūta Mockutė")).Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void A_name_with_no_letter_has_no_badge()
    {
        Render<MemberBadge>(p => p.Add(c => c.ProfileId, "7k3qm2x9ab").Add(c => c.Name, "1234")).Markup.Trim().ShouldBeEmpty();
        Render<MemberBadge>(p => p.Add(c => c.ProfileId, "7k3qm2x9ab").Add(c => c.Name, null)).Markup.Trim().ShouldBeEmpty();
    }
}
