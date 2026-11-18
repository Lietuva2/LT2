using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Parliament;
using Lt2.SharedKernel;

namespace Lt2.Migrator;

/// <summary>
/// Design 8.26.5: the invented closed bills of the stand-in's local overlay (<c>tools/demo_closed_bills.py</c> writes their
/// adoption votes into <c>tools/Lt2.SeimasStandIn/Recent</c>; the numbers here are the same). <c>--demo-appeal</c> gives
/// each, once its vote is imported, a record with invented positions taken before the vote, from the invented
/// demonstration accounts only (<see cref="DemoAppeal.Build"/> and <see cref="DemoAppeal.BuildClosed"/>: fixed identifiers,
/// addresses on <see cref="InventedMembers.Domain"/>), and closes it as the closing job would, as XVP-0707. Fixed seeds:
/// the same database gives the same sides, and a second run finds every record already there.
/// </summary>
public static class DemoClosedBills
{
    /// <summary>
    /// One invented bill: how many invented members took a side, whether their majority is the Seimas's, and the share of
    /// their larger side. Under 20 positions the bill shows "maža imtis" (design 8.26.2) and the split is whatever it is.
    /// </summary>
    public sealed record Spec(string Number, string Description, int Positions, bool AgreesWithSeimas, double Larger, string? ArgumentFor = null, string? ArgumentAgainst = null);

