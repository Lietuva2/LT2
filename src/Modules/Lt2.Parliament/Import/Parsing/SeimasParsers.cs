using System.Text.RegularExpressions;
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
    public const string TermsServed = "kadencijų_skaičius";
    public const string Membership = "Pareigos";
    public const string BodyId = "padalinio_id";
    public const string BodyName = "padalinio_pavadinimas";
    public const string GroupId = "parlamentinės_grupės_id";
    public const string GroupName = "parlamentinės_grupės_pavadinimas";
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
    public const string AgendaStage = "KlausimoStadija";
    public const string AgendaDocumentLink = "dokumento_nuoroda";
    public const string Discussion = "darbotvarkes-klausimas";
    public const string DiscussionId = "svarst_kl_stad_id";
    public const string DiscussionAgendaId = "kl_stad_id";
    public const string DiscussionNumber = "nr";
    public const string Stage = "stadija";
    public const string Vote = "balsavimas";
    public const string ProceedingsVoteId = "bals_id";
    public const string ProceedingsVoteTime = "nuo";
    public const string Wording = "aprasas";
    public const string VoteHeading = "antraste";
    public const string ResultVote = "SeimoNariųBalsavimas";
    public const string VoteId = "balsavimo_id";
    public const string VoteTime = "balsavimo_laikas";

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
internal static partial class SeimasParsers
{
    // These IDs come from separate Seimas sequences. Keep them separate from agenda and body IDs locally.
    private const long DiscussionIdOffset = long.MinValue / 2;
    private const long GroupIdOffset = long.MinValue / 2;
    private const string DistrictEnding = " apygardoje";

    [GeneratedRegex(@"\(Nr\.\s*(?<number>[IVX]+P-\d+)(?:\(\d+\))?\)$", RegexOptions.CultureInvariant)]
    private static partial Regex BillNumberSuffix();

    public static IReadOnlyList<ParsedTerm> Terms(byte[] body) =>
        XmlRead.Load(body).All(N.Term)
            .Select(e => new ParsedTerm(checked((int)e.RequiredId(N.TermId)), e.Required(N.Name), e.RequiredDate(N.From), e.OptionalDate(N.To)))
            .ToList();

    public static IReadOnlyList<ParsedMember> Members(byte[] body) =>
        XmlRead.Load(body).All(N.Member).Select(Member).ToList();

    private static ParsedMember Member(XElement e)
    {
        var elected = e.Optional(N.ElectedHow);
        var district = elected?.EndsWith(DistrictEnding, StringComparison.Ordinal) == true
            ? elected[..^DistrictEnding.Length] : null;
        return new(
            e.RequiredId(N.PersonId),
            e.Required(N.FirstName),
            e.Required(N.LastName),
            e.Optional(N.Gender),
            e.RequiredDate(N.From),
            e.OptionalDate(N.To),
            e.Optional(N.NominatingParty),
            district is null ? elected : null,
            district,
            e.OptionalCount(N.TermsServed),
            e.Children(N.Membership)
                .Select(Membership).OfType<ParsedMembership>()
                .ToList(),
            e.Children(N.Contact)
                .Select(c => (Kind: c.Optional(N.ContactKind), Value: c.Optional(N.ContactValue)))
                .Where(c => c.Kind is not null && c.Value is not null)
                .Select(c => new ParsedContact(c.Kind!, c.Value!))
                .ToList());
    }

    private static ParsedMembership? Membership(XElement e)
    {
        var bodyId = e.OptionalId(N.BodyId);
        var groupId = e.OptionalId(N.GroupId);
        var bodyName = e.Optional(N.BodyName);
        var groupName = e.Optional(N.GroupName);
        var name = bodyId.HasValue ? bodyName : groupName;
        var id = bodyId ?? checked(GroupIdOffset + groupId);
        return id is { } key && name is not null
            ? new ParsedMembership(key, name, bodyId.HasValue ? VoteRules.Body(name) : BodyKind.Group,
                e.Optional(N.Role) ?? "", e.RequiredDate(N.From), e.OptionalDate(N.To))
            : null;
    }

