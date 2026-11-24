namespace Lt2.Appeals.Domain;

/// <summary>What an AI answer holds (design 8.11.4, 8.13.1): the source was not enough, the answer is not in the fixed form, or a title and a description.</summary>
internal enum DraftAnswerKind
{
    NotEnough,
    Invalid,
    Valid,
}

/// <summary>The reading of an AI answer: <see cref="Title"/> and <see cref="Description"/> are set only when <see cref="Kind"/> is <see cref="DraftAnswerKind.Valid"/>.</summary>
internal sealed record DraftAnswer(DraftAnswerKind Kind, string? Title = null, string? Description = null)
{
    public static DraftAnswer NotEnough { get; } = new(DraftAnswerKind.NotEnough);

    public static DraftAnswer Invalid { get; } = new(DraftAnswerKind.Invalid);

    public static DraftAnswer Valid(string title, string description) => new(DraftAnswerKind.Valid, title, description);
}

/// <summary>
/// Reads what the prompts ask for (design 8.11.4, 8.13.1): the readable title on the first line, one empty line, then the
/// description; or, when the source is not enough, only the fixed sentence (<see cref="SummaryRules.NotEnoughAnswer"/>).
/// Anything else is invalid, never half stored.
/// </summary>
internal static class DraftAnswers
{
    public static DraftAnswer Read(string? answer)
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

        var description = AppealRules.NormaliseText(string.Join('\n', lines.Skip(empty + 1)));
        return AppealRules.CheckDescription(description) is null ? DraftAnswer.Valid(title, description) : DraftAnswer.Invalid;
    }
}
