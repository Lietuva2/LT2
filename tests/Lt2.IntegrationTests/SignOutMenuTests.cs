using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// "Atsijungti" in the main menu (owner, 2026-11-13): a server-rendered form on every page for a signed-in person, which
/// works without JavaScript and keeps the antiforgery protection.
/// </summary>
public sealed partial class SignOutMenuTests(DatabaseFixture database) : ApiTest(database)
{
    [GeneratedRegex("<form[^>]*id=\"nav-sign-out\"[^>]*>.*?</form>", RegexOptions.Singleline)]
    private static partial Regex FormRegex();

    [GeneratedRegex("<input[^>]*>")]
    private static partial Regex InputRegex();

    private static Dictionary<string, string> Fields(string form) =>
        InputRegex().Matches(form)
            .Select(m => (Name: Regex.Match(m.Value, "name=\"([^\"]+)\"").Groups[1].Value, Value: Regex.Match(m.Value, "value=\"([^\"]*)\"").Groups[1].Value))
            .Where(f => f.Name.Length > 0)
            .ToDictionary(f => WebUtility.HtmlDecode(f.Name), f => WebUtility.HtmlDecode(f.Value));

    [Theory]
    [InlineData("/metodika")]
    [InlineData("/seimas/XVP-0412/kreipimasis")]
    [InlineData("/sveiki")]
    public async Task A_signed_in_person_signs_out_from_the_menu_on_any_page_without_javascript(string path)
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var form = FormRegex().Match(await member.Client.GetStringAsync(path));
        form.Success.ShouldBeTrue("the menu has the sign-out form");
        var fields = Fields(form.Value);
        fields.ShouldContainKey("__RequestVerificationToken");

        using var posted = await member.Client.PostAsync(path, new FormUrlEncodedContent(fields));
        ((int)posted.StatusCode).ShouldBeLessThan(400, await posted.Content.ReadAsStringAsync());
        if (posted.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther)
        {
            new Uri(new Uri("https://lt2.test"), posted.Headers.Location!).AbsolutePath.ShouldBe("/", "taken to the front page");
        }
        else
        {
            posted.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/", "taken to the front page");
        }

        using var me = await member.Client.GetAsync(ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "signed out");
    }

    [Fact]
    public async Task Without_the_antiforgery_token_the_form_signs_nobody_out()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var fields = Fields(FormRegex().Match(await member.Client.GetStringAsync("/metodika")).Value);
        fields.Remove("__RequestVerificationToken");

        using var posted = await member.Client.PostAsync("/metodika", new FormUrlEncodedContent(fields));
        posted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var me = await member.Client.GetAsync(ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.OK, "still signed in");
    }

    [Fact]
    public async Task A_visitor_has_no_sign_out_in_the_menu()
    {
        using var stranger = NewBrowser();
        FormRegex().IsMatch(await stranger.Client.GetStringAsync("/metodika")).ShouldBeFalse();
    }
}
