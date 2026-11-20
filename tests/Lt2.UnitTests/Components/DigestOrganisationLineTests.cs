using Bunit;
using Lt2.Api.Contracts.Digests;
using Lt2.Web.Client;
using Lt2.Web.Client.Organisations;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.14.4: the organisations' line under a bill on a summary's public copy and the moderators' screen, as in the MPs'
/// email: one sentence per stance with the names, each a link to its statement on the bill's page; nothing for a summary
/// without one or a snapshot from before the line was kept.
/// </summary>
public sealed class DigestOrganisationLineTests : BunitContext
{
    private static readonly Guid For1 = Guid.Parse("0193a1b2-0000-7000-8000-0000000000d1");
    private static readonly Guid For2 = Guid.Parse("0193a1b2-0000-7000-8000-0000000000d2");
    private static readonly Guid Remarks = Guid.Parse("0193a1b2-0000-7000-8000-0000000000d4");

    public DigestOrganisationLineTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
    }

    [Fact]
    public void Names_each_organisation_by_stance_with_a_link_to_its_statement()
    {
        var line = Render<DigestOrganisationLine>(p => p
            .Add(c => c.BillNumber, "XVP-0412")
            .Add(c => c.Id, "orgs")
            .Add(c => c.Organisations, (IReadOnlyList<DigestOrganisationResponse>)
            [
                new("for", Guid.NewGuid(), "a", "Pavyzdinė mokesčių aiškumo asociacija", For1),
                new("for", Guid.NewGuid(), "b", "Pavyzdinė dirbančiųjų bendrija", For2),
                new("remarks", Guid.NewGuid(), "c", "Pavyzdinė viešųjų finansų stebėsena", Remarks),
            ]));

        line.Find("#orgs-for").TextContent.ShouldBe("Organizacijos, prašančios balsuoti už: Pavyzdinė mokesčių aiškumo asociacija, Pavyzdinė dirbančiųjų bendrija.");
        line.Find("#orgs-remarks").TextContent.ShouldBe("Pastabų atsiuntė: Pavyzdinė viešųjų finansų stebėsena.");
        line.FindAll("#orgs-against").ShouldBeEmpty();
        line.FindAll("#orgs-for a").Select(a => a.GetAttribute("href"))
            .ShouldBe(["/seimas/XVP-0412#statement-" + For1, "/seimas/XVP-0412#statement-" + For2]);
    }

    [Fact]
    public void Shows_nothing_without_a_line_or_with_an_empty_one()
    {
        Render<DigestOrganisationLine>(p => p.Add(c => c.BillNumber, "XVP-0412").Add(c => c.Id, "orgs").Add(c => c.Organisations, null)).Markup.Trim().ShouldBeEmpty();
        Render<DigestOrganisationLine>(p => p.Add(c => c.BillNumber, "XVP-0412").Add(c => c.Id, "orgs").Add(c => c.Organisations, (IReadOnlyList<DigestOrganisationResponse>)[]))
            .Markup.Trim().ShouldBeEmpty();
    }
}
