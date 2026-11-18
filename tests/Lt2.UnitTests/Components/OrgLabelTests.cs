using Bunit;
using Lt2.Api.Contracts.Organisations;
using Lt2.Web.Client;
using Lt2.Web.Client.Organisations;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.14.2 (owner, 2026-11-18): a confirmed organisation's chip reads "Patvirtinta" and says how in a hint (the browser's
/// popover, no script needed); without an id the whole text is its title and for screen readers; "Nepatvirtinta" stays.
/// </summary>
public sealed class OrgLabelTests : BunitContext
{
    public OrgLabelTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
    }

    [Fact]
    public void A_confirmed_chip_says_patvirtinta_and_opens_how_in_a_hint()
    {
        var label = Render<OrgLabel>(p => p.Add(c => c.Label, new OrganisationLabelResponse("domain", "mokesciai.pavyzdys.test")).Add(c => c.Id, "org-label"));

        label.Find("#org-label").TextContent.ShouldBe("Patvirtinta");
        label.Find("#org-label").GetAttribute("popovertarget").ShouldBe("org-label-panel");
        label.Find("#org-label-panel").GetAttribute("popover").ShouldBe("auto");
        label.Find("#org-label-panel .hint__answer").TextContent.ShouldBe("Patvirtinta per el. pašto adresą @mokesciai.pavyzdys.test");

        var moderator = Render<OrgLabel>(p => p.Add(c => c.Label, new OrganisationLabelResponse("moderator", null)).Add(c => c.Id, "org-label-2"));
        moderator.Find("#org-label-2").TextContent.ShouldBe("Patvirtinta");
        moderator.Find("#org-label-2-panel .hint__answer").TextContent.ShouldBe("Patvirtino moderatorius");
    }

    [Fact]
    public void Without_an_id_the_whole_label_is_the_title_and_for_screen_readers_and_nepatvirtinta_stays()
    {
        var label = Render<OrgLabel>(p => p.Add(c => c.Label, new OrganisationLabelResponse("domain", "mokesciai.pavyzdys.test")));
        var chip = label.Find("[data-org-label=domain]");
        chip.GetAttribute("title").ShouldBe("Patvirtinta per el. pašto adresą @mokesciai.pavyzdys.test");
        chip.QuerySelector(".visually-hidden")!.TextContent.ShouldBe("Patvirtinta per el. pašto adresą @mokesciai.pavyzdys.test");
        chip.QuerySelector("[aria-hidden=true]")!.TextContent.ShouldBe("Patvirtinta");

        Render<OrgLabel>(p => p.Add(c => c.Label, new OrganisationLabelResponse(null, null))).Find("[data-org-label=none]").TextContent.ShouldBe("Nepatvirtinta");
    }
}