    public static readonly IReadOnlyList<Spec> Bills =
    [
        new("XVP-0701", "Seniūnijos galėtų pačios skirstyti iki 5 % savivaldybės biudžeto vietos bendruomenių projektams.", 640, true, 0.64),
        new("XVP-0702", "Darbuotojai, auginantys vaikus iki 12 metų, galėtų reikalauti lankstaus darbo laiko, o viršvalandžių riba per metus padidėtų iki 220 valandų.", 2350, false, 0.58,
            "Lankstus darbo laikas padėtų tėvams suderinti darbą ir šeimą.", "Didesnė viršvalandžių riba reikštų ilgesnes darbo savaites."),
        new("XVP-0703", "Šeimos gydytojo konsultacija nuotoliu būtų apmokama taip pat kaip apsilankymas poliklinikoje.", 1480, true, 0.71),
        new("XVP-0704", "Mokyklos galėtų jungti klases iki 30 mokinių, o neformalusis švietimas būtų finansuojamas pagal vieną krepšelį visoje šalyje.", 1820, true, 0.55),
        new("XVP-0705", "Kelių programos lėšos vietiniams keliams būtų skirstomos pagal eismo intensyvumą, o ne tik pagal kelių ilgį.", 310, true, 0.82),
        new("XVP-0706", "Pagrindiniams maisto produktams būtų taikomas 9 % PVM tarifas vietoj 21 %.", 2140, false, 0.61,
            "Mažesnis PVM maistui palengvintų gyvenimą mažas pajamas gaunantiems žmonėms.", "Lengvata kainuotų biudžetui, o kainos parduotuvėse nebūtinai sumažėtų."),
        new("XVP-0708", "Kirtimai saugomose teritorijose būtų leidžiami tik gavus Aplinkos ministerijos leidimą, o kirtimų planai būtų skelbiami viešai.", 960, false, 0.67),
        new("XVP-0709", "Įmonės, išmetančios teršalus, kas ketvirtį skelbtų taršos matavimus savo interneto svetainėse.", 420, true, 0.73),
        new("XVP-0710", "Pagalba į namus būtų teikiama ir savaitgaliais, o jos laukti tektų ne ilgiau kaip 30 dienų.", 18, true, 0.6),
        new("XVP-0711", "Konkursai į valstybės tarnybą vyktų nuotoliu, o pokalbių vertinimo kriterijai būtų skelbiami iš anksto.", 230, true, 0.6),
        new("XVP-0712", "Elektros skirstymo tinklų operatorius turėtų prijungti naujus vartotojus per 60 dienų nuo prašymo.", 1150, true, 0.52),
        new("XVP-0713", "Saulės elektrinėms iki 50 kW ant pastatų stogų nereikėtų statybą leidžiančio dokumento.", 870, true, 0.78,
            "Paprastesnė tvarka paspartintų saulės elektrinių įrengimą ant namų stogų."),
        new("XVP-0714", "Studentams visus metus būtų taikoma 50 % nuolaida tarpmiestinių autobusų ir traukinių bilietams.", 12, true, 0.6),
        new("XVP-0715", "Valstybės finansuojamos studijų vietos būtų skirstomos pagal darbo rinkos poreikių prognozę, o ne tik pagal stojančiųjų pasirinkimą.", 540, false, 0.57),
        new("XVP-0716", "Detaliųjų planų projektai būtų skelbiami vienoje valstybės sistemoje, o pastaboms teikti būtų skiriama 30 dienų.", 95, true, 0.62),
        new("XVP-0717", "Gyvenamajam namui iki 80 m² pastatyti nereikėtų statybą leidžiančio dokumento, pakaktų pranešti savivaldybei.", 310, false, 0.54),
        new("XVP-0718", "Alkoholiu būtų leidžiama prekiauti sekmadieniais iki 20 valandos, o degalinėse – visą parą.", 1960, true, 0.63),
        new("XVP-0719", "Elektroninių cigarečių skysčiai su kvapiosiomis medžiagomis būtų uždrausti, o pardavėjai tikrintų visų pirkėjų iki 25 metų amžių.", 740, true, 0.69),
        new("XVP-0720", "Visi šunys ir katės būtų ženklinami mikroschemomis ir registruojami vienoje sistemoje.", 1630, true, 0.86,
            "Paženklintus gyvūnus lengviau grąžinti šeimininkams, kai jie pasiklysta."),
        new("XVP-0721", "Nekilnojamojo turto mokestis būtų taikomas antram ir kitiems būstams, kai jų vertė kartu viršija 150 000 eurų.", 2480, false, 0.74,
            "Mokestis skatintų nuomoti ar parduoti tuščiai stovinčius butus.", "Mokestis paliestų žmones, paveldėjusius seną sodybą ar butą kitame mieste."),
        new("XVP-0722", "Akcizas degalams būtų didinamas kasmet po 3 % iki 2030 metų.", 680, false, 0.56),
        new("XVP-0723", "Savarankiškai dirbantys asmenys galėtų mokėti socialinio draudimo įmokas kas mėnesį, o ne tik kartą per metus.", 1210, true, 0.6),
        new("XVP-0724", "Vaiko pinigai šeimoms, auginančioms tris ir daugiau vaikų, padidėtų 20 %.", 1390, true, 0.81,
            "Didesnė išmoka padėtų gausioms šeimoms, kurių išlaidos auga greičiau nei pajamos."),
        new("XVP-0725", "Interneto žiniasklaidos priemonės privalėtų skelbti savo valdytojus ir finansavimo šaltinius.", 48, false, 0.58),
        new("XVP-0726", "Vaizdo kameros daugiabučių laiptinėse būtų leidžiamos gavus daugumos butų savininkų sutikimą.", 7, true, 0.6),
        new("XVP-0727", "Savivaldybės ir ligoninės privalėtų per 24 valandas pranešti apie kibernetinius incidentus.", 150, true, 0.66),
        new("XVP-0728", "Šauktinių tarnyba būtų prailginta iki 12 mėnesių, o tarnavusieji gautų papildomų balų stodami į aukštąsias mokyklas.", 1720, true, 0.59),
        new("XVP-0729", "Daugiabučių savininkų bendrijos turėtų paskirti asmenį, atsakingą už informaciją apie artimiausias priedangas.", 26, true, 0.65),
        new("XVP-0730", "Trumpalaikės nuomos būstai būtų registruojami viename registre, o nuomotojai mokėtų turistinį mokestį.", 63, false, 0.6),
        new("XVP-0731", "Saugomų pastatų savininkams būtų kompensuojama iki 50 % tvarkybos darbų išlaidų.", 380, true, 0.57),
    ];

    /// <summary>What a run did with the invented closed bills: records written and closed, already there, left as they are, not imported yet; positions written.</summary>
    public sealed record Outcome(int Created, int AlreadyThere, int LeftAsItIs, int NotImported, int Positions)
    {
        public override string ToString() =>
            $"{Created} created and closed, {AlreadyThere} already there, {LeftAsItIs} left as they are"
            + (NotImported > 0 ? $", {NotImported} not imported yet" : "")
            + $"; invented positions written: {Positions}";
    }

