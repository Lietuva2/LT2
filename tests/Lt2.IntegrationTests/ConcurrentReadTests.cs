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

    /// <summary>
    /// Every read the module offers to pages and to other modules takes a context of its own, not only the two the trial site
    /// showed: the account, its sessions and known browsers, the terms, the party support, the staff rules, whom the account
    /// speaks for as an MP, the MPs taken over, the office, the member's profile, the moderators' queue, the member's code and
    /// the codes' numbers, the feedback list and the MP links, all at once in one scope, twice each.
    /// </summary>
    [Fact]
    public async Task The_modules_reads_may_run_at_once_in_one_scope()
    {
        var (_, email) = await Factory.MemberAsync("Rūta Mockutė");
        var userId = await Database.UserIdAsync(email);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
        var ct = TestContext.Current.CancellationToken;

        for (var round = 0; round < 5; round++)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var identity = services.GetRequiredService<IIdentityModule>();
            var politicians = services.GetRequiredService<IPoliticians>();
            var profiles = services.GetRequiredService<IMemberProfiles>();
            var invitations = services.GetRequiredService<IInvitations>();
            var feedback = services.GetRequiredService<IFeedback>();
            var links = services.GetRequiredService<IMpLinks>();

            var accounts = Twice(() => identity.GetAccountAsync(principal, ct));
            var sessions = Twice(() => identity.GetSessionsAsync(principal, ct));
            var browsers = Twice(() => identity.GetKnownBrowsersAsync(principal, ct));
            var terms = Twice(() => identity.TermsRequiredAsync(principal, ct));
            var parties = Twice(() => identity.GetPartySupportAsync(principal, ct));
            var staff = Twice(() => identity.GetStaffSessionAsync(principal, ct));
            var speakers = Twice(() => politicians.SpeakerAsync(principal, ct));
            var takenOver = Twice(() => politicians.TakenOverAsync([1], ct));
            var offices = Twice(() => politicians.OfficeAsync(principal, ct));
            var mine = Twice(() => profiles.MineAsync(principal, ct));
            var queues = Twice(() => profiles.QueueAsync(ct));
            var codes = Twice(() => invitations.MineAsync(userId, ct));
            var stats = Twice(() => invitations.StatsAsync(30, null, ct));
            var items = Twice(() => feedback.ListAsync(null, null, ct));
            var speaks = Twice(() => links.SpeaksForAsync([userId], ct));
            await Task.WhenAll(new IEnumerable<Task>[]
            {
                accounts, sessions, browsers, terms, parties, staff, speakers, takenOver, offices, mine, queues, codes, stats, items, speaks,
            }.SelectMany(t => t));

            var label = $"round {round + 1}";
            accounts.ShouldAllBe(a => a.Result.IsSuccess && a.Result.Value.Id == userId && a.Result.Value.RegistrationComplete, label);
            sessions.ShouldAllBe(s => s.Result.Count > 0, label);
            browsers.ShouldAllBe(b => b.Result.IsSuccess, label);
            terms.ShouldAllBe(t => !t.Result, label);
            parties.ShouldAllBe(p => p.Result.IsSuccess && p.Result.Value.Party == null, label);
            staff.ShouldAllBe(s => s.Result.ActiveRoles.Count == 0, label);
            speakers.ShouldAllBe(s => s.Result == null, label);
            takenOver.ShouldAllBe(t => t.Result.Count == 0, label);
            offices.ShouldAllBe(o => !o.Result.IsSuccess, label);
            mine.ShouldAllBe(m => m.Result.IsSuccess, label);
            codes.ShouldAllBe(c => c.Result.IsSuccess, label);
            codes.Select(c => c.Result.Value.Code).Distinct().Count().ShouldBe(1, label);
            speaks.ShouldAllBe(s => s.Result.Count == 0, label);
        }
    }

    private static List<Task<T>> Twice<T>(Func<Task<T>> read) => [read(), read()];
}
