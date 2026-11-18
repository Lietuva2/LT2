using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The version histories show what changed (owner, 2026-11-18): one version against the one before it, inserted words in
/// <c>&lt;ins&gt;</c> and removed ones in <c>&lt;del&gt;</c> with hidden words for screen readers, the paragraphs and lists kept,
/// and the whole text behind "Rodyti visą tekstą" (the browser's own <c>details.more</c>, design 8.15). Server-rendered.
/// </summary>
public sealed class TextChangesTests : BunitContext
{
    public TextChangesTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
    }

    [Fact]
    public void Inserted_words_are_in_ins_and_removed_words_in_del_each_announced()
    {
        var changes = Render<TextChanges>(p => p.Add(c => c.Old, "Projektas mažina tarifą.").Add(c => c.New, "Projektas gerokai didina tarifą."));
        var text = changes.Find("div.text-changes.typed > p");
        var del = text.QuerySelector("del")!;
        var ins = text.QuerySelector("ins")!;
        del.TextContent.ShouldBe("pašalinta: mažina");
        del.QuerySelector("span.visually-hidden")!.TextContent.ShouldBe("pašalinta: ");
        ins.TextContent.ShouldBe("pridėta: gerokai didina");
        ins.QuerySelector("span.visually-hidden")!.TextContent.ShouldBe("pridėta: ");
        text.TextContent.ShouldBe("Projektas pašalinta: mažinapridėta: gerokai didina tarifą.");
    }

    [Fact]
    public void The_whole_text_is_behind_Rodyti_visą_tekstą_closed()
    {
        var changes = Render<TextChanges>(p => p.Add(c => c.Old, "Pirma.\nAntra.").Add(c => c.New, "Pirma.\nAntra, pakeista.").Add(c => c.Class, "voice typed"));
        var whole = changes.Find("details.more.stack.stack--tight.text-changes__whole");
        whole.HasAttribute("open").ShouldBeFalse();
        whole.QuerySelector("summary.linkish.small")!.TextContent.ShouldBe("Rodyti visą tekstą");
        whole.QuerySelectorAll("div.voice.typed > p").Select(e => e.TextContent).ShouldBe(["Pirma.", "Antra, pakeista."]);
        whole.QuerySelectorAll("ins, del").ShouldBeEmpty();
    }

    [Fact]
    public void The_oldest_version_is_shown_whole_without_marks()
    {
        var changes = Render<TextChanges>(p => p.Add(c => c.Old, null).Add(c => c.New, "Pirmoji versija."));
        changes.Find("p.typed").TextContent.ShouldBe("Pirmoji versija.");
        changes.FindAll("ins, del, details").ShouldBeEmpty();
    }

    [Fact]
    public void A_version_with_the_same_text_is_shown_whole_without_marks()
    {
        var changes = Render<TextChanges>(p => p.Add(c => c.Old, "Tas pats.").Add(c => c.New, "Tas pats."));
        changes.Find("p.typed").TextContent.ShouldBe("Tas pats.");
        changes.FindAll("ins, del, details").ShouldBeEmpty();
    }

    [Fact]
    public void A_removed_text_is_struck_through_whole_without_the_whole_text()
    {
        var changes = Render<TextChanges>(p => p.Add(c => c.Old, "Buvęs aprašymas.").Add(c => c.New, null));
        changes.Find("div.text-changes > p > del").TextContent.ShouldBe("pašalinta: Buvęs aprašymas.");
        changes.FindAll("ins, details").ShouldBeEmpty();
    }

    [Fact]
    public void A_list_stays_a_list_with_its_numbers_and_its_changes()
    {
        var changes = Render<TextChanges>(p => p
            .Add(c => c.Old, "Siūlome:\n3. mažinti tarifą\n4. palikti lengvatas\n5. tikrinti kasmet")
            .Add(c => c.New, "Siūlome:\n3. mažinti tarifą\n4. panaikinti lengvatas"));
        var box = changes.Find("div.text-changes");
        box.Children.Select(e => e.TagName.ToLowerInvariant()).ShouldBe(["p", "ol"]);
        var items = box.QuerySelectorAll("ol.typed-list.typed-list--numbered > li");
        items.Select(li => li.GetAttribute("value")).ShouldBe(["3", "4", "5"], "the numbers as typed; the removed item keeps its place");
        items[1].QuerySelector("del")!.TextContent.ShouldBe("pašalinta: palikti");
        items[1].QuerySelector("ins")!.TextContent.ShouldBe("pridėta: panaikinti");
        items[2].QuerySelector("del")!.TextContent.ShouldBe("pašalinta: tikrinti kasmet");
    }

    [Fact]
    public void Nothing_typed_becomes_markup()
    {
        var changes = Render<TextChanges>(p => p.Add(c => c.Old, "Tekstas <b>senas</b>.").Add(c => c.New, "Tekstas <i>naujas</i>."));
        changes.FindAll("b, i").ShouldBeEmpty();
        changes.Find("div.text-changes").TextContent.ShouldContain("</");
        changes.Find("details p").TextContent.ShouldBe("Tekstas <i>naujas</i>.");
    }
}
