using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Parliament;
using Microsoft.Extensions.Configuration;

namespace Lt2.Migrator;

/// <summary>
/// <c>--demo-appeal</c>: fills the record of XVP-0412 with invented accounts, so the local bill page shows the
/// prototype's numbers (design/ui/kreipimasis.html): 6 800 ask to vote for (6 150 of them accounts older than 30 days),
/// 4 280 against (3 900), the prototype's four arguments with their "Man svarbu" counts. It also gives <see cref="AiBill"/>
/// an invented description with the source <c>ai</c> (design 8.13), under the same refusals.
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

    /// <summary>
    /// Design 8.13: a second bill gets an invented AI summary nobody has read (source <c>ai</c>), so the label "Sukurta DI ·
    /// žmonių neperžiūrėta", "Pranešti apie klaidą" and the moderator's confirm, correct and remove can be seen locally
    /// without a provider key. Invented text, not made by any AI.
    /// </summary>
    public const string AiBill = "XVP-0533";

    public const string AiDescription =
        "Projektas leistų savivaldybėms pačioms nustatyti vietinių rinkliavų dydį pagal paslaugos sąnaudas, o ne pagal bendrą visai šaliai dydį. " +
        "Rinkliavų dydžius savivaldybės turėtų skelbti iki kitų metų pradžios. Pataisa įsigaliotų 2027 m. sausio 1 d.";

    /// <summary>What happened, for the console. <c>AiDescription</c>: <c>created</c>, <c>already there</c> or <c>left as it is</c>.</summary>
    public sealed record Outcome(string Status, int AccountsCreated, string AiDescription);

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

        if (await ParliamentMaintenance.BillTitleAsync(connectionString, AiBill, cancellationToken) is null)
        {
            throw new InvalidOperationException($"Refused: bill {AiBill} is not in the parliament data yet; let the worker import from the stand-in first.");
        }

        var now = clock.GetUtcNow();
        var people = Build(now);
        var created = await InventedMembers.CreateAsync(connectionString, people.Members, cancellationToken);
        var outcome = await InventedAppeal.SeedAsync(connectionString, people.Appeal, cancellationToken);
        if (outcome == InventedAppealOutcome.BillTaken)
        {
            throw new InvalidOperationException($"Refused: {Bill} already has a record that people took part on; the demonstration fills only a bill nobody took part in.");
        }

        // The invented AI summary links to the bill's own e-seimas address from the (invented) parliament data.
        var address = await ParliamentMaintenance.BillAddressAsync(connectionString, AiBill, cancellationToken);
        var summary = await InventedAppeal.SeedAiDescriptionAsync(connectionString, AiBill, AiDescription, address, now.AddDays(-2), cancellationToken);
        return new Outcome(
            outcome == InventedAppealOutcome.Created ? "created" : "already there",
            created,
            summary switch
            {
                InventedAppealOutcome.Created => "created",
                InventedAppealOutcome.AlreadyThere => "already there",
                _ => "left as it is",
            });
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

            // Sides taken at any time since the appeal opened (not all on day one), so the invented data does not read as
            // a surge of new accounts.
            var opened = now.AddDays(-75);
            var earliest = since > opened ? since : opened;
            var taken = earliest + TimeSpan.FromMinutes(random.NextDouble() * (now - earliest).TotalMinutes);
            positions.Add(new InventedPosition(Id(n), side, since, taken));
        }

        // The prototype's named authors (design 8.12: nobody opened anything; the description is a moderator's).
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
        return (members, new InventedAppealSpec(Bill, Description, opened, positions, reasons));
    }
}
