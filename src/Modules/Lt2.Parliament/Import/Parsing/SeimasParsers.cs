using System.Xml.Linq;

namespace Lt2.Parliament.Import.Parsing;

/// <summary>
/// Element and attribute names of the open-data responses. Each one is a line of the register of assumptions
/// (tests/Lt2.Parliament.ImportTests/Seimas/ASSUMPTIONS.md), marked there as seen in a live response or assumed.
/// </summary>
internal static class N
{
    // Terms
    public const string Term = "SeimoKadencija";
    public const string TermId = "kadencijos_id";
    public const string Name = "pavadinimas";
    public const string From = "data_nuo";
    public const string To = "data_iki";

    // MPs
    public const string Member = "SeimoNarys";
    public const string PersonId = "asmens_id";
    public const string FirstName = "vardas";
    public const string LastName = "pavardė";
    public const string Gender = "lytis";
    public const string NominatingParty = "iškėlusi_partija";
    public const string ElectedHow = "išrinkimo_būdas";
    public const string Constituency = "apygarda";
    public const string TermsServed = "kadencijų_skaičius";
    public const string Membership = "Pareigos";
    public const string BodyId = "padalinio_id";
    public const string BodyName = "padalinio_pavadinimas";
    public const string Role = "pareigos";
    public const string Contact = "Kontaktai";
    public const string ContactKind = "rūšis";
    public const string ContactValue = "reikšmė";

    // Sessions and sittings
    public const string Session = "SeimoSesija";
    public const string SessionId = "sesijos_id";
    public const string Sitting = "SeimoPosėdis";
    public const string SittingId = "posėdžio_id";
    public const string Number = "numeris";
    public const string SittingKind = "tipas";
    public const string Starts = "pradžia";
    public const string Ends = "pabaiga";

    // Agenda and proceedings
    public const string AgendaItem = "DarbotvarkėsKlausimas";
    public const string AgendaItemId = "darbotvarkės_klausimo_id";
    public const string Stage = "stadija";
    public const string Document = "Dokumentas";
    public const string BillNumber = "registracijos_numeris";
    public const string Link = "nuoroda";
    public const string Vote = "Balsavimas";
    public const string VoteId = "balsavimo_id";
    public const string VoteTime = "balsavimo_laikas";
    public const string Wording = "aprašas";
    public const string VoteType = "balsavimo_tipas";

    // Vote results
    public const string Totals = "BendriBalsavimoRezultatai";
    public const string Seats = "viso";
    public const string Voted = "balsavo";
    public const string For = "už";
    public const string Against = "prieš";
    public const string Abstain = "susilaikė";
    public const string Note = "komentaras";
    public const string Ballot = "IndividualusBalsavimoRezultatas";
    public const string Faction = "frakcija";
    public const string Choice = "kaip_balsavo";
}

/// <summary>
/// Thin parsers: one per service, from the raw body to records, no database and no rules beyond reading. Their shapes
/// are the part most likely to change once live responses are seen, so they stay apart from the import logic.
/// </summary>
internal static class SeimasParsers
{
    public static IReadOnlyList<ParsedTerm> Terms(byte[] body) =>
        XmlRead.Load(body).All(N.Term)
            .Select(e => new ParsedTerm(checked((int)e.RequiredId(N.TermId)), e.Required(N.Name), e.RequiredDate(N.From), e.OptionalDate(N.To)))
            .ToList();

    public static IReadOnlyList<ParsedMember> Members(byte[] body) =>
        XmlRead.Load(body).All(N.Member).Select(Member).ToList();

    private static ParsedMember Member(XElement e) => new(
        e.RequiredId(N.PersonId),
        e.Required(N.FirstName),
        e.Required(N.LastName),
        e.Optional(N.Gender),
        e.RequiredDate(N.From),
        e.OptionalDate(N.To),
        e.Optional(N.NominatingParty),
        e.Optional(N.ElectedHow),
        e.Optional(N.Constituency),
        e.OptionalCount(N.TermsServed),
        e.Children(N.Membership)
            .Select(m => new ParsedMembership(m.RequiredId(N.BodyId), m.Required(N.BodyName), m.Optional(N.Role) ?? "", m.RequiredDate(N.From), m.OptionalDate(N.To)))
            .ToList(),
        e.Children(N.Contact)
            .Select(c => new ParsedContact(c.Required(N.ContactKind), c.Required(N.ContactValue)))
            .ToList());

    public static IReadOnlyList<ParsedSession> Sessions(byte[] body) =>
        XmlRead.Load(body).All(N.Session)
            .Select(e => new ParsedSession(e.RequiredId(N.SessionId), e.Required(N.Name), e.RequiredDate(N.From), e.OptionalDate(N.To)))
            .ToList();

    public static IReadOnlyList<ParsedSitting> Sittings(byte[] body) =>
        XmlRead.Load(body).All(N.Sitting)
            .Select(e => new ParsedSitting(e.RequiredId(N.SittingId), e.Optional(N.Number), e.Optional(N.SittingKind), e.RequiredTime(N.Starts), e.OptionalTime(N.Ends)))
            .ToList();

    public static IReadOnlyList<ParsedAgendaItem> Agenda(byte[] body) =>
        XmlRead.Load(body).All(N.AgendaItem)
            .Select(e =>
            {
                var document = e.Children(N.Document).FirstOrDefault(d => d.Optional(N.BillNumber) is not null);
                return new ParsedAgendaItem(
                    e.RequiredId(N.AgendaItemId),
                    e.Optional(N.Number),
                    e.Required(N.Name),
                    e.Optional(N.Stage),
                    document?.Optional(N.BillNumber),
                    document?.Optional(N.Link));
            })
            .ToList();

    /// <summary>The votes of a sitting's items. A vote is linked to the agenda item it is nested in.</summary>
    public static IReadOnlyList<ParsedVoteReference> Proceedings(byte[] body) =>
        XmlRead.Load(body).All(N.Vote)
            .Select(e =>
            {
                var item = e.Enclosing(N.AgendaItem) ?? throw new SeimasFormatException($"<{N.Vote}> is not inside a <{N.AgendaItem}>.");
                var wording = e.Optional(N.Wording) ?? "";
                var type = e.Optional(N.VoteType);
                return new ParsedVoteReference(e.RequiredId(N.VoteId), item.RequiredId(N.AgendaItemId), e.RequiredTime(N.VoteTime), wording, type, VoteRules.IsConsensus(type, wording));
            })
            .ToList();

    /// <summary>Totals and the per-MP rows of one vote. Without a totals element the document is empty (unknown vote).</summary>
    public static ParsedVoteResult? VoteResult(byte[] body)
    {
        var document = XmlRead.Load(body);
        var totals = document.All(N.Totals).FirstOrDefault();
        if (totals is null)
        {
            return null;
        }

        var ballots = document.All(N.Ballot)
            .Select(b => new ParsedBallot(
                b.OptionalId(N.PersonId),
                b.Required(N.FirstName),
                b.Required(N.LastName),
                b.Optional(N.Faction),
                VoteRules.Choice(b.Raw(N.Choice))))
            .ToList();
        return new ParsedVoteResult(
            totals.RequiredId(N.VoteId),
            new VoteTotals(totals.OptionalCount(N.For), totals.OptionalCount(N.Against), totals.OptionalCount(N.Abstain), totals.OptionalCount(N.Voted), totals.OptionalCount(N.Seats)),
            totals.Optional(N.Note),
            ballots);
    }
}
