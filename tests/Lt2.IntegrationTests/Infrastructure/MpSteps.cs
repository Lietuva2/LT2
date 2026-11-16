using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>A stand-in MP of the invented parliament: the official address (only in the synthetic open data) and the person number.</summary>
public sealed record StandInMp(string Address, long SeimasPersonId, string Name);

/// <summary>An account that took an MP's profile over: its browser (a passkey session), its passkey and its address.</summary>
public sealed record MpAccount(TestBrowser Browser, SoftwareAuthenticator Authenticator, string Email, StandInMp Mp, string Slug) : IDisposable
{
    public void Dispose()
    {
        Browser.Dispose();
        Authenticator.Dispose();
    }
}

/// <summary>
/// Steps of design 8.5 as a browser takes them. One account per address: each test takes its own stand-in MP and deletes
/// the account it made (which ends the link), so no profile stays taken over for the next test. Ona Jonaitienė is never
/// taken over: her profile guards the one nobody took over (<c>ParliamentPageTests</c>).
/// </summary>
public static class MpSteps
{
    public static readonly StandInMp Azuolaitis = new("pavyzdys.marius.azuolaitis@lrs.lt", 99000002, "Marius Ąžuolaitis");
    public static readonly StandInMp Berzinyte = new("pavyzdys.egle.berzinyte@lrs.lt", 99000003, "Eglė Beržinytė");
    public static readonly StandInMp Klevinskas = new("pavyzdys.vytautas.klevinskas@lrs.lt", 99000004, "Vytautas Klevinskas");
    public static readonly StandInMp Uosyte = new("pavyzdys.laima.uosyte@lrs.lt", 99000006, "Laima Uosytė");
    public static readonly StandInMp Liepaitis = new("pavyzdys.darius.liepaitis@lrs.lt", 99000007, "Darius Liepaitis");
    public static readonly StandInMp Pusinskiene = new("pavyzdys.asta.pusinskiene@lrs.lt", 99000008, "Asta Pušinskienė");
    public static readonly StandInMp Gluosnis = new("pavyzdys.kestutis.gluosnis@lrs.lt", 99000009, "Kęstutis Gluosnis");
    public static readonly StandInMp Eglaite = new("pavyzdys.indre.eglaite@lrs.lt", 99000010, "Indrė Eglaitė");

    /// <summary>Tadas Alksninis: his mandate ended, so his address is official for nobody (design 8.5.1).</summary>
    public static readonly StandInMp Alksninis = new("pavyzdys.tadas.alksninis@lrs.lt", 99000011, "Tadas Alksninis");

    public static readonly StandInMp Sermuksnyte = new("pavyzdys.gintare.sermuksnyte@lrs.lt", 99000012, "Gintarė Šermukšnytė");
    public static readonly StandInMp Kadagyte = new("pavyzdys.aurelija.kadagyte@lrs.lt", 99000013, "Aurelija Kadagytė");
    public static readonly StandInMp Ievaite = new("pavyzdys.rasa.ievaite@lrs.lt", 99000014, "Rasa Ievaitė");

    /// <summary>The profile's slug of a stand-in MP.</summary>
    public static Task<string?> SlugAsync(this DatabaseFixture database, StandInMp mp) =>
        database.ScalarAsync<string>("SELECT slug FROM parliament.person WHERE seimas_person_id = @id", ("id", mp.SeimasPersonId));

