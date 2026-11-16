using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Migrator;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Passkey and staff steps as a browser takes them, for the staff rules (design 6.2).</summary>
public static class StaffSteps
{
    /// <summary>Adds the authenticator's passkey to the signed-in account; returns the response.</summary>
    public static async Task<HttpResponseMessage> AddPasskeyAsync(this TestBrowser browser, SoftwareAuthenticator authenticator)
    {
        using var creation = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
        if (creation.StatusCode != HttpStatusCode.OK)
        {
            return await CopyAsync(creation);
        }

        return await browser.PostAsync(
            ApiRoutes.Auth.Passkeys,
            new PasskeyCredentialRequest { Credential = authenticator.Create(await creation.Content.ReadAsStringAsync()) });
    }

    /// <summary>Signs this browser in with the authenticator's passkey; returns the response.</summary>
    public static async Task<HttpResponseMessage> SignInWithPasskeyAsync(this TestBrowser browser, SoftwareAuthenticator authenticator)
    {
        using var request = await browser.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        return await browser.PostAsync(
            ApiRoutes.Auth.PasskeySignIn,
            new PasskeyCredentialRequest { Credential = authenticator.Get(await OptionsAsync(request)) });
    }

    /// <summary>A fresh passkey confirmation in the current session; returns the response.</summary>
    public static async Task<HttpResponseMessage> ConfirmPasskeyAsync(this TestBrowser browser, SoftwareAuthenticator authenticator)
    {
        using var request = await browser.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        return await browser.PostAsync(
            ApiRoutes.Me.PasskeyConfirmation,
            new PasskeyCredentialRequest { Credential = authenticator.Get(await OptionsAsync(request)) });
    }

    /// <summary>The passkey options the server gave; a refusal fails with its status and body, not later in the authenticator.</summary>
    private static async Task<string> OptionsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"passkey options: {body}");
        return body;
    }

    /// <summary>
    /// Design 6.2: this browser's session becomes a staff session with the authenticator's passkey, without signing out;
    /// returns the response.
    /// </summary>
    public static async Task<HttpResponseMessage> StartStaffSessionAsync(this TestBrowser browser, SoftwareAuthenticator authenticator)
    {
        using var request = await browser.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        return await browser.PostAsync(
            ApiRoutes.Me.StaffSession,
            new PasskeyCredentialRequest { Credential = authenticator.Get(await OptionsAsync(request)) });
    }

    public static Task<HttpResponseMessage> ActivateRoleAsync(this TestBrowser browser, string code) =>
        browser.PostAsync(ApiRoutes.Me.RoleActivation, new RoleActivationRequest { Code = code });

    /// <summary>Grants a staff role from the server, as the migrator does; returns the one-time activation code.</summary>
    public static async Task<string> GrantFromServerAsync(this DatabaseFixture database, string email, string role)
    {
        var grant = await MigrationRunner.GrantRoleAsync(database.MigratorConnectionString, email, role, CancellationToken.None);
        grant.IsSuccess.ShouldBeTrue(grant.Error?.Code);
        return grant.Value.ActivationCode;
    }

    private static async Task<HttpResponseMessage> CopyAsync(HttpResponseMessage response)
    {
        var copy = new HttpResponseMessage(response.StatusCode) { Content = new StringContent(await response.Content.ReadAsStringAsync()) };
        copy.Content.Headers.ContentType = response.Content.Headers.ContentType;
        return copy;
    }
}

/// <summary>
/// A member who is an active staff member: signed in with the staff passkey after activation. A <paramref name="Shared"/>
/// one belongs to every test of the run (<see cref="StaffMembers.SharedAsync"/>): disposing it does nothing.
/// </summary>
public sealed record StaffMember(string Email, TestBrowser Browser, SoftwareAuthenticator Authenticator, bool Shared = false) : IDisposable
{
    public void Dispose()
    {
        if (Shared)
        {
            return;
        }

        Browser.Dispose();
        Authenticator.Dispose();
    }
}

public static class StaffMembers
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<(Lt2Factory Factory, string Role), StaffMember> Made = [];

    /// <summary>
    /// One active staff member per application and role for the whole run: making one takes a registration, a passkey and
    /// a grant through the migrator, so tests that only need someone in the role share them. A test about the staff
    /// account itself (its passkeys, sessions, a reset) makes its own with <see cref="ActiveAsync"/>.
    /// </summary>
    public static async Task<StaffMember> SharedAsync(Lt2Factory factory, DatabaseFixture database, string role)
    {
        await Gate.WaitAsync();
        try
        {
            if (!Made.TryGetValue((factory, role), out var member))
            {
                member = await ActiveAsync(factory, database, role) with { Shared = true };
                Made[(factory, role)] = member;
            }
            else
            {
                // A fresh passkey confirmation for every test that takes the shared member: saving a setting needs one from
                // the last ten minutes (design 6.9), and without this a test passed only when another test had confirmed
                // shortly before it, which depended on the order and the speed of the run.
                using var confirmed = await member.Browser.ConfirmPasskeyAsync(member.Authenticator);
                confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
            }

            return member;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Registers a member, adds a passkey, grants the role from the server, signs in with the passkey in a new browser
    /// and activates the role there.
    /// </summary>
    public static async Task<StaffMember> ActiveAsync(Lt2Factory factory, DatabaseFixture database, string role)
    {
        var authenticator = new SoftwareAuthenticator();
        string email;
        using (var setup = factory.NewBrowser())
        {
            email = await setup.RegisterAndSignInAsync();
            using var added = await setup.AddPasskeyAsync(authenticator);
            added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        }

        var code = await database.GrantFromServerAsync(email, role);
        var browser = factory.NewBrowser();
        using (var signIn = await browser.SignInWithPasskeyAsync(authenticator))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await signIn.Content.ReadAsStringAsync());
        }

        using (var activation = await browser.ActivateRoleAsync(code))
        {
            activation.StatusCode.ShouldBe(HttpStatusCode.NoContent, await activation.Content.ReadAsStringAsync());
        }

        return new StaffMember(email, browser, authenticator);
    }
}
