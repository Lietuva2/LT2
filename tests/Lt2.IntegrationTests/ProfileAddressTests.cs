using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// A member's public address carries the name (design 8.25.2, owner 2026-11-16): <c>/nariai/{name-slug}-{id}</c>. The id
/// decides; any other name part answers a permanent redirect to the current address; a retired id still answers 410 and an
/// unknown one 404 whatever the name part.
/// </summary>
public sealed class ProfileAddressTests(DatabaseFixture database) : ApiTest(database)
{
    private static async Task<(HttpStatusCode Status, string? Location)> GetAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path, TestContext.Current.CancellationToken);
        return (response.StatusCode, response.Headers.Location?.OriginalString);
    }

    private static async Task<string> AddressAsync(TestBrowser member) =>
        (await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).Address.ShouldNotBeNull();

    [Fact]
    public async Task The_address_carries_the_name_and_any_other_name_part_or_the_id_alone_redirects_permanently_to_it()
    {
        var (member, _, id) = await Factory.PublicMemberAsync("Žydrūnė Ąžuolaitė-Čiurlionienė");
        using var _m = member;
        using var visitor = NewBrowser();

        var address = await AddressAsync(member);
        address.ShouldBe($"/nariai/zydrune-azuolaite-ciurlioniene-{id}");
        (await visitor.GetJsonAsync<MemberProfileResponse>(ProfileRoutes.MemberOf(id))).Address.ShouldBe(address);
        (await GetAsync(visitor, address)).Status.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync(address, TestContext.Current.CancellationToken));
        html.ShouldContain("Žydrūnė Ąžuolaitė-Čiurlionienė");

        foreach (var other in new[] { "/nariai/" + id, $"/nariai/kitas-vardas-{id}", $"/nariai/zydrune-{id}" })
        {
            (await GetAsync(visitor, other)).ShouldBe((HttpStatusCode.MovedPermanently, address), other);
        }

        (await GetAsync(visitor, $"/nariai/kitas-{id}?rodyti=20")).ShouldBe((HttpStatusCode.MovedPermanently, address + "?rodyti=20"), "the query is kept");
        // Routing answers any case and a trailing slash: those are other addresses too, redirected to the one served.
        foreach (var other in new[] { address.ToUpperInvariant(), "/NARIAI/" + address["/nariai/".Length..], address + "/", "/nariai/" + id.ToUpperInvariant() })
        {
            (await GetAsync(visitor, other)).ShouldBe((HttpStatusCode.MovedPermanently, address), other);
        }

        // A name ending in what looks like an id still leads by the last part (design 8.25.2: it may only read like another's).
        (await GetAsync(visitor, $"/nariai/vardas-zxcvbnmk45-{id}")).ShouldBe((HttpStatusCode.MovedPermanently, address));

        // A changed display name changes the address; the old one leads to the new one.
        using (var renamed = await member.SaveFormAsync(f => f with { DisplayName = "Rūta Mockutė" }))
        {
            renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        }

        var changed = await AddressAsync(member);
        changed.ShouldBe($"/nariai/ruta-mockute-{id}");
        (await GetAsync(visitor, address)).ShouldBe((HttpStatusCode.MovedPermanently, changed));
        (await GetAsync(visitor, changed)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_retired_id_answers_410_and_an_unknown_one_404_whatever_the_name_part()
    {
        var (member, _, id) = await Factory.PublicMemberAsync("Išeisianti Narė");
        using var _m = member;
        var address = await AddressAsync(member);
        (await member.SavedProfileAsync(null, isPublic: false)).ProfileId.ShouldBeNull();
        using var visitor = NewBrowser();
        foreach (var path in new[] { address, "/nariai/" + id, $"/nariai/kitas-vardas-{id}" })
        {
            (await GetAsync(visitor, path)).ShouldBe((HttpStatusCode.Gone, (string?)null), path);
        }

        var unknown = ProfileSteps.UnknownProfileId();
        foreach (var path in new[] { "/nariai/" + unknown, $"/nariai/ruta-mockute-{unknown}" })
        {
            (await GetAsync(visitor, path)).ShouldBe((HttpStatusCode.NotFound, (string?)null), path);
        }
    }

    [Fact]
    public async Task A_name_with_only_symbols_gives_the_id_alone_and_two_members_with_one_name_get_different_addresses()
    {
        var (symbols, _, symbolsId) = await Factory.PublicMemberAsync("☆ … ☆");
        var (first, _, firstId) = await Factory.PublicMemberAsync("Jonas Petraitis");
        var (second, _, secondId) = await Factory.PublicMemberAsync("Jonas Petraitis");
        using var _s = symbols;
        using var _f = first;
        using var _t = second;
        using var visitor = NewBrowser();

        (await AddressAsync(symbols)).ShouldBe("/nariai/" + symbolsId);
        (await GetAsync(visitor, "/nariai/" + symbolsId)).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(visitor, $"/nariai/zvaigzde-{symbolsId}")).ShouldBe((HttpStatusCode.MovedPermanently, "/nariai/" + symbolsId));

        var a = await AddressAsync(first);
        var b = await AddressAsync(second);
        (a, b).ShouldBe(($"/nariai/jonas-petraitis-{firstId}", $"/nariai/jonas-petraitis-{secondId}"));
        a.ShouldNotBe(b);
        (await GetAsync(visitor, a)).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(visitor, b)).Status.ShouldBe(HttpStatusCode.OK);
    }
}
