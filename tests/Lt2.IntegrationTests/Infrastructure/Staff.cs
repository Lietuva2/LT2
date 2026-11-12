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
            new PasskeyCredentialRequest { Credential = authenticator.Get(await request.Content.ReadAsStringAsync()) });
    }

    /// <summary>A fresh passkey confirmation in the current session; returns the response.</summary>
    public static async Task<HttpResponseMessage> ConfirmPasskeyAsync(this TestBrowser browser, SoftwareAuthenticator authenticator)
    {
        using var request = await browser.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        return await browser.PostAsync(
            ApiRoutes.Me.PasskeyConfirmation,
            new PasskeyCredentialRequest { Credential = authenticator.Get(await request.Content.ReadAsStringAsync()) });
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

/// <summary>A member who is an active staff member: signed in with the staff passkey after activation.</summary>
public sealed record StaffMember(string Email, TestBrowser Browser, SoftwareAuthenticator Authenticator) : IDisposable
{
    public void Dispose()
    {
        Browser.Dispose();
        Authenticator.Dispose();
    }
}

public static class StaffMembers
{
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
