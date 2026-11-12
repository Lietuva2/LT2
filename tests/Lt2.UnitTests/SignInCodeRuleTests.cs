using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 6.2: a six-digit code sent by email, valid for ten minutes, five attempts, single use.</summary>
public sealed class SignInCodeRuleTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static (SignInCode Code, string Digits) Issue()
    {
        var id = Guid.CreateVersion7(IssuedAt);
        var digits = SignInCodeRules.Generate();
        return (new SignInCode
        {
            Id = id,
            UserId = Guid.CreateVersion7(IssuedAt),
            CodeHash = SignInCodeRules.Hash(id, digits),
            CreatedAt = IssuedAt,
            ExpiresAt = IssuedAt + SignInCodeRules.Lifetime,
        }, digits);
    }

    private static string Other(string digits) => digits == "000000" ? "000001" : "000000";

    [Fact]
    public void A_code_is_six_digits()
    {
        for (var i = 0; i < 1000; i++)
        {
            SignInCodeRules.Generate().ShouldMatch("^[0-9]{6}$");
        }
    }

    [Fact]
    public void Codes_are_not_predictable_from_one_another() =>
        Enumerable.Range(0, 200).Select(_ => SignInCodeRules.Generate()).Distinct().Count().ShouldBeGreaterThan(190);

    [Fact]
    public void The_right_code_is_valid_within_ten_minutes()
    {
        var (code, digits) = Issue();
        SignInCodeRules.Check(code, digits, IssuedAt).ShouldBe(CodeCheck.Valid);
        SignInCodeRules.Check(code, digits, IssuedAt.AddMinutes(9).AddSeconds(59)).ShouldBe(CodeCheck.Valid);
    }

    [Fact]
    public void A_code_expires_after_ten_minutes()
    {
        var (code, digits) = Issue();
        SignInCodeRules.Lifetime.ShouldBe(TimeSpan.FromMinutes(10));
        SignInCodeRules.Check(code, digits, IssuedAt.AddMinutes(10)).ShouldBe(CodeCheck.Expired);
    }

    [Fact]
    public void A_wrong_code_is_a_mismatch()
    {
        var (code, digits) = Issue();
        SignInCodeRules.Check(code, Other(digits), IssuedAt).ShouldBe(CodeCheck.Mismatch);
    }

    [Fact]
    public void After_five_failed_attempts_even_the_right_code_is_refused()
    {
        var (code, digits) = Issue();
        SignInCodeRules.MaxAttempts.ShouldBe(5);
        code.FailedAttempts = 4;
        SignInCodeRules.Check(code, digits, IssuedAt).ShouldBe(CodeCheck.Valid);
        code.FailedAttempts = 5;
        SignInCodeRules.Check(code, digits, IssuedAt).ShouldBe(CodeCheck.TooManyAttempts);
    }

    [Fact]
    public void A_code_can_be_used_only_once()
    {
        var (code, digits) = Issue();
        code.ConsumedAt = IssuedAt.AddMinutes(1);
        SignInCodeRules.Check(code, digits, IssuedAt.AddMinutes(2)).ShouldBe(CodeCheck.AlreadyUsed);
    }

    [Fact]
    public void The_stored_hash_is_salted_with_the_code_id()
    {
        var first = SignInCodeRules.Hash(Guid.CreateVersion7(), "123456");
        var second = SignInCodeRules.Hash(Guid.CreateVersion7(), "123456");
        first.ShouldNotBe(second);
        first.Length.ShouldBe(32);
    }

    private static readonly CodeLimits Default = new(
        Lt2.SharedKernel.SettingKeys.SignInCodesPerHour.Default,
        Lt2.SharedKernel.SettingKeys.SignInCodesPerDay.Default);

    [Fact]
    public void By_default_an_address_is_sent_at_most_five_codes_an_hour_and_ten_a_day() =>
        Default.ShouldBe(new CodeLimits(PerHour: 5, PerDay: 10));

    [Fact]
    public void The_fifth_code_of_an_hour_is_sent_and_the_sixth_is_not()
    {
        SignInCodeRules.MayIssue(issuedInLastHour: 4, issuedInLastDay: 4, Default).ShouldBeTrue();
        SignInCodeRules.MayIssue(issuedInLastHour: 5, issuedInLastDay: 5, Default).ShouldBeFalse();
    }

    [Fact]
    public void The_tenth_code_of_a_day_is_sent_and_the_eleventh_is_not_even_in_a_new_hour()
    {
        SignInCodeRules.MayIssue(issuedInLastHour: 0, issuedInLastDay: 9, Default).ShouldBeTrue();
        SignInCodeRules.MayIssue(issuedInLastHour: 0, issuedInLastDay: 10, Default).ShouldBeFalse();
    }

    [Fact]
    public void Either_limit_alone_stops_the_code()
    {
        var limits = new CodeLimits(PerHour: 3, PerDay: 20);
        SignInCodeRules.MayIssue(issuedInLastHour: 3, issuedInLastDay: 3, limits).ShouldBeFalse();
        SignInCodeRules.MayIssue(issuedInLastHour: 0, issuedInLastDay: 20, limits).ShouldBeFalse();
        SignInCodeRules.MayIssue(issuedInLastHour: 2, issuedInLastDay: 19, limits).ShouldBeTrue();
    }

    [Fact]
    public void The_daily_limit_bounds_guessing_to_fifty_tries_a_day_against_one_account() =>
        SignInCodeRules.MaxGuessesPerDay(Default).ShouldBe(50);

    [Fact]
    public void The_limits_are_counted_over_an_hour_and_over_a_day()
    {
        SignInCodeRules.Hour.ShouldBe(TimeSpan.FromHours(1));
        SignInCodeRules.Day.ShouldBe(TimeSpan.FromDays(1));
    }
}
