using System.Globalization;

namespace Lt2.SharedKernel;

/// <summary>The Seimas's numbers at a record's closing vote, as the comparison reads them (design 8.26.2).</summary>
/// <param name="IsConsensus">Adopted by consensus: no numbers, a majority "for".</param>
public sealed record SeimasVoteNumbers(bool IsConsensus, int? For, int? Against, int? Abstain);

/// <summary>
/// One closed bill compared (design 8.26.2): the Seimas's share and majority, the members' share and majority, and whether
/// the two are compared and agree. <paramref name="Sample"/> is the members' positions counted (by the filter).
/// </summary>
/// <param name="SeimasShareFor">For ÷ (for + against + abstain); null for a consensus or a vote without numbers.</param>
/// <param name="SeimasMajorityFor">The majority of those who voted chose "for"; a consensus counts as "for"; null without numbers.</param>
/// <param name="MembersShareFor">For ÷ (for + against), only from <see cref="AnalyticsRules.SmallSample"/> positions; null below.</param>
/// <param name="MembersMajorityFor">The larger side is "for"; null on a tie or with no positions.</param>
/// <param name="Compared">The bill enters "Majorities agreed": a sample of at least 20 and both majorities known.</param>
/// <param name="Agreed">Both majorities chose the same; null when not compared.</param>
/// <param name="Dot">The bill has a dot in the plot: compared and the Seimas's share known (not a consensus).</param>
public sealed record BillComparison(
    decimal? SeimasShareFor, bool? SeimasMajorityFor, int Sample, decimal? MembersShareFor, bool? MembersMajorityFor, bool Compared, bool? Agreed, bool Dot);

/// <summary>How often a group of choices matched the caller's sides (design 8.26.4): matched of common, and free votes.</summary>
/// <param name="Share">Matched ÷ common, only from <see cref="AnalyticsRules.SmallSample"/> common votes; null below.</param>
public sealed record Agreement(int Matched, int Common, int Free, decimal? Share);

/// <summary>
/// The counted rules of the analytics page (design 8.26), as pure functions. One sample size for every number on it, 20,
/// fixed by the owner (2026-11-16), not a setting.
/// </summary>
public static class AnalyticsRules
{
    /// <summary>Under this many a sample is "maža imtis": no share is shown, only the counts.</summary>
    public const int SmallSample = 20;

    private static readonly CompareInfo Lithuanian = CultureInfo.GetCultureInfo("lt-LT").CompareInfo;

    /// <summary>A sample of <paramref name="size"/> is large enough for a share.</summary>
    public static bool IsEnough(int size) => size >= SmallSample;

    /// <summary>The share <paramref name="part"/> of <paramref name="whole"/>, shown only from a sample of 20; null below or with nothing.</summary>
    public static decimal? ShareFromSample(int part, int whole) => whole > 0 && IsEnough(whole) ? (decimal)part / whole : null;

    /// <summary>The Seimas's share "for": for ÷ (for + against + abstain); none for a consensus or without numbers.</summary>
    public static decimal? SeimasShareFor(SeimasVoteNumbers vote)
    {
        ArgumentNullException.ThrowIfNull(vote);
        if (vote.IsConsensus || vote.For is not { } @for || vote.Against is not { } against || vote.Abstain is not { } abstain)
        {
            return null;
        }

        var voted = @for + against + abstain;
        return voted == 0 ? null : (decimal)@for / voted;
    }

    /// <summary>
    /// The majority of those who voted chose "for": the votes for are more than half of for, against and abstain. A
    /// consensus is a majority "for"; a vote without numbers has none (null). Not the legal rule, which counts the MPs
    /// registered at the sitting (design 8.26.2).
    /// </summary>
    public static bool? SeimasMajorityFor(SeimasVoteNumbers vote)
    {
        ArgumentNullException.ThrowIfNull(vote);
        if (vote.IsConsensus)
        {
            return true;
        }

        if (vote.For is not { } @for || vote.Against is not { } against || vote.Abstain is not { } abstain || @for + against + abstain == 0)
        {
            return null;
        }

        return 2 * @for > @for + against + abstain;
    }

