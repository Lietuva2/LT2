using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Lt2.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The sign-in and signed-in pages show a note only when something happened, and then the localized text, never a
/// variable name. Rendered with bUnit against fake API clients, so no server or browser is needed.
/// </summary>
public sealed class SignInComponentTests : BunitContext
{
    private const string Email = "ruta.mockute@pavyzdys.test";

    private readonly FakeAuthApi _auth = new();
    private readonly FakeMeApi _me = new();

    public SignInComponentTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        Services.AddSingleton<IAuthApi>(_auth);
        Services.AddSingleton<IMeApi>(_me);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private string Text(string key)
    {
        var text = Services.GetRequiredService<IStringLocalizer<UiText>>()[key];
        text.ResourceNotFound.ShouldBeFalse(key);
        return text.Value;
    }

    [Fact]
    public void No_note_is_shown_before_anything_happened()
    {
        var page = Render<SignIn>();

        page.FindAll(".note").ShouldBeEmpty();
        page.Markup.ShouldNotContain("_message");
        page.Markup.ShouldNotContain("_info");
    }

    [Fact]
    public void No_note_is_shown_on_the_registration_or_code_steps_either()
    {
        var page = Render<SignIn>();
        page.FindAll("button.linkish")[0].Click();
        page.Find("#reg-email").ShouldNotBeNull();
        page.FindAll(".note").ShouldBeEmpty();

        page.FindAll("button.linkish")[0].Click();
        page.Find("#email").Input(Email);
        page.Find("form").Submit();
        page.Find("#code").ShouldNotBeNull();
        page.FindAll(".note").ShouldBeEmpty();
    }

    [Fact]
    public void A_request_that_cannot_reach_the_server_shows_the_network_error()
    {
        _auth.RequestCode = _ => throw new HttpRequestException("offline");
        var page = Render<SignIn>();

        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        var note = page.WaitForElement(".note[role=alert]");
        note.TextContent.ShouldBe(Text("Error.Network"));
        page.FindAll(".note--quiet").ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_request_shows_the_servers_explanation()
    {
        const string detail = "Per daug bandymų. Palaukite ir bandykite vėliau.";
        _auth.RequestCode = _ => Task.FromResult(ApiResult.Fail(new ApiProblem { Status = 429, Code = "rate-limited", Detail = detail }));
        var page = Render<SignIn>();

        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(detail);
    }

    [Fact]
    public void Resending_the_code_confirms_it_quietly()
    {
        var page = Render<SignIn>();
        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        page.FindAll("button.linkish")[0].Click();

        page.WaitForElement(".note--quiet").TextContent.ShouldBe(Text("Code.Resent"));
        page.FindAll(".note[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void The_signed_in_page_shows_no_note_until_an_action_fails()
    {
        _me.Sessions =
        [
            new SessionResponse { Id = Guid.NewGuid(), Method = "otp", IsCurrent = true },
            new SessionResponse { Id = Guid.NewGuid(), Method = "otp", IsCurrent = false },
        ];
        var page = Render<Hello>();
        page.WaitForElement("#hello-title").TextContent.ShouldBe("Sveiki, Rūta M.!");
        page.FindAll(".note").ShouldBeEmpty();
        page.Markup.ShouldNotContain("_message");

        _me.EndSession = _ => throw new HttpRequestException("offline");
        page.Find(".rows__side button").Click();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(Text("Error.Network"));
    }

    [Fact]
    public void The_note_component_renders_nothing_without_text()
    {
        Render<Messages>().Markup.Trim().ShouldBeEmpty();
        Render<Messages>(p => p.Add(m => m.Message, "Klaida")).Find(".note").TextContent.ShouldBe("Klaida");
    }

    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<RequestCodeRequest, Task<ApiResult>> RequestCode { get; set; } = _ => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default) =>
            RequestCode(request);

        public Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<string>.Ok("{}"));

        public Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<string>.Ok("{}"));

        public Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());
    }

    private sealed class FakeMeApi : IMeApi
    {
        public IReadOnlyList<SessionResponse> Sessions { get; set; } = [];

        public Func<Guid, Task<ApiResult>> EndSession { get; set; } = _ => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<MeResponse>.Ok(new MeResponse
            {
                Id = Guid.NewGuid(),
                Email = Email,
                DisplayName = "Rūta M.",
                VerificationLevel = "email",
                Roles = ["member"],
            }));

        public Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<IReadOnlyList<SessionResponse>>.Ok(Sessions));

        public Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => EndSession(sessionId);
    }
}