    public static IReadOnlyList<ParsedSession> Sessions(byte[] body) =>
        XmlRead.Load(body).All(N.Session)
            .Select(e => new ParsedSession(e.RequiredId(N.SessionId), e.Required(N.Name), e.RequiredDate(N.From), e.OptionalDate(N.To)))
            .ToList();

    public static IReadOnlyList<ParsedSitting> Sittings(byte[] body) =>
        XmlRead.Load(body).All(N.Sitting)
            .Select(e => (Row: e, StartsAt: e.OptionalTime(N.Starts)))
            .Where(s => s.StartsAt.HasValue)
            .Select(s => new ParsedSitting(s.Row.RequiredId(N.SittingId), s.Row.Optional(N.Number), s.Row.Optional(N.SittingKind),
                s.StartsAt!.Value, s.Row.OptionalTime(N.Ends)))
            .ToList();

    public static IReadOnlyList<ParsedAgendaItem> Agenda(byte[] body) =>
        XmlRead.Load(body).All(N.AgendaItem)
            .SelectMany(e =>
            {
                var (title, number) = BillInTitle(e.Optional(N.Name) ?? "");
                return e.Children(N.AgendaStage).Select(stage => new ParsedAgendaItem(
                    stage.RequiredId(N.AgendaItemId), e.Optional(N.Number), title, stage.Optional(N.Name),
                    number, stage.Optional(N.AgendaDocumentLink)));
            })
            .ToList();

    /// <summary>The sitting as it happened, including votes on items absent from its published agenda.</summary>
    public static ParsedProceedings Proceedings(byte[] body)
    {
        var items = new Dictionary<long, ParsedAgendaItem>();
        var votes = new Dictionary<long, (ParsedVoteReference Vote, bool Linked)>();
        foreach (var e in XmlRead.Load(body).All(N.Vote))
        {
            var discussion = e.Enclosing(N.Discussion) ?? throw new SeimasFormatException($"<{N.Vote}> is not inside a <{N.Discussion}>.");
            var discussionId = discussion.OptionalId(N.DiscussionId);
            var agendaId = discussion.OptionalId(N.DiscussionAgendaId);
            var itemId = agendaId ?? (checked(DiscussionIdOffset + discussionId)
                ?? throw new SeimasFormatException($"<{N.Discussion}> has no {N.DiscussionId} or {N.DiscussionAgendaId}."));
            var stage = discussion.TextOf(N.Stage);
            var (title, number) = BillInTitle(discussion.TextOf(N.Name) ?? "");
            items.TryAdd(itemId, new ParsedAgendaItem(itemId, discussion.TextOf(N.DiscussionNumber), title, stage, number, null));

            var voteId = e.RequiredId(N.ProceedingsVoteId);
            var wording = e.TextOf(N.Wording) ?? "";
            var consensus = VoteRules.IsConsensus(e.TextOf(N.VoteHeading), wording);
            var vote = new ParsedVoteReference(voteId, itemId, e.RequiredTimeOf(N.ProceedingsVoteTime), wording, consensus, stage);
            // The same vote can also appear under a group of items. Prefer its direct agenda link when one exists.
            if (!votes.TryGetValue(voteId, out var previous) || (!previous.Linked && agendaId.HasValue))
            {
                votes[voteId] = (vote, agendaId.HasValue);
            }
        }

        var references = votes.Values.Select(v => v.Vote).ToList();
        var referencedItems = references.Select(v => v.AgendaItemId).ToHashSet();
        return new ParsedProceedings(items.Values.Where(i => referencedItems.Contains(i.Id)).ToList(), references);
    }

    private static (string Title, string? Number) BillInTitle(string title)
    {
        var match = BillNumberSuffix().Match(title);
        return match.Success ? (title[..match.Index].TrimEnd(), match.Groups["number"].Value) : (title, null);
    }

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
            (totals.Enclosing(N.ResultVote) ?? throw new SeimasFormatException($"<{N.Totals}> is not inside a <{N.ResultVote}>.")).RequiredId(N.VoteId),
            new VoteTotals(totals.OptionalCount(N.For), totals.OptionalCount(N.Against), totals.OptionalCount(N.Abstain), totals.OptionalCount(N.Voted), totals.OptionalCount(N.Seats)),
            totals.Optional(N.Note),
            ballots);
    }
}
