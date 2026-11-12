using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Parliament;
using Microsoft.Extensions.Configuration;

namespace Lt2.Migrator;

/// <summary>
/// <c>--demo-appeal</c>: fills the appeal on XVP-0412 with invented accounts, so the local appeal page shows the
/// prototype's numbers (design/ui/kreipimasis.html): 6 800 ask to vote for (6 150 of them accounts older than 30 days),
/// 4 280 against (3 900), the prototype's four arguments with their "Man svarbu" counts.
/// <para>
/// It refuses unless both hold: the configuration says <c>Demo:Allowed=true</c> (only <c>deploy/.env</c>, the local
/// defaults, sets it), and the parliament data is the stand-in's (<see cref="ParliamentMaintenance.IsDataInventedAsync"/>):
/// a database that ever imported from the real Seimas service can never be given invented participants.
/// </para>
/// </summary>
public static class DemoAppeal
{
    public const string Bill = "XVP-0412";
    public const int For = 6800;
    public const int ForOlder = 6150;
    public const int Against = 4280;
    public const int AgainstOlder = 3900;

    public const string Description =
        "Įvedamas trečias pajamų mokesčio tarifas: metinėms pajamoms, viršijančioms 60 vidutinių darbo užmokesčių, būtų taikomas 32 % tarifas vietoj 20 %.\n" +
        "Dividendai ir pajamos iš akcijų pardavimo įskaičiuojamos į tą pačią ribą. Pataisa įsigaliotų 2027 m. sausio 1 d.";

    /// <summary>What happened, for the console.</summary>
    public sealed record Outcome(string Status, int AccountsCreated);

    public static async Task<Outcome> RunAsync(IConfiguration configuration, string connectionString, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("Demo:Allowed"))
        {
            throw new InvalidOperationException("Refused: --demo-appeal runs only where Demo:Allowed is true (the local stack's deploy/.env).");
        }

        if (!await ParliamentMaintenance.IsDataInventedAsync(connectionString, cancellationToken))
        {
            throw new InvalidOperationException("Refused: --demo-appeal needs the Seimas stand-in's invented parliament data, and this database holds none or real data.");
        }

        if (await ParliamentMaintenance.BillTitleAsync(connectionString, Bill, cancellationToken) is null)
        {
            throw new InvalidOperationException($"Refused: bill {Bill} is not in the parliament data yet; let the worker import from the stand-in first.");
        }

        var now = clock.GetUtcNow();
        var people = Build(now);
        var created = await InventedMembers.CreateAsync(connectionString, people.Members, cancellationToken);
        var outcome = await InventedAppeal.SeedAsync(connectionString, people.Appeal, cancellationToken);
        return outcome switch
        {
            InventedAppealOutcome.Created => new Outcome("created", created),
            InventedAppealOutcome.AlreadyThere => new Outcome("already there", created),
            _ => throw new InvalidOperationException($"Refused: {Bill} already has an appeal opened by someone else; hide it first if it should go."),
        };
    }

    /// <summary>The invented people and the appeal, the same on every run (fixed seed and identifiers).</summary>
    public static (IReadOnlyList<InventedMember> Members, InventedAppealSpec Appeal) Build(DateTimeOffset now)
    {
        var random = new Random(412);
        var members = new List<InventedMember>();
        var positions = new List<InventedPosition>();

        Guid Id(int n) => new($"0193b000-0412-7000-8000-{n:D12}");
        DateTimeOffset Older() => now.AddDays(-random.Next(31, 420)).AddMinutes(-random.Next(0, 1440));
        DateTimeOffset Newer() => now.AddDays(-random.Next(1, 29)).AddMinutes(-random.Next(0, 1440));

        void Add(int n, string side, bool older, string? name = null)
        {
            var since = older ? Older() : Newer();
            members.Add(new InventedMember(Id(n), $"demo.{n:D5}@{InventedMembers.Domain}", name, since));
            positions.Add(new InventedPosition(Id(n), side, since));
        }

        // The prototype's named authors: Rūta Mockutė opened the appeal.
        Add(1, "against", true, "Rūta Mockutė");
        Add(2, "for", true, "Giedrė L.");
        Add(3, "for", true, "Mindaugas R.");
        Add(4, "against", true, "Aistė Kazlaitė");

        var n = 5;
        for (var i = 2; i < For; i++)
        {
            Add(n++, "for", i < ForOlder);
        }

        for (var i = 2; i < Against; i++)
        {
            Add(n++, "against", i < AgainstOlder);
        }

        var forIds = positions.Where(p => p.Side == "for").Select(p => p.UserId).ToList();
        var againstIds = positions.Where(p => p.Side == "against").Select(p => p.UserId).ToList();
        var reasons = new List<InventedReason>
        {
            new(Id(2), "for", "Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.", forIds.Take(1920).ToList()),
            new(Id(3), "for", "Gynyba finansuojama nekeliant PVM.", forIds.Skip(1000).Take(1310).ToList()),
            new(Id(1), "against", "Akcijų pardavimo įskaičiavimas paliečia vienkartinius įvykius, pvz., šeimos verslo pardavimą.", againstIds.Take(1480).ToList()),
            new(Id(4), "against", "Skatina įmonių steigėjus keisti mokestinę rezidavimo vietą.", againstIds.Skip(800).Take(970).ToList()),
        };

        var opened = now.AddDays(-75);
        return (members, new InventedAppealSpec(Bill, Id(1), Description, 1000, opened, positions, reasons));
    }
}
