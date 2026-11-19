using System.Security.Claims;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// The components of one page render at the same time in the request's one scope (the islands' server-side clients). Reads
/// they reach in the Identity module run at once and must not share the scope's one Identity context, which cannot run two
/// operations together ("a second operation was started on this context instance").
/// </summary>
public sealed class ConcurrentReadTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>
    /// The trial site, 2026-11-19: a moderator following a bill opened its page and got the error page now and then. The
    /// moderators' panel asked the staff rules (the session's roles) while "Sekti" asked which MP the person speaks for, both
    /// on the request's one Identity context. The same two reads, three of each at once in one scope (twenty loads of the page
    /// itself on the local database did not show the race; this shows it every time).
    /// </summary>
    [Fact]
    public async Task The_staff_rules_and_the_mp_links_may_be_read_at_once_in_one_scope()
    {
        var moderator = await ModeratorAsync();
        var userId = await Database.UserIdAsync(moderator.Email);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
        var ct = TestContext.Current.CancellationToken;

        for (var round = 0; round < 5; round++)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityModule>();
            var links = scope.ServiceProvider.GetRequiredService<IMpLinks>();
            var staff = Enumerable.Range(0, 3).Select(_ => identity.GetStaffSessionAsync(principal, ct)).ToList();
            var speaks = Enumerable.Range(0, 3).Select(_ => links.SpeaksForAsync([userId], ct)).ToList();
            await Task.WhenAll(staff.Cast<Task>().Concat(speaks));

            // Without the session's claim no staff session holds (the account's roles are still read); the moderator speaks
            // for no MP.
            staff.ShouldAllBe(s => s.Result.ActiveRoles.Count == 0, $"round {round + 1}");
            speaks.ShouldAllBe(s => s.Result.Count == 0, $"round {round + 1}");
        }
    }
}
