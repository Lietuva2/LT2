namespace Lt2.Parliament.Import.Parsing;

/// <summary>How an MP voted, as the per-MP row says.</summary>
internal enum BallotChoice
{
    DidNotVote = 0,
    For = 1,
    Against = 2,
    Abstain = 3,
}

/// <summary>Design 5.1: what a vote decided.</summary>
internal enum VoteKind
{
    Other = 0,
    StageResult = 1,
    Amendment = 2,
    Procedural = 3,
}

/// <summary>The stages of a bill in the Seimas.</summary>
internal enum BillStage
{
    Presentation = 1,
    Consideration = 2,
    Adoption = 3,
}

/// <summary>Design 5.1: the kinds of Seimas bodies.</summary>
internal enum BodyKind
{
    Other = 0,
    Faction = 1,
    Committee = 2,
    Commission = 3,
    Group = 4,
}

/// <summary>
/// Reading rules over Seimas text: ballot values, consensus, vote classification, body kinds. All are assumptions about
/// wording (register) and must be re-tested against recorded items (design 7.2); a moderator's correction of a
/// classification wins over <see cref="Classify"/> (<see cref="Domain.VoteClassification"/>).
/// </summary>
internal static class VoteRules
{
    /// <summary><c>kaip_balsavo</c>: "Už", "Prieš", "Susilaikė"; empty means the MP did not vote. Anything else is kept as
    /// "did not vote" rather than guessed.</summary>
    public static BallotChoice Choice(string? value) => Normalise(value) switch
    {
        "už" => BallotChoice.For,
        "prieš" => BallotChoice.Against,
        "susilaikė" => BallotChoice.Abstain,
        _ => BallotChoice.DidNotVote,
    };

    /// <summary>
    /// The standing disclaimer lrs.lt puts in <c>komentaras</c> on every vote result, whether or not the per-MP rows add
    /// up to the totals (design 7.2). It says nothing about the vote it is on.
    /// </summary>
    public const string StandingDisclaimer =
        "Elektroninėmis priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų";

    /// <summary>Design 7.2: the Seimas's note on a result as shown beside it; null when empty or when it is only the
    /// <see cref="StandingDisclaimer"/> (case, surrounding spaces and a closing full stop ignored).</summary>
    public static string? OfficialNote(string? komentaras)
    {
        var note = komentaras?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            return null;
        }

        return Normalise(note.TrimEnd('.')) == Normalise(StandingDisclaimer) ? null : note;
    }

    /// <summary>Approved by consensus ("Pritarta bendru sutarimu"): no per-MP votes exist.</summary>
    public static bool IsConsensus(string? type, string? wording) =>
        Normalise(type).Contains("bendru sutarimu", StringComparison.Ordinal)
        || Normalise(wording).Contains("bendru sutarimu", StringComparison.Ordinal);

    /// <summary>The agenda's stage text ("Pateikimas", "Svarstymas", "Priėmimas"), or null.</summary>
    public static BillStage? Stage(string? text)
    {
        var s = Normalise(text);
        return s.StartsWith("pateikim", StringComparison.Ordinal) ? BillStage.Presentation
            : s.StartsWith("svarstym", StringComparison.Ordinal) ? BillStage.Consideration
            : s.StartsWith("priėmim", StringComparison.Ordinal) ? BillStage.Adoption
            : null;
    }

    /// <summary>
    /// Design 7.2: a rule set over the wording marks the stage results; what the rules cannot classify is "other" and has
    /// no stage. A plain approval ("Pritarti", "Pritarta bendru sutarimu") of an item at a known stage is that stage's
    /// result.
    /// </summary>
    public static (VoteKind Kind, BillStage? Stage) Classify(string? wording, BillStage? itemStage)
    {
        var w = Normalise(wording);
        var separator = w.IndexOf(" – ", StringComparison.Ordinal);
        var outcome = separator >= 0 ? w[..separator] : w;
        if (outcome == "sprendimas nepriimtas")
        {
            return (VoteKind.Other, null);
        }

        if (separator >= 0 && outcome is "pritarta" or "nepritarta" or "pritarta bendru sutarimu")
        {
            w = w[(separator + 3)..];
        }

        if (w.Contains("darbotvarkė", StringComparison.Ordinal))
        {
            return (VoteKind.Procedural, null);
        }

        if (w.Contains("pasiūlym", StringComparison.Ordinal) || w.Contains("pataisos", StringComparison.Ordinal))
        {
            return (VoteKind.Amendment, itemStage);
        }

        if (w.Contains("po pateikimo", StringComparison.Ordinal))
        {
            return (VoteKind.StageResult, BillStage.Presentation);
        }

        if (w.Contains("po svarstymo", StringComparison.Ordinal))
        {
            return (VoteKind.StageResult, BillStage.Consideration);
        }

        if (w.Contains("priimti", StringComparison.Ordinal) || w.Contains("priimtas", StringComparison.Ordinal) || w.Contains("priėmim", StringComparison.Ordinal))
        {
            return (VoteKind.StageResult, BillStage.Adoption);
        }

        if (itemStage is not null && w.StartsWith("pritar", StringComparison.Ordinal))
        {
            return (VoteKind.StageResult, itemStage);
        }

        return (VoteKind.Other, null);
    }

    /// <summary>The kind of a body from its name: "…frakcija", "…komitetas", "…komisija", "…grupė" (assumed: no kind
    /// field is known). The mixed group of MPs is a group, not a faction.</summary>
    public static BodyKind Body(string name)
    {
        var n = Normalise(name);
        return n.Contains("frakcij", StringComparison.Ordinal) ? BodyKind.Faction
            : n.Contains("komitet", StringComparison.Ordinal) ? BodyKind.Committee
            : n.Contains("komisij", StringComparison.Ordinal) ? BodyKind.Commission
            : n.Contains("grupė", StringComparison.Ordinal) ? BodyKind.Group
            : BodyKind.Other;
    }

    private static string Normalise(string? text) =>
        (text ?? "").Trim().ToLower(System.Globalization.CultureInfo.GetCultureInfo("lt-LT"));
}
