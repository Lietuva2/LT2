using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 6.2, 8.20.2: a browser known to the account has its own five codes an hour and ten a day (the same two settings, counted separately); a code it
/// asked for is accepted only from it; at most twenty per account; a year unseen and it is forgotten.
/// </summary>
public sealed class KnownBrowserRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Laptop = Guid.CreateVersion7(Now);
    private static readonly Guid Phone = Guid.CreateVersion7(Now.AddSeconds(1));

    private static SignInCode Code(Guid? browser, int minutesAgo, bool consumed = false) => new()
    {
        Id = Guid.CreateVersion7(Now.AddMinutes(-minutesAgo)),
        UserId = Guid.Empty,
        CreatedAt = Now.AddMinutes(-minutesAgo),
        ExpiresAt = Now.AddMinutes(10 - minutesAgo),
        ConsumedAt = consumed ? Now : null,
        KnownDeviceId = browser,
    };

    [Fact]
    public void A_known_browser_has_its_own_five_codes_an_hour_and_ten_a_day_by_the_same_settings()
    {
        var defaults = new CodeLimits(Lt2.SharedKernel.SettingKeys.SignInCodesPerHour.Default, Lt2.SharedKernel.SettingKeys.SignInCodesPerDay.Default);
        defaults.ShouldBe(new CodeLimits(5, 10));
        KnownBrowserRules.MayIssue(0, 0, defaults).ShouldBeTrue();
        KnownBrowserRules.MayIssue(4, 9, defaults).ShouldBeTrue();
        KnownBrowserRules.MayIssue(5, 5, defaults).ShouldBeFalse("five an hour");
    }

    [Fact]
    public void A_known_browser_gets_no_eleventh_code_in_a_day_so_whoever_holds_the_cookie_cannot_guess_more()
    {
        var defaults = new CodeLimits(5, 10);
        KnownBrowserRules.MayIssue(0, 10, defaults).ShouldBeFalse();
        SignInCodeRules.MaxGuessesPerDay(defaults).ShouldBe(50, "per allowance, as for the shared one");
    }

    [Fact]
    public void A_known_browsers_allowance_follows_the_settings()
    {
        var raised = new CodeLimits(20, 50);
        KnownBrowserRules.MayIssue(19, 49, raised).ShouldBeTrue();
        KnownBrowserRules.MayIssue(0, 50, raised).ShouldBeFalse();
    }

    [Fact]
    public void The_shared_limits_for_other_browsers_stay_five_an_hour_and_ten_a_day()
    {
        var limits = new CodeLimits(5, 10);
        SignInCodeRules.MayIssue(4, 9, limits).ShouldBeTrue();
        SignInCodeRules.MayIssue(5, 5, limits).ShouldBeFalse();
        SignInCodeRules.MayIssue(0, 10, limits).ShouldBeFalse();
    }

    [Fact]
    public void A_code_any_browser_may_use_is_accepted_from_every_browser()
    {
        KnownBrowserRules.MayUse(null, null).ShouldBeTrue();
        KnownBrowserRules.MayUse(null, Laptop).ShouldBeTrue();
    }

    [Fact]
    public void A_code_a_known_browser_asked_for_is_accepted_only_from_it()
    {
        KnownBrowserRules.MayUse(Laptop, Laptop).ShouldBeTrue();
        KnownBrowserRules.MayUse(Laptop, null).ShouldBeFalse("a stranger's browser");
        KnownBrowserRules.MayUse(Laptop, Phone).ShouldBeFalse("another browser known to the same account");
    }

    [Fact]
    public void A_stranger_tries_only_the_newest_shared_code_never_a_known_browsers()
    {
        var shared = Code(null, 1);
        var tried = KnownBrowserRules.CodesToTry([Code(null, 5), shared, Code(Laptop, 0)], null);
        tried.ShouldBe(new[] { shared });
    }

    [Fact]
    public void A_known_browser_tries_its_own_newest_code_and_the_newest_shared_one()
    {
        var own = Code(Laptop, 2);
        var shared = Code(null, 1);
        var tried = KnownBrowserRules.CodesToTry([Code(Laptop, 4), own, shared, Code(Phone, 0)], Laptop);
        tried.ShouldBe(new[] { own, shared });
    }

    [Fact]
    public void Used_or_replaced_codes_are_never_tried()
    {
        KnownBrowserRules.CodesToTry([Code(null, 1, consumed: true), Code(Laptop, 0, consumed: true)], Laptop).ShouldBeEmpty();
    }

    [Fact]
    public void An_account_keeps_twenty_known_browsers_and_the_one_seen_longest_ago_goes_first()
    {
        var browsers = Enumerable.Range(0, 21).Select(i => (Id: Guid.CreateVersion7(Now.AddMinutes(i)), LastSeenAt: Now.AddDays(-i))).ToList();
        KnownBrowserRules.MaxPerAccount.ShouldBe(20);
        KnownBrowserRules.BeyondCap(browsers).ShouldBe(new[] { browsers[20].Id });
        KnownBrowserRules.BeyondCap(browsers.Take(20)).ShouldBeEmpty();
    }

    [Fact]
    public void A_browser_not_seen_for_a_year_is_forgotten()
    {
        KnownBrowserRules.IsStale(Now.AddDays(-364), Now).ShouldBeFalse();
        KnownBrowserRules.IsStale(Now.AddDays(-366), Now).ShouldBeTrue();
        KnownBrowserRules.StaleBefore(Now).ShouldBe(Now.AddDays(-365));
    }
}
