using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lt2.Identity;
using Lt2.Identity.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 6.4: the proof of work of registration and code requests, in the ALTCHA protocol: a signed challenge with its expiry,
/// a number found by trying, and the checks; a challenge is used once (that part, which needs the database, is in
/// <c>BotCheckTests</c> of the integration tests).
/// </summary>
public sealed class BotCheckRuleTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("a key for tests");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static BotChallenge Challenge(int number = 123, int max = 1000, int minutes = 10) =>
        BotCheckRules.NewChallenge(Key, max, Now.AddMinutes(minutes), number, new byte[12]);

    private static BotCheckOutcome Check(string? payload, DateTimeOffset? now = null) =>
        BotCheckRules.Check(payload, Key, now ?? Now, out _, out _);

    [Fact]
    public void A_challenge_is_the_hash_of_its_salt_and_the_secret_number_signed_by_the_server()
    {
        var challenge = Challenge(number: 123);

        challenge.Algorithm.ShouldBe("SHA-256");
        challenge.Salt.ShouldBe("000000000000000000000000?expires=" + Now.AddMinutes(10).ToUnixTimeSeconds());
        challenge.Challenge.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Salt + "123"))));
        challenge.Signature.ShouldBe(Convert.ToHexStringLower(HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(challenge.Challenge))));
        challenge.MaxNumber.ShouldBe(1000);
    }

    [Fact]
    public void Every_challenge_has_a_salt_of_its_own()
    {
        var salts = Enumerable.Range(0, 50).Select(_ => BotCheckRules.NewChallenge(Key, 1000, Now.AddMinutes(10)).Salt).Distinct().Count();
        salts.ShouldBe(50);
    }

    [Fact]
    public void The_secret_number_is_anywhere_from_zero_to_the_maximum()
    {
        var numbers = Enumerable.Range(0, 400).Select(_ => BotCheckRules.Solve(BotCheckRules.NewChallenge(Key, 20, Now.AddMinutes(10)))!.Value).ToList();
        numbers.ShouldAllBe(n => n >= 0 && n <= 20);
        numbers.Distinct().Count().ShouldBeGreaterThan(10);
    }

    [Fact]
    public void A_solved_challenge_is_accepted_and_names_its_signature_and_expiry()
    {
        var challenge = Challenge();
        var solution = BotCheckRules.Payload(challenge, BotCheckRules.Solve(challenge)!.Value);

        BotCheckRules.Check(solution, Key, Now, out var signature, out var expires).ShouldBe(BotCheckOutcome.Accepted);

        signature.ShouldBe(challenge.Signature);
        expires.ShouldBe(Now.AddMinutes(10));
    }

    [Fact]
    public void A_solution_made_by_another_party_in_the_browsers_way_is_accepted()
    {
        // What lt2-botcheck.js sends: base64 of JSON with these five fields in this naming.
        var challenge = Challenge(number: 777);
        var json = $$"""{"algorithm":"SHA-256","challenge":"{{challenge.Challenge}}","number":777,"salt":"{{challenge.Salt}}","signature":"{{challenge.Signature}}"}""";

        Check(Convert.ToBase64String(Encoding.UTF8.GetBytes(json))).ShouldBe(BotCheckOutcome.Accepted);
    }

    [Fact]
    public void A_number_that_does_not_solve_the_challenge_is_refused()
    {
        var challenge = Challenge(number: 123);

        Check(BotCheckRules.Payload(challenge, 124)).ShouldBe(BotCheckOutcome.WrongNumber);
        Check(BotCheckRules.Payload(challenge, 0)).ShouldBe(BotCheckOutcome.WrongNumber);
    }

    [Fact]
    public void A_signature_that_is_not_the_servers_is_refused()
    {
        var challenge = Challenge();
        var other = BotCheckRules.NewChallenge(Encoding.UTF8.GetBytes("another key"), 1000, Now.AddMinutes(10), 123, new byte[12]);

        challenge.Challenge.ShouldBe(other.Challenge);
        Check(BotCheckRules.Payload(other, 123)).ShouldBe(BotCheckOutcome.BadSignature);
        Check(BotCheckRules.Payload(challenge with { Signature = new string('0', 64) }, 123)).ShouldBe(BotCheckOutcome.BadSignature);
        Check(BotCheckRules.Payload(challenge with { Signature = new string('z', 64) }, 123)).ShouldBe(BotCheckOutcome.BadSignature, "not even hex");
    }

    [Fact]
    public void A_challenge_whose_expiry_was_changed_no_longer_matches_its_signature()
    {
        // The expiry is part of the salt, so part of what is hashed and signed: moving it makes another challenge.
        var challenge = Challenge(number: 123);
        var later = challenge with { Salt = challenge.Salt.Replace(Now.AddMinutes(10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), Now.AddDays(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)) };

        Check(BotCheckRules.Payload(later, 123)).ShouldBe(BotCheckOutcome.WrongNumber);
    }

    [Fact]
    public void A_challenge_is_refused_from_the_moment_it_expires()
    {
        var challenge = Challenge(number: 5, minutes: 10);
        var solution = BotCheckRules.Payload(challenge, 5);

        Check(solution, Now.AddMinutes(10).AddSeconds(-1)).ShouldBe(BotCheckOutcome.Accepted);
        Check(solution, Now.AddMinutes(10)).ShouldBe(BotCheckOutcome.Expired);
        Check(solution, Now.AddHours(2)).ShouldBe(BotCheckOutcome.Expired);
    }

    [Fact]
    public void A_salt_without_an_expiry_is_refused()
    {
        var salt = "abcdef";
        var challenge = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + "1")));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(challenge)));
        var solved = new BotChallenge("SHA-256", challenge, 10, salt, signature);

        Check(BotCheckRules.Payload(solved, 1)).ShouldBe(BotCheckOutcome.Malformed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    [InlineData("e30=")]
    [InlineData("bnVsbA==")] // null
    [InlineData("W10=")] // []
    public void A_missing_or_malformed_solution_is_refused(string? payload) => Check(payload).ShouldBe(BotCheckOutcome.Malformed);

    [Fact]
    public void A_solution_with_another_algorithm_or_a_negative_number_is_refused()
    {
        var challenge = Challenge();
        Check(BotCheckRules.Payload(challenge with { Algorithm = "SHA-1" }, 123)).ShouldBe(BotCheckOutcome.Malformed);
        Check(BotCheckRules.Payload(challenge, -1)).ShouldBe(BotCheckOutcome.Malformed);
    }

    [Fact]
    public void A_solution_longer_than_a_real_one_is_refused_without_being_read()
    {
        var challenge = Challenge();
        var solution = BotCheckRules.Payload(challenge, 123);
        solution.Length.ShouldBeLessThan(BotCheckRules.MaxSolutionLength);

        Check(solution + new string('A', BotCheckRules.MaxSolutionLength)).ShouldBe(BotCheckOutcome.Malformed);
    }

    [Fact]
    public void The_expiry_is_read_from_the_salt_as_altcha_writes_it()
    {
        BotCheckRules.ExpiryOf("aa?expires=1700000000").ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        BotCheckRules.ExpiryOf("aa?expires=1700000000&x=1").ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        BotCheckRules.ExpiryOf("aa").ShouldBeNull();
        BotCheckRules.ExpiryOf("aa?expires=").ShouldBeNull();
        BotCheckRules.ExpiryOf("aa?expires=-5").ShouldBeNull();
        BotCheckRules.ExpiryOf("aa?expires=abc").ShouldBeNull();
    }

    [Fact]
    public void The_default_difficulty_takes_a_fraction_of_a_second_to_solve_with_one_hash_after_another()
    {
        // The setting's default (BotCheck:MaxNumber, 20 000) is solved by the browser in 10 000 tries on average and 20 000 at
        // worst. A server core does a SHA-256 try in about a microsecond, a browser's crypto.subtle in about 5 to 50 (design 6.4).
        var challenge = BotCheckRules.NewChallenge(Key, Lt2.SharedKernel.SettingKeys.BotCheckMaxNumber.Default, Now.AddMinutes(10), Lt2.SharedKernel.SettingKeys.BotCheckMaxNumber.Default, new byte[12]);
        var watch = Stopwatch.StartNew();
        BotCheckRules.Solve(challenge).ShouldBe(Lt2.SharedKernel.SettingKeys.BotCheckMaxNumber.Default);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
