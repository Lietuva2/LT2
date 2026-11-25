using System.Text.RegularExpressions;

namespace Lt2.Appeals.Domain;

/// <summary>What an AI answer holds (design 8.11.4, 8.13.1): the source was not enough, the answer is not in the fixed form, or a title and a description.</summary>
internal enum DraftAnswerKind
{
    NotEnough,
    Invalid,
    Valid,
}

/// <summary>
/// The reading of an AI answer: <see cref="Title"/>, <see cref="Description"/>, <see cref="Source"/> (the number of the
/// document it rests on, from 1) and <see cref="Statement"/> (the public note for the history, design 8.13) are set only
/// when <see cref="Kind"/> is <see cref="DraftAnswerKind.Valid"/>.
/// </summary>
internal sealed record DraftAnswer(DraftAnswerKind Kind, string? Title = null, string? Description = null, int Source = 0, string? Statement = null)
{
    public static DraftAnswer NotEnough { get; } = new(DraftAnswerKind.NotEnough);

    public static DraftAnswer Invalid { get; } = new(DraftAnswerKind.Invalid);

    public static DraftAnswer Valid(string title, string description, int source, string statement) => new(DraftAnswerKind.Valid, title, description, source, statement);
}

/// <summary>
/// Reads what the prompts ask for (design 8.11.4, 8.13, 8.13.1): the readable title on the first line, one empty line, the
/// description, one empty line, "Šaltinis: N" naming one of the documents sent, and "Paaiškinimas:" with the public
/// statement; or, when the source is not enough, only the fixed sentence (<see cref="SummaryRules.NotEnoughAnswer"/>).
/// Anything else is invalid, never half stored.
/// </summary>
internal static partial class DraftAnswers
{
    private const string StatementLabel = "Paaiškinimas:";

    public static DraftAnswer Read(string? answer, int documents)
    {
        var whole = (answer ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (SummaryRules.IsNotEnough(whole))
        {
            return DraftAnswer.NotEnough;
        }

        var lines = whole.Split('\n');
        var empty = Array.FindIndex(lines, l => l.Trim().Length == 0);
        if (empty < 0)
        {
            return DraftAnswer.Invalid;
        }

        var title = string.Join('\n', lines.Take(empty)).Trim();
        if (title.EndsWith('.'))
        {
            title = title[..^1];
        }

        if (title.Length == 0 || title.Contains('\n') || AppealRules.CheckPlainTitle(title) is not null)
        {
            return DraftAnswer.Invalid;
        }

        // The last "Šaltinis: N" line closes the description; the statement follows it.
        var sourceLine = Array.FindLastIndex(lines, l => SourceLine().IsMatch(l.Trim()));
        if (sourceLine <= empty
            || !int.TryParse(SourceLine().Match(lines[sourceLine].Trim()).Groups[1].Value, out var source)
            || source < 1 || source > documents)
        {
            return DraftAnswer.Invalid;
        }

        var rest = string.Join('\n', lines.Skip(sourceLine + 1)).Trim();
        if (!rest.StartsWith(StatementLabel, StringComparison.OrdinalIgnoreCase))
        {
            return DraftAnswer.Invalid;
        }

        var statement = AppealRules.NormaliseText(rest[StatementLabel.Length..]);
        if (statement.Length == 0 || StatementRules.Check(statement, required: true) is not null)
        {
            return DraftAnswer.Invalid;
        }

        var description = AppealRules.NormaliseText(string.Join('\n', lines.Skip(empty + 1).Take(sourceLine - empty - 1)));
        return AppealRules.CheckDescription(description) is null ? DraftAnswer.Valid(title, description, source, statement) : DraftAnswer.Invalid;
    }

    [GeneratedRegex(@"^Šaltinis:\s*(\d{1,2})\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceLine();
}
