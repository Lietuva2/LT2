using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Organisations;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Comments;
using Lt2.Web.Client.Organisations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.3: "Užrakinti autoriaus paskyrą" in the "Kiti veiksmai" menu of an item whose author can be restricted from writing (an
/// argument, a comment, an organisation's statement) and of a reported profile, only for an administrator; last of all, after hiding;
/// its form asks one statement, sends the item's kind and id (never an address) and asks the staff passkey on the same press when the
/// server says so.
/// </summary>
public sealed class LockAuthorTests : BunitContext
{
    private const string Statement = "Pakartotinai skelbė kitų asmenų duomenis.";
    private static readonly Guid Item = Guid.Parse("0193a1b2-0000-7000-8000-000000000412");

    private readonly ApiFake _admin;
    private readonly ApiFake _me;

    public LockAuthorTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var moderation = DispatchProxy.Create<IModerationApi, ApiFake>();
        Services.AddSingleton(moderation);
        Services.AddSingleton(DispatchProxy.Create<ICommentModerationApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IOrganisationModerationApi, ApiFake>());
        var profiles = DispatchProxy.Create<IProfilesApi, ApiFake>();
        ((ApiFake)(object)profiles).On("GetModerationAsync", ApiResult<IReadOnlyList<ProfileModerationResponse>>.Ok(new[]
        {
            new ProfileModerationResponse(
                Item, "abc123", "Rūta Mockutė", "Apie mane.", null, null, false,
                new[] { new ProfileReportResponse(Guid.NewGuid(), "insult", "Įžeidžia.", DateTimeOffset.UnixEpoch) }),
        }));
        Services.AddSingleton(profiles);
        var auth = DispatchProxy.Create<IAuthApi, ApiFake>();
        ((ApiFake)(object)auth).On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        Services.AddSingleton(auth);
        var admin = DispatchProxy.Create<IAdminApi, ApiFake>();
        _admin = (ApiFake)(object)admin;
        Services.AddSingleton(admin);
        var me = DispatchProxy.Create<IMeApi, ApiFake>();
        _me = (ApiFake)(object)me;
        _me.On("ConfirmPasskeyAsync", ApiResult.Ok());
        Services.AddSingleton(me);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    /// <summary>The account the browser asks <c>GET /me</c> about: a staff member with this role active, or an ordinary member.</summary>
    private void SignedInAs(string? role) =>
        _me.On("GetAsync", ApiResult<MeResponse>.Ok(role is null ? MeFakes.Member : MeFakes.Staff(role)));

    private IRenderedComponent<ModerationActions> Argument(string status = "published", string recordStatus = "open") =>
        Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Kind, "reason").Add(c => c.Status, status).Add(c => c.RecordStatus, recordStatus).Add(c => c.Seen, true));

    private static string[] Offered<T>(IRenderedComponent<T> menu) where T : IComponent =>
        [.. menu.FindAll(".menu button.menu__item").Select(b => b.GetAttribute("data-action")!)];

    private static void Settled<T>(IRenderedComponent<T> menu, bool lockOffered) where T : IComponent =>
        menu.WaitForAssertion(() => Offered(menu).Contains("lock").ShouldBe(lockOffered));

    [Fact]
    public void An_administrator_is_offered_the_lock_last_after_hiding_in_the_one_run_set_apart()
    {
        SignedInAs("administrator");

        var menu = Argument();

        Settled(menu, true);
        Offered(menu).ShouldBe(["restrict", "hide", "lock"]);
        menu.FindAll(".menu hr.menu__rule").Count.ShouldBe(1, "hiding and the lock share the run set apart at the end");
        menu.Find($"#action-lock-{Item}").TextContent.ShouldBe("Užrakinti autoriaus paskyrą");
    }

    [Theory]
    [InlineData("moderator")]
    [InlineData(null)]
    public async Task Anyone_who_is_not_an_administrator_is_never_offered_the_lock(string? role)
    {
        SignedInAs(role);

        var menu = Argument();

        await menu.InvokeAsync(() => { });
        menu.WaitForState(() => _me.Calls.Contains("GetAsync"));
        Offered(menu).ShouldBe(["restrict", "hide"]);
    }

    [Fact]
    public void The_lock_is_offered_where_the_restriction_is_and_nowhere_else()
    {
        SignedInAs("administrator");

        var waiting = Argument("pending");
        Settled(waiting, true);
        Offered(waiting).ShouldBe(["restrict", "hide", "lock"], "the filled button is publish; the menu holds the rest");

        var onClosedRecord = Argument("published", recordStatus: "closed");
        onClosedRecord.WaitForState(() => _me.Calls.Contains("GetAsync"));
        Offered(onClosedRecord).ShouldNotContain("lock", "a closed record offers hiding only, and no restriction");
        Offered(onClosedRecord).ShouldNotContain("restrict");

        var bill = Render<ModerationActions>(p => p.Add(c => c.Id, Item).Add(c => c.Number, "XVP-0412").Add(c => c.Kind, "record").Add(c => c.Status, "open"));
        bill.WaitForState(() => _me.Calls.Contains("GetAsync"));
        Offered(bill).ShouldNotContain("lock", "a bill's record has no author");
    }

    [Theory]
    [InlineData("published", new[] { "restrict", "hide", "lock" })]
    [InlineData("hidden", new[] { "restrict", "lock" })]
    public void A_comments_menu_offers_the_lock_last_to_an_administrator_only(string status, string[] expected)
    {
        SignedInAs("administrator");

        var menu = Render<CommentModerationActions>(p => p.Add(c => c.Id, Item).Add(c => c.Status, status));

        Settled(menu, true);
        Offered(menu).ShouldBe(expected);
    }

    [Fact]
    public void A_comments_menu_has_no_lock_for_a_moderator()
    {
        SignedInAs("moderator");

        var menu = Render<CommentModerationActions>(p => p.Add(c => c.Id, Item).Add(c => c.Status, "published"));

        menu.WaitForState(() => _me.Calls.Contains("GetAsync"));
        Offered(menu).ShouldNotContain("lock");
    }

    [Theory]
    [InlineData("statement", "pending", false, true)]
    [InlineData("statement", "published", false, true)]
    [InlineData("statement", "pending", true, false)]
    [InlineData("organisation", "active", false, false)]
    public void An_organisations_statement_offers_the_lock_where_its_writer_can_be_restricted_and_an_organisation_never(string kind, string status, bool billFinished, bool offered)
    {
        SignedInAs("administrator");

        var menu = Render<OrgModerationActions>(p => p.Add(c => c.Id, Item).Add(c => c.Kind, kind).Add(c => c.Status, status).Add(c => c.BillFinished, billFinished));

        menu.WaitForState(() => _me.Calls.Contains("GetAsync"));
        menu.WaitForAssertion(() => Offered(menu).Contains("lock").ShouldBe(offered));
        if (offered)
        {
            Offered(menu)[^1].ShouldBe("lock");
        }
    }

    [Fact]
    public void A_reported_profile_offers_the_lock_last_to_an_administrator()
    {
        SignedInAs("administrator");

        var admin = Render<ProfileModerationView>();

        admin.WaitForAssertion(() => Offered(admin).ShouldBe(["hide", "lock"]));
        admin.Find("#profile-action-lock-" + Item).Click();
        admin.FindAll("#lock-profile-" + Item.ToString("N")).Count.ShouldBe(1, "the same form, for the profile's moderation id");
    }

    [Fact]
    public void A_reported_profile_offers_no_lock_to_a_moderator()
    {
        SignedInAs("moderator");

        var moderator = Render<ProfileModerationView>();

        moderator.WaitForAssertion(() => _me.Calls.ShouldContain("GetAsync"));
        moderator.WaitForAssertion(() => Offered(moderator).ShouldBe(["hide"]));
    }

    [Fact]
    public void The_lock_opens_one_statement_form_and_cancel_brings_the_menu_back()
    {
        SignedInAs("administrator");
        var menu = Argument();
        Settled(menu, true);

        menu.Find($"#action-lock-{Item}").Click();

        menu.FindAll($"#lock-{Item:N}").Count.ShouldBe(1);
        menu.Find($"#lock-sentence-{Item:N}").TextContent.ShouldBe(Text("Acc.Lock.Sentence"));
        menu.FindAll(".menu").ShouldBeEmpty("one action open at a time");
        menu.Find($"#lock-cancel-{Item:N}").Click();
        menu.FindAll($"#lock-{Item:N}").ShouldBeEmpty();
        Offered(menu).ShouldContain("lock");
        _admin.Calls.ShouldNotContain("LockAuthorAsync");
    }

    [Fact]
    public void A_statement_is_required_and_nothing_is_sent_without_it()
    {
        SignedInAs("administrator");
        var menu = Argument();
        Settled(menu, true);

        menu.Find($"#action-lock-{Item}").Click();
        menu.Find($"#lock-{Item:N}").Submit();

        menu.WaitForAssertion(() => menu.FindAll($"#lock-{Item:N} .field--error").Count.ShouldBe(1));
        _admin.Calls.ShouldNotContain("LockAuthorAsync");
    }

    [Fact]
    public void Locking_sends_the_items_kind_and_id_with_the_statement_asks_the_passkey_on_the_same_press_and_says_it_is_done()
    {
        SignedInAs("administrator");
        _admin.OnEach("LockAuthorAsync", ApiResult.Fail(new ApiProblem { Status = 403, Code = "passkey.confirmation-required", Detail = "x" }), ApiResult.Ok());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
        var menu = Argument();
        Settled(menu, true);

        menu.Find($"#action-lock-{Item}").Click();
        menu.Find($"#lock-statement-{Item:N}").Input(Statement);
        menu.Find($"#lock-{Item:N}").Submit();

        menu.WaitForAssertion(() => _admin.Calls.Count(c => c == "LockAuthorAsync").ShouldBe(2));
        _me.Calls.ShouldContain("ConfirmPasskeyAsync");
        var request = (AuthorLockRequest)_admin.Arguments[_admin.Calls.LastIndexOf("LockAuthorAsync")][0]!;
        (request.TargetKind, request.TargetId, request.Statement).ShouldBe(("reason", Item, Statement));
        menu.WaitForAssertion(() => menu.Markup.ShouldContain(Text("Lock.Done")));
        menu.FindAll($"#lock-{Item:N}").ShouldBeEmpty();
    }

    [Fact]
    public void Cancelling_the_passkey_prompt_locks_nothing_and_keeps_the_statement()
    {
        SignedInAs("administrator");
        _admin.On("LockAuthorAsync", ApiResult.Fail(new ApiProblem { Status = 403, Code = "passkey.confirmation-required", Detail = "x" }));
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        var menu = Argument();
        Settled(menu, true);

        menu.Find($"#action-lock-{Item}").Click();
        menu.Find($"#lock-statement-{Item:N}").Input(Statement);
        menu.Find($"#lock-{Item:N}").Submit();

        menu.WaitForAssertion(() => menu.Markup.ShouldContain(Text("Acc.Passkey.Cancelled")));
        _admin.Calls.Count(c => c == "LockAuthorAsync").ShouldBe(1);
        menu.Find($"#lock-statement-{Item:N}").GetAttribute("value").ShouldBe(Statement);
    }
}
