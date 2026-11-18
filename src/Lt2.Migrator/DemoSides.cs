using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Parliament;
using Microsoft.Extensions.Configuration;

namespace Lt2.Migrator;

/// <summary>
/// <c>--demo-sides-for ADDRESS</c> (design 8.26.5): gives one existing account random sides on the invented closed bills
/// (<see cref="DemoClosedBills.Bills"/> and <see cref="DemoAppeal.ClosedBill"/>), dated before their votes and added to
/// their frozen counts, so the account's owner can see "Kas Seime balsuoja taip, kaip prašote jūs" for themselves. The
/// address is matched as sign-in matches it (normalised, case-insensitive); <c>-</c> reads it from the standard input, so
/// it need not stand in a command line. The address is never logged or shown, a refusal included.
/// <para>
/// Refuses unless the configuration says <c>Demo:Allowed=true</c> (the local stack and the trial) and the parliament data is
/// the stand-in's; refuses an unknown address, an account without the consent to store positions, and an MP's own account.
/// Only that one account is touched. The sides follow from the account's identifier (a fixed rule, no clock): running it
/// again gives nothing twice.
/// </para>
/// </summary>
public static class DemoSides
{
    public sealed record Outcome(int For, int Against);

    public static async Task<Outcome> RunAsync(IConfiguration configuration, string connectionString, string address, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        if (!configuration.GetValue<bool>("Demo:Allowed"))
        {
            throw new InvalidOperationException("Refused: --demo-sides-for runs only where Demo:Allowed is true (the local stack and the trial).");
        }

        if (!await ParliamentMaintenance.IsDataInventedAsync(connectionString, cancellationToken))
        {
            throw new InvalidOperationException("Refused: --demo-sides-for needs the Seimas stand-in's invented parliament data, and this database holds none or real data.");
        }

        if (address == "-")
        {
            address = (await Console.In.ReadLineAsync(cancellationToken))?.Trim() ?? "";
        }

        var account = await DemoSideAccount.FindAsync(connectionString, address, cancellationToken);
        switch (account.Status)
        {
            case DemoSideAccountStatus.Unknown:
                throw new InvalidOperationException("Refused: no account with a completed registration has the address given; nothing was changed.");
            case DemoSideAccountStatus.NoPositionConsent:
                throw new InvalidOperationException("Refused: the account has not given the consent to store positions (or withdrew it); nothing was changed.");
            case DemoSideAccountStatus.MpAccount:
                throw new InvalidOperationException("Refused: the account is an MP's own account, which takes no side (design 8.5.4); nothing was changed.");
        }

        var (given, against) = await InventedRecord.GiveClosedSidesAsync(
            connectionString, account.UserId, account.MemberSince, Sides(account.UserId), clock.GetUtcNow(), cancellationToken);
        return new Outcome(given, against);
    }

    /// <summary>The account's sides: about 55 % "for", each placed somewhere in its record's open time; the same for the same account.</summary>
    public static IReadOnlyList<(string Bill, string Side, double Placed)> Sides(Guid userId)
    {
        var random = new Random(BitConverter.ToInt32(userId.ToByteArray(), 0));
        return DemoClosedBills.Bills.Select(b => b.Number).Append(DemoAppeal.ClosedBill)
            .Select(bill => (bill, random.NextDouble() < 0.55 ? "for" : "against", random.NextDouble()))
            .ToList();
    }
}
