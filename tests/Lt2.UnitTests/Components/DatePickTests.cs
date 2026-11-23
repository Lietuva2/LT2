using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The calendar button beside a typed date field (owner, 2026-11-23): only a button, rendered hidden (js/lt2-dates.js shows it
/// where the browser has showPicker()), carrying the id of the text field it fills and a label; the native date input is made
/// by the script on a press, never rendered.
/// </summary>
public sealed class DatePickTests : BunitContext
{
    public DatePickTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
    }

    [Fact]
    public void It_renders_only_a_hidden_button_with_the_text_fields_id_a_label_and_the_calendar_icon()
    {
        var pick = Render<DatePick>(p => p.Add(c => c.InputId, "flag-date"));

        var button = pick.Find("button");
        button.GetAttribute("type").ShouldBe("button");
        button.GetAttribute("data-date-pick").ShouldBe("flag-date");
        button.GetAttribute("aria-label").ShouldBe("Pasirinkti datą iš kalendoriaus");
        button.ClassList.ShouldContain("date-pick");
        button.HasAttribute("hidden").ShouldBeTrue("hidden until the script shows it, so no page shows a dead control");
        button.HasAttribute("disabled").ShouldBeFalse();
        button.QuerySelector("svg.icon rect").ShouldNotBeNull("the calendar icon");
        pick.FindAll("input").ShouldBeEmpty("the native date input is made by the script on a press, never rendered");
    }

    [Fact]
    public void It_is_disabled_with_its_field()
    {
        var pick = Render<DatePick>(p => p.Add(c => c.InputId, "restrict-until-1").Add(c => c.Disabled, true));

        pick.Find("button").HasAttribute("disabled").ShouldBeTrue();
    }
}