    public static async Task<Outcome> SeedAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Only the demonstration's own invented accounts: their fixed identifiers, and of those only the ones that exist with an
        // address on the reserved domain and the positions consent (InventedMembers.SinceAsync). The organisations'
        // representatives are left out: they never take a side.
        var ids = DemoAppeal.Build(DateTimeOffset.UnixEpoch).Members.Select(m => m.Id)
            .Concat(DemoAppeal.BuildClosed(DateTimeOffset.UnixEpoch).Members.Select(m => m.Id))
            .ToList();
        var since = await InventedMembers.SinceAsync(connectionString, ids, cancellationToken);
        var pool = since.OrderBy(p => p.Key).ToList();
        int created = 0, already = 0, left = 0, notImported = 0, positions = 0;
        foreach (var bill in Bills)
        {
            if (await ParliamentMaintenance.FinalVoteNumbersAsync(connectionString, bill.Number, cancellationToken) is not { } vote
                || AnalyticsRules.SeimasMajorityFor(vote.Numbers) is not { } seimasFor)
            {
                notImported++;
                continue;
            }

            var spec = Build(bill, vote.HeldAt, seimasFor, pool);
            var seeded = await InventedRecord.SeedAsync(connectionString, spec, cancellationToken);
            if (seeded == InventedRecordOutcome.BillTaken)
            {
                left++;
                continue;
            }

            var closed = await InventedRecord.CloseAsync(connectionString, bill.Number, vote.VoteId, vote.HeldAt.AddHours(1), cancellationToken);
            if (seeded == InventedRecordOutcome.Created)
            {
                created++;
                positions += spec.Positions.Count;
            }
            else if (closed)
            {
                created++;
            }
            else
            {
                already++;
            }
        }

        return new Outcome(created, already, left, notImported, positions);
    }

    /// <summary>
    /// The record of one invented bill: its positions from the accounts registered before the vote, taken between the
    /// record's opening (30 to 90 days before the vote) and two hours before the vote, the same for the same accounts.
    /// </summary>
    public static InventedRecordSpec Build(Spec bill, DateTimeOffset votedAt, bool seimasFor, IReadOnlyList<KeyValuePair<Guid, DateTimeOffset>> pool)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(pool);
        var random = new Random(int.Parse(bill.Number.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture) * 7919);
        var opened = votedAt.AddDays(-random.Next(30, 91));
        var latest = votedAt.AddHours(-2);
        var eligible = pool.Where(p => p.Value < latest.AddHours(-1)).ToList();
        for (var i = eligible.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (eligible[i], eligible[j]) = (eligible[j], eligible[i]);
        }

        var chosen = eligible.Take(bill.Positions).ToList();
        var larger = (int)Math.Ceiling(chosen.Count * bill.Larger);
        if (larger * 2 == chosen.Count)
        {
            larger++; // no tie: a tie has no majority and the bill would not be compared
        }

        var membersFor = bill.AgreesWithSeimas == seimasFor;
        var positions = new List<InventedPosition>();
        for (var i = 0; i < chosen.Count; i++)
        {
            var (user, registered) = (chosen[i].Key, chosen[i].Value);
            var side = (i < larger) == membersFor ? "for" : "against";
            var earliest = registered > opened ? registered : opened;
            var taken = earliest + TimeSpan.FromMinutes(random.NextDouble() * Math.Max(1, (latest - earliest).TotalMinutes));
            positions.Add(new InventedPosition(user, side, registered, taken));
        }

        // Shuffled so the sides are not in blocks by account; the first position stays the marker of "already there".
        for (var i = positions.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        var reasons = new List<InventedReason>();
        void Argument(string side, string? text)
        {
            var mine = positions.Where(p => p.Side == side).Select(p => p.UserId).ToList();
            if (text is not null && mine.Count > 1)
            {
                reasons.Add(new InventedReason(mine[0], side, text, mine.Skip(1).Take(mine.Count / (4 + random.Next(4))).ToList()));
            }
        }

        Argument("for", bill.ArgumentFor);
        Argument("against", bill.ArgumentAgainst);
        return new InventedRecordSpec(bill.Number, bill.Description, opened, positions, reasons);
    }
}