    /// <summary>The members' majority: "for" when more chose for, not "for" when more chose against, none on a tie.</summary>
    public static bool? MembersMajorityFor(int @for, int against) => @for > against ? true : against > @for ? false : null;

    /// <summary>Compares one closed bill (design 8.26.2) from the Seimas's numbers and the members' counts chosen by the filter.</summary>
    public static BillComparison Compare(SeimasVoteNumbers vote, int membersFor, int membersAgainst)
    {
        var seimasShare = SeimasShareFor(vote);
        var seimasMajority = SeimasMajorityFor(vote);
        var sample = membersFor + membersAgainst;
        var membersMajority = MembersMajorityFor(membersFor, membersAgainst);
        var compared = IsEnough(sample) && seimasMajority is not null && membersMajority is not null;
        return new BillComparison(
            seimasShare,
            seimasMajority,
            sample,
            ShareFromSample(membersFor, sample),
            membersMajority,
            compared,
            compared ? seimasMajority == membersMajority : null,
            compared && seimasShare is not null);
    }

    /// <summary>
    /// How often choices matched the caller's sides (design 8.26.4): each pair is the caller's side (<c>for</c> or
    /// <c>against</c>) and a choice or position at that bill's closing vote. A choice of for, against or abstain is in the
    /// sample and matches only when it is the side (an abstention never does); <c>free</c> is a free vote, counted apart;
    /// anything else (did not vote, nobody voted, a consensus without ballots) is not in the sample.
    /// </summary>
    public static Agreement AgreementOf(IEnumerable<(string Side, string? Choice)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        int matched = 0, common = 0, free = 0;
        foreach (var (side, choice) in pairs)
        {
            switch (choice)
            {
                case "for" or "against" or "abstain":
                    common++;
                    if (string.Equals(choice, side, StringComparison.Ordinal))
                    {
                        matched++;
                    }

                    break;
                case "free":
                    free++;
                    break;
            }
        }

        return new Agreement(matched, common, free, ShareFromSample(matched, common));
    }

    /// <summary>
    /// The caller's order (design 8.26.4, owner 2026-11-16): those with a share (20 common votes or more) by the share,
    /// highest first, then by name; then the small samples by name. Nobody without a common vote is listed.
    /// </summary>
    public static IReadOnlyList<T> OrderForCaller<T>(IEnumerable<T> rows, Func<T, Agreement> agreement, Func<T, string> name)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(agreement);
        ArgumentNullException.ThrowIfNull(name);
        var byName = Comparer<string>.Create((a, b) => Lithuanian.Compare(a, b, CompareOptions.IgnoreCase));
        var listed = rows.Where(r => agreement(r).Common > 0).ToList();
        return listed.Where(r => agreement(r).Share is not null)
            .OrderByDescending(r => agreement(r).Share).ThenBy(name, byName)
            .Concat(listed.Where(r => agreement(r).Share is null).OrderBy(name, byName))
            .ToList();
    }

    /// <summary>The Vilnius days a period covers: a calendar year of the term cut to the term, or the whole term up to today.</summary>
    public static (DateOnly From, DateOnly To) Period(DateOnly termFrom, DateOnly? termTo, DateOnly today, int? year)
    {
        var to = termTo is { } end && end < today ? end : today;
        if (year is { } y && Years(termFrom, termTo, today).Contains(y))
        {
            var from = new DateOnly(y, 1, 1);
            var last = new DateOnly(y, 12, 31);
            return (from < termFrom ? termFrom : from, last > to ? to : last);
        }

        return (termFrom, to);
    }

    /// <summary>The calendar years of the term up to today, newest first: the periods the page offers beside the whole term.</summary>
    public static IReadOnlyList<int> Years(DateOnly termFrom, DateOnly? termTo, DateOnly today)
    {
        var last = (termTo is { } end && end < today ? end : today).Year;
        return last < termFrom.Year ? [] : Enumerable.Range(termFrom.Year, last - termFrom.Year + 1).Reverse().ToList();
    }
}
