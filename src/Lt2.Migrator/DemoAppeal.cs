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

    /// <summary>
    /// What happened, for the console. <c>AiDescription</c>: <c>created</c>, <c>already there</c> or <c>left as it is</c>.
    /// <c>Organisations</c>: how many of the invented organisations on <see cref="Bill"/> were created (design 8.14.14).
    /// </summary>
    public sealed record Outcome(string Status, int AccountsCreated, string AiDescription, int Organisations = 0, int TermsAccepted = 0, string ClosedBill = "left as it is");

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

        // Design 8.22.2: accounts made by an earlier run, before the terms of use (or before their newest version), accept them
        // now, so re-running this command brings an existing local database up to date.
        var termsAccepted = await InventedMembers.AcceptTermsAsync(connectionString, people.Members, cancellationToken);
        var outcome = await InventedRecord.SeedAsync(connectionString, people.Record, cancellationToken);
        if (outcome == InventedRecordOutcome.BillTaken)
        {
            throw new InvalidOperationException($"Refused: {Bill} already has a record that people took part on; the demonstration fills only a bill nobody took part in.");
        }

        // The invented AI summary links to the bill's own e-seimas address from the (invented) parliament data.
        var address = await ParliamentMaintenance.BillAddressAsync(connectionString, AiBill, cancellationToken);
        var summary = await InventedRecord.SeedAiDescriptionAsync(connectionString, AiBill, AiDescription, address, now.AddDays(-2), cancellationToken);

        // Design 8.14.14: invented organisations with statements on the bill, so "Organizacijų pozicijos" can be seen. Their
        // representatives are invented accounts of their own, with no side on any bill.
        var organisations = Organisations();
        await InventedMembers.CreateAsync(connectionString, OrganisationMembers(now), cancellationToken);
        termsAccepted += await InventedMembers.AcceptTermsAsync(connectionString, OrganisationMembers(now), cancellationToken);
        var organisationsCreated = await InventedOrganisations.SeedAsync(connectionString, organisations, now, cancellationToken);

        // Design 8.23: invented comments and "Sutinku" under the first argument and the first statement, by the organisations'
        // invented representatives as persons, so "Aptarimas" and its controls can be seen and walked before the islands start.
        await SeedCommentsAsync(connectionString, now, cancellationToken);

        // Design 8.26.5: a bill the Seimas voted on finally, with invented positions, so the analytics page's comparison can be seen.
        var (closedBill, closedCreated, closedTerms) = await SeedClosedBillAsync(connectionString, cancellationToken);
        created += closedCreated;
        termsAccepted += closedTerms;
        return new Outcome(
            outcome == InventedRecordOutcome.Created ? "created" : "already there",
            created,
            summary switch
            {
                InventedRecordOutcome.Created => "created",
                InventedRecordOutcome.AlreadyThere => "already there",
                _ => "left as it is",
            },
            organisationsCreated,
            termsAccepted,
            closedBill);
    }

    /// <summary>Two comments under the bill's first published argument and one under its first statement, with "Sutinku"; repeatable.</summary>
    private static async Task SeedCommentsAsync(string connectionString, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = new Npgsql.NpgsqlCommand(
            """
            WITH reason AS (
                SELECT r.id FROM appeals.reason r JOIN appeals.bill_record a ON a.id = r.record_id
                WHERE a.bill_number = @bill AND r.status = 'published' ORDER BY r.created_at, r.id LIMIT 1),
            statement AS (
                SELECT s.id FROM appeals.organisation_statement s WHERE s.bill_number = @bill AND s.status = 'published' ORDER BY s.id LIMIT 1)
            INSERT INTO appeals.comment (id, target_kind, target_id, bill_number, speaks_as, author_user_id, text, status, created_at)
            SELECT c.id, c.kind, c.target, @bill, 'person', c.author, c.text, 'published', c.at
            FROM (
                SELECT @c1 AS id, 'reason' AS kind, (SELECT id FROM reason) AS target, @m1 AS author,
                       'Ar skaičiuota, kiek tai kainuotų savivaldybėms? Būtų gerai matyti šaltinį.' AS text, @t1 AS at
                UNION ALL SELECT @c2, 'reason', (SELECT id FROM reason), @m2, 'Šaltinis: https://pavyzdys.test/tyrimas (išgalvotas).', @t2
                UNION ALL SELECT @c3, 'statement', (SELECT id FROM statement), @m3, 'Ar asociacija skaičiavo poveikį mažoms įmonėms?', @t3
            ) c
            WHERE c.target IS NOT NULL
            ON CONFLICT (id) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue("bill", Bill);
            command.Parameters.AddWithValue("c1", Comment(1));
            command.Parameters.AddWithValue("c2", Comment(2));
            command.Parameters.AddWithValue("c3", Comment(3));
            command.Parameters.AddWithValue("m1", OrganisationMember(1));
            command.Parameters.AddWithValue("m2", OrganisationMember(2));
            command.Parameters.AddWithValue("m3", OrganisationMember(3));
            command.Parameters.AddWithValue("t1", now.AddDays(-3));
            command.Parameters.AddWithValue("t2", now.AddDays(-2));
            command.Parameters.AddWithValue("t3", now.AddDays(-1));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var agreements = new Npgsql.NpgsqlCommand(
            """
            INSERT INTO appeals.comment_agreement (comment_id, user_id, created_at)
            SELECT c.comment, c.person, @at FROM (VALUES (@c1, @m2), (@c1, @m3), (@c2, @m1)) AS c(comment, person)
            WHERE EXISTS (SELECT 1 FROM appeals.comment WHERE id = c.comment)
            ON CONFLICT DO NOTHING
            """,
            connection);
        agreements.Parameters.AddWithValue("c1", Comment(1));
        agreements.Parameters.AddWithValue("c2", Comment(2));
        agreements.Parameters.AddWithValue("m1", OrganisationMember(1));
        agreements.Parameters.AddWithValue("m2", OrganisationMember(2));
        agreements.Parameters.AddWithValue("m3", OrganisationMember(3));
        agreements.Parameters.AddWithValue("at", now.AddHours(-12));
        await agreements.ExecuteNonQueryAsync(cancellationToken);

        static Guid Comment(int n) => new($"0193b000-0412-7000-a000-{n:D12}");
    }
    /// <summary>The invented organisations' representatives: accounts of their own, never given a side.</summary>
    public static IReadOnlyList<InventedMember> OrganisationMembers(DateTimeOffset now) =>
    [
        new(OrganisationMember(1), $"demo.org1@{InventedMembers.Domain}", "Laura P.", now.AddDays(-200)),
        new(OrganisationMember(2), $"demo.org2@{InventedMembers.Domain}", "Tomas V.", now.AddDays(-150)),
        new(OrganisationMember(3), $"demo.org3@{InventedMembers.Domain}", "Eglė S.", now.AddDays(-120)),
    ];

    private static Guid OrganisationMember(int n) => new($"0193b000-0412-7000-9000-{n:D12}");

    /// <summary>
    /// Three invented organisations on <see cref="Bill"/> (design 8.14.14): one confirmed by an invented domain asking to
    /// vote for, with a goal; one unconfirmed asking to vote against; one confirmed by a moderator with remarks. Names that
    /// say they are examples, on the reserved <c>pavyzdys.test</c> domain.
    /// </summary>
    public static IReadOnlyList<InventedOrganisationSpec> Organisations() =>
    [
        new(
            "pavyzdine-mokesciu-aiskumo-asociacija",
            "Pavyzdinė mokesčių aiškumo asociacija",
            "Išgalvota organizacija demonstraciniams duomenims: aiškina, kaip mokesčiai paliečia skirtingas pajamų grupes.",
            "https://mokesciai.pavyzdys.test",
            "domain",
            OrganisationMember(1),
            new InventedStatement(
                Bill,
                "for",
                "Prašome balsuoti už. Trečias tarifas didžiausioms pajamoms sumažintų skirtumą tarp to, kiek nuo pajamų sumoka samdomi darbuotojai ir didžiausias pajamas gaunantys žmonės.\nSiūlome kartu įvertinti, kaip pataisa paveiks savivaldybių biudžetus.",
                "https://mokesciai.pavyzdys.test/pozicija",
                8000)),
        new(
            "pavyzdinis-seimos-verslu-samburis",
            "Pavyzdinis šeimos verslų sambūris",
            "Išgalvota organizacija demonstraciniams duomenims: smulkiųjų šeimos įmonių savininkai.",
            "https://seimos-verslai.pavyzdys.test",
            null,
            OrganisationMember(2),
            new InventedStatement(
                Bill,
                "against",
                "Prašome balsuoti prieš. Akcijų pardavimo įskaičiavimas į tą pačią ribą paliestų vienkartinius įvykius, pavyzdžiui, šeimos verslo pardavimą išeinant į pensiją.",
                null,
                null)),
        new(
            "pavyzdine-viesuju-finansu-stebesena",
            "Pavyzdinė viešųjų finansų stebėsena",
            "Išgalvota organizacija demonstraciniams duomenims: stebi, kaip rengiami biudžeto pakeitimai.",
            null,
            "moderator",
            OrganisationMember(3),
            new InventedStatement(
                Bill,
                "remarks",
                "Pastabos: aiškinamajame rašte nenurodyta, kiek žmonių paliestų naujas tarifas. Prašome šį skaičių paskelbti prieš svarstymą.",
                "https://finansai.pavyzdys.test/pastabos",
                null)),
    ];

    /// <summary>The invented people and the record, the same on every run (fixed seed and identifiers).</summary>
    public static (IReadOnlyList<InventedMember> Members, InventedRecordSpec Record) Build(DateTimeOffset now)
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

            // Sides taken at any time since the record opened (not all on day one), so the invented data does not read as
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
        return (members, new InventedRecordSpec(Bill, Description, opened, positions, reasons));
    }

    /// <summary>
    /// Design 8.26.5: the stand-in's local overlay holds <see cref="ClosedBill"/> with an adoption vote nine days ago (with
    /// per-MP ballots); this gives it a record with invented positions taken before the vote and closes it as the closing job
    /// would, so the dot plot, its table and a member's comparison can be seen on the local stack. Positions only: the
    /// example numbers of <see cref="Bill"/> stay as they are.
    /// </summary>
    public const string ClosedBill = "XVP-0707";
    public const int ClosedFor = 1240;
    public const int ClosedForOlder = 1100;
    public const int ClosedAgainst = 860;
    public const int ClosedAgainstOlder = 760;

    public const string ClosedDescription =
        "Viešųjų pirkimų sutartys ir jų pakeitimai būtų skelbiami per tris darbo dienas nuo pasirašymo, o mažos vertės pirkimų riba pakelta iki 15 000 eurų.";

    /// <summary>Seeds and closes <see cref="ClosedBill"/>; "not imported yet" while the stand-in's vote is not in the data.</summary>
    private static async Task<(string Status, int Created, int TermsAccepted)> SeedClosedBillAsync(string connectionString, CancellationToken cancellationToken)
    {
        if (await ParliamentMaintenance.FinalVoteAsync(connectionString, ClosedBill, cancellationToken) is not { } vote)
        {
            return ("not imported yet", 0, 0);
        }

        var (members, record) = BuildClosed(vote.HeldAt);
        var created = await InventedMembers.CreateAsync(connectionString, members, cancellationToken);
        var terms = await InventedMembers.AcceptTermsAsync(connectionString, members, cancellationToken);
        var seeded = await InventedRecord.SeedAsync(connectionString, record, cancellationToken);
        if (seeded == InventedRecordOutcome.BillTaken)
        {
            return ("left as it is", created, terms);
        }

        var closed = await InventedRecord.CloseAsync(connectionString, ClosedBill, vote.VoteId, vote.HeldAt.AddHours(1), cancellationToken);
        return (closed ? "created and closed" : "already there", created, terms);
    }

    /// <summary>The invented people and the record of <see cref="ClosedBill"/>, the same on every run (fixed seed and identifiers).</summary>
    public static (IReadOnlyList<InventedMember> Members, InventedRecordSpec Record) BuildClosed(DateTimeOffset votedAt)
    {
        var random = new Random(707);
        var members = new List<InventedMember>();
        var positions = new List<InventedPosition>();
        var opened = votedAt.AddDays(-50);

        Guid Id(int n) => new($"0193b000-0707-7000-8000-{n:D12}");

        void Add(int n, string side, bool older, string? name = null)
        {
            var since = older ? votedAt.AddDays(-random.Next(31, 420)) : votedAt.AddDays(-random.Next(1, 29)).AddMinutes(-random.Next(0, 1440));
            members.Add(new InventedMember(Id(n), $"demo.closed.{n:D5}@{InventedMembers.Domain}", name, since));
            var earliest = since > opened ? since : opened;
            var latest = votedAt.AddHours(-2);
            var taken = earliest + TimeSpan.FromMinutes(random.NextDouble() * Math.Max(1, (latest - earliest).TotalMinutes));
            positions.Add(new InventedPosition(Id(n), side, since, taken));
        }

        Add(1, "for", true, "Saulius K.");
        Add(2, "against", true, "Ieva D.");
        var n = 3;
        for (var i = 1; i < ClosedFor; i++)
        {
            Add(n++, "for", i < ClosedForOlder);
        }

        for (var i = 1; i < ClosedAgainst; i++)
        {
            Add(n++, "against", i < ClosedAgainstOlder);
        }

        var forIds = positions.Where(p => p.Side == "for").Select(p => p.UserId).ToList();
        var againstIds = positions.Where(p => p.Side == "against").Select(p => p.UserId).ToList();
        var reasons = new List<InventedReason>
        {
            new(Id(1), "for", "Greitai paskelbtos sutartys leidžia laiku pastebėti neįprastus pakeitimus.", forIds.Take(410).ToList()),
            new(Id(2), "against", "Pakelta mažos vertės pirkimų riba sumažintų konkurenciją mažuose pirkimuose.", againstIds.Take(290).ToList()),
        };
        return (members, new InventedRecordSpec(ClosedBill, ClosedDescription, opened, positions, reasons));
    }
}