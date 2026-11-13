using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Opening an appeal (design 8.1): the form cannot be used before WebAssembly has taken over the prerendered page, and
/// when the bill got an appeal meanwhile that the person cannot see (another member's, waiting for a moderator), their
/// text stays and they are told why, instead of a reload to the same empty form.
/// </summary>
public sealed class OpenAppealFormTests : BunitContext
{
    private const string Bill = "XVP-0412";
    private const string Description = "Projektas keičia mokesčių tvarką: įvedamas naujas tarifas didelėms pajamoms.";

    private readonly AppealsApiFake _api;

    public OpenAppealFormTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var api = DispatchProxy.Create<IAppealsApi, AppealsApiFake>();
        _api = (AppealsApiFake)(object)api;
        _api.On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 50, 500, 0, 0, 3000)));
        _api.On("GetMyRestrictionAsync", ApiResult<MyRestrictionResponse>.Ok(new MyRestrictionResponse(false, null, null)));
        Services.AddSingleton(api);
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private static ApiProblem Problem(int status, string code) => new() { Status = status, Code = code };

    private IRenderedComponent<OpenAppealForm> FillAndSubmit()
    {
        var form = Render<OpenAppealForm>(p => p.Add(c => c.Number, Bill));
        form.Find("#open-description").Input(Description);
        form.Find("form").Submit();
        return form;
    }

    [Fact]
    public void While_only_prerendered_nothing_in_the_form_can_be_used()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var form = Render<OpenAppealForm>(p => p.Add(c => c.Number, Bill));

        form.Find("#open-description").HasAttribute("disabled").ShouldBeTrue();
        form.Find("#open-threshold").HasAttribute("disabled").ShouldBeTrue();
        form.Find("form button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Opened_the_person_is_taken_to_the_appeal()
    {
        _api.On("OpenAsync", ApiResult<AppealResponse>.Ok(null!));
        FillAndSubmit();

        Navigation.History.Single().Uri.ShouldBe($"/seimas/{Bill}/kreipimasis");
    }

    [Fact]
    public void Taken_meanwhile_by_an_appeal_waiting_for_a_moderator_the_text_stays_and_the_note_says_why()
    {
        _api.On("OpenAsync", ApiResult<AppealResponse>.Fail(Problem(409, "appeal.exists")));
        _api.On("GetAppealAsync", ApiResult<AppealResponse>.Fail(Problem(404, "appeal.not-found")));
        var form = FillAndSubmit();

        Navigation.History.ShouldBeEmpty("a reload would show this form again, empty");
        form.Find("#open-description").GetAttribute("value").ShouldBe(Description);
        form.Markup.ShouldContain(Text("Open.TakenMeanwhile"));
        form.Find("form button[type=submit]").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void Taken_meanwhile_by_a_published_appeal_the_person_is_taken_to_it()
    {
        _api.On("OpenAsync", ApiResult<AppealResponse>.Fail(Problem(409, "appeal.exists")));
        _api.On("GetAppealAsync", ApiResult<AppealResponse>.Ok(null!));
        FillAndSubmit();

        Navigation.History.Single().Uri.ShouldBe($"/seimas/{Bill}/kreipimasis");
    }

    /// <summary>Answers the calls it was told about, by method name; any other call fails the test.</summary>
    public class AppealsApiFake : DispatchProxy
    {
        private readonly Dictionary<string, object> _answers = [];

        public void On<T>(string method, ApiResult<T> answer) => _answers[method] = Task.FromResult(answer);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            _answers.TryGetValue(targetMethod!.Name, out var answer)
                ? answer
                : throw new NotSupportedException($"IAppealsApi.{targetMethod.Name} was not expected here.");
    }
}
