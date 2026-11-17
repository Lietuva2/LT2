using Lt2.Notifications.Templates;

namespace Lt2.UnitTests;

/// <summary>
/// Owner, 2026-11-17 ("make it a normal link with the text and url under it"): in a member's email every line ending
/// "text: https://…" is a link with that text and the address printed under it in the HTML part; any other address in a line is
/// a plain link; the plain part is unchanged; nothing is rewritten or tracked.
/// </summary>
public sealed class EmailLinkTests
{
    private const string Url = "https://lt2.test/seimas/XVP-0412";

    private static string Link(string text) =>
        $"<a href=\"{Url}\" style=\"font-weight:600;color:#1f6b3a\">{text}</a><br><span style=\"font-size:13px;color:#64748b;word-break:break-all\">{Url}</span>";

    [Fact]
    public void A_short_label_and_an_address_is_a_link_with_the_address_under_it() =>
        EmailLayout.LinkLineHtml("Projektas: " + Url).ShouldBe(Link("Projektas"));

    [Fact]
    public void Of_several_sentences_only_the_last_is_the_link_text() =>
        EmailLayout.LinkLineHtml("Šį laišką gavote, nes sekate šį projektą Lietuva 2.0. Ką sekate, galite pakeisti paskyroje: " + Url)
            .ShouldBe("Šį laišką gavote, nes sekate šį projektą Lietuva 2.0. " + Link("Ką sekate, galite pakeisti paskyroje"));

    [Fact]
    public void A_list_line_keeps_its_dash_outside_the_link() =>
        EmailLayout.LinkLineHtml("– XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa: " + Url)
            .ShouldBe("– " + Link("XVP-0412 &#183; Gyventojų pajamų mokesčio įstatymo pataisa"), "the text encoded as everywhere in the HTML part");

    [Fact]
    public void An_address_inside_a_sentence_is_a_plain_link() =>
        EmailLayout.LinkLineHtml("Juos rasite " + Url + " (reikia rakto).")
            .ShouldBe($"Juos rasite <a href=\"{Url}\">{Url}</a> (reikia rakto).");

    [Fact]
    public void Text_is_encoded_and_a_line_without_an_address_stays_text() =>
        EmailLayout.LinkLineHtml("„A“ <b> & c").ShouldBe("„A“ &lt;b&gt; &amp; c");

    [Fact]
    public void The_helper_writes_the_plain_line_the_html_turns_into_a_link()
    {
        EmailLayout.LinkLine("Priimti kvietimą", Url).ShouldBe("Priimti kvietimą: " + Url);
        EmailLayout.LinkedHtml("Laba diena,\n" + EmailLayout.LinkLine("Priimti kvietimą", Url)).ShouldBe("Laba diena,<br>" + Link("Priimti kvietimą"));
    }

    [Fact]
    public void A_built_email_keeps_its_plain_part_and_links_in_the_html_part()
    {
        var email = EmailLayout.Build("a@pavyzdys.test", "S", ["Laba diena,", "Projektas: " + Url], "Privatumas: https://lt2.test/privatumas");
        email.TextBody.ShouldBe("Laba diena,\n\nProjektas: " + Url + "\n\nPrivatumas: https://lt2.test/privatumas\n");
        email.HtmlBody!.ShouldContain("<p>" + Link("Projektas") + "</p>");
        email.HtmlBody!.ShouldContain(">Privatumas</a>");
    }
}