    /// <summary>
    /// A completed member whose address is the MP's official address (with the trial invitation unless told otherwise), with
    /// the positions consent, signed in by code. An account left on the address by a failed run is deleted first.
    /// </summary>
    public static async Task<TestBrowser> OfficialMemberAsync(this Lt2Factory factory, StandInMp mp, string? address = null, string? invitation = TestData.Invitation)
    {
        await factory.Database.ForgetAccountAsync(address ?? mp.Address);
        var browser = factory.NewBrowser();
        var code = await browser.RegisterAsync(address ?? mp.Address, invitation);
        await browser.SignInWithCodeAsync(address ?? mp.Address, code);
        await browser.CompleteRegistrationAsync(mp.Name);
        using var given = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, new PositionConsentRequest { TextVersion = PositionConsent.TextVersion });
        given.StatusCode.ShouldBe(HttpStatusCode.NoContent, await given.Content.ReadAsStringAsync());
        return browser;
    }

    public static Task<HttpResponseMessage> ClaimAsync(this TestBrowser browser, string slug) => browser.PostAsync(MpRoutes.ClaimOf(slug), new { });

    /// <summary>The token of the newest take-over link in the outbox to <paramref name="to"/> (the part after the <c>#</c>).</summary>
    public static async Task<string> TakeoverTokenAsync(this DatabaseFixture database, string to)
    {
        var path = await database.ScalarAsync<string>(
            "SELECT payload->>'path' FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) ORDER BY created_at DESC, id DESC LIMIT 1",
            ("k", MessageKinds.MpTakeoverLinkEmail), ("to", to));
        path.ShouldNotBeNull($"a take-over link to {to} is in the outbox");
        path.ShouldStartWith(MpRoutes.TakeoverPage + "#", Case.Sensitive, "design 6.8: the token after the #");
        return path[(path.IndexOf('#', StringComparison.Ordinal) + 1)..];
    }

    /// <summary>Makes a passkey on <paramref name="authenticator"/> for the signed-in account and takes the profile over with <paramref name="token"/>.</summary>
    public static async Task<HttpResponseMessage> TakeOverAsync(this TestBrowser browser, SoftwareAuthenticator authenticator, string token)
    {
        using var creation = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
        creation.StatusCode.ShouldBe(HttpStatusCode.OK, await creation.Content.ReadAsStringAsync());
        return await browser.PostAsync(MpRoutes.Takeover, new MpTakeoverRequest { Token = token, Credential = authenticator.Create(await creation.Content.ReadAsStringAsync()) });
    }

    /// <summary>The whole take-over (design 8.5.2): an official member claims the profile, reads the link and makes the passkey.</summary>
    public static async Task<MpAccount> MpAccountAsync(this Lt2Factory factory, StandInMp mp)
    {
        var browser = await factory.OfficialMemberAsync(mp);
        var slug = (await factory.Database.SlugAsync(mp))!;
        using (var claimed = await browser.ClaimAsync(slug))
        {
            claimed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await claimed.Content.ReadAsStringAsync());
        }

        var authenticator = new SoftwareAuthenticator();
        using (var taken = await browser.TakeOverAsync(authenticator, await factory.Database.TakeoverTokenAsync(mp.Address)))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
        }

        return new MpAccount(browser, authenticator, mp.Address, mp, slug);
    }

    public static async Task<MeResponse> MeAsync(this TestBrowser browser) =>
        (await browser.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root, JsonSerializerOptions.Web))!;

    /// <summary>A member signed in with a passkey (a passkey session), as an office member accepts an invitation.</summary>
    public static async Task<(TestBrowser Browser, SoftwareAuthenticator Authenticator, string Email)> PasskeyMemberAsync(this Lt2Factory factory, string displayName)
    {
        var (setup, email) = await factory.MemberAsync(displayName);
        var authenticator = new SoftwareAuthenticator();
        using (setup)
        {
            using var added = await setup.AddPasskeyAsync(authenticator);
            added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        }

        var browser = factory.NewBrowser();
        using var signIn = await browser.SignInWithPasskeyAsync(authenticator);
        signIn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await signIn.Content.ReadAsStringAsync());
        return (browser, authenticator, email);
    }

    /// <summary>Deletes the account through the API (design 8.5.2: deleting the account ends the link); asserts it worked.</summary>
    public static async Task DeleteAccountAsync(this TestBrowser browser)
    {
        using var deleted = await browser.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
    }

    /// <summary>A failed run's account on <paramref name="address"/> goes, with its link (the rows cascade).</summary>
    public static Task ForgetAccountAsync(this DatabaseFixture database, string address) =>
        database.ExecuteAsync("DELETE FROM identity.\"user\" WHERE lower(email) = lower(@e)", ("e", address));

    /// <summary>Open links of a person, in a part (<c>mp</c> or <c>office</c>).</summary>
    public static Task<long> OpenLinksAsync(this DatabaseFixture database, long seimasPersonId, string part) =>
        database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.politician_link WHERE seimas_person_id = @p AND part = @part AND ended_at IS NULL", ("p", seimasPersonId), ("part", part));

    /// <summary>A page's persisted component state, each entry decoded, as one text (to prove what the server put in it).</summary>
    public static string StateOf(string html)
    {
        var comment = System.Text.RegularExpressions.Regex.Match(html, "<!--Blazor-(WebAssembly-)?Component-State:[^>]*-->");
        comment.Success.ShouldBeTrue("a page with islands carries their state");
        var payload = comment.Value[(comment.Value.IndexOf(':', StringComparison.Ordinal) + 1)..^3];
        using var outer = JsonDocument.Parse(Convert.FromBase64String(payload));
        return string.Join("\n", outer.RootElement.EnumerateObject().Select(p => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p.Value.GetString()!))));
    }

    /// <summary>The account notices about MPs in the outbox to <paramref name="to"/>, oldest first.</summary>
    public static async Task<List<string>> MpNoticesAsync(this DatabaseFixture database, string to)
    {
        var notices = new List<string>();
        await using var connection = new Npgsql.NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT payload->>'notice' FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) AND payload->>'notice' LIKE 'mp-%' ORDER BY created_at, id",
            connection);
        command.Parameters.AddWithValue("k", MessageKinds.AccountNoticeEmail);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notices.Add(reader.GetString(0));
        }

        return notices;
    }
}
