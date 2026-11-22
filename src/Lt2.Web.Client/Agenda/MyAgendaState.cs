using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
using Lt2.Web.Client.Components;

namespace Lt2.Web.Client.Agenda;

/// <summary>
/// One row of "Jūsų projektai" (design 8.7): a bill the member follows that is on the agenda or was voted on in the last
/// week, with the member's own side (null: none), all from the member's own answers.
/// </summary>
public sealed record MyAgendaRow(FollowedBillResponse Bill, string? Side);

/// <summary>
/// The member's own answer for a page of the timeline: their rows by bill number, and whether they have given the positions
/// consent (design 6.5), so a row knows whether to ask for it before a first side; and whether the caller is an MP's own
/// account (design 8.5.4, owner 2026-11-17), whose rows offer no sides.
/// </summary>
public sealed record MyAgendaAnswer(IReadOnlyDictionary<string, MyBillResponse> Bills, bool HasPositionConsent, bool MpAccount = false);

/// <summary>
/// The member's own answers for the front page (design 8.7) and the agenda page (8.11.1, owner 2026-11-19: the same rows),
/// asked once in the browser and shared by the page's islands (the front page's panel "Jūsų projektai", the marks under the
/// titles and the side columns of the timeline's rows): <c>/me/bills</c> for all the bills shown (one request per
/// <see cref="MyBillsQuery.MaxNumbers"/>, side by side) and the member's follows for the panel (owner, 2026-11-19: the panel
/// has no share link; the bill's page keeps it). Never asked while the server prerenders, so nothing of it is in the page's
/// HTML. A failed answer is not kept, so the next island asks again. After a member's own action in a row the answers are
/// read again (<see cref="RefreshAsync"/>) and <see cref="Changed"/> tells every island.
/// </summary>
public sealed class MyAgendaState(IMyBillsApi bills, IFollowsApi follows, MyAccountState account, TimeProvider clock)
{
    /// <summary>"Voted on in the last week": the agenda's recent votes reach this far back (design 8.11.1).</summary>
    public const int RecentDays = 7;

    private IReadOnlyList<string> _numbers = [];
    private string? _key;
    private Task<MyAgendaAnswer?>? _mine;
    private Task<IReadOnlyList<MyAgendaRow>?>? _panel;

    /// <summary>The member's own answer changed (a side taken, changed or withdrawn): read it again.</summary>
    public event Action? Changed;

    /// <summary>The caller's own answer for the given bills; null when it failed.</summary>
    public Task<MyAgendaAnswer?> MineAsync(IReadOnlyList<string> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        var key = string.Join(",", numbers);
        if (_mine is null || _key != key || _mine.IsFaulted || (_mine.IsCompletedSuccessfully && _mine.Result is null))
        {
            _key = key;
            _numbers = numbers;
            _mine = FetchAsync(numbers);
        }

        return _mine;
    }

    /// <summary>
    /// Reads the caller's own answer again after their action in a row (the server may also have followed the bill, design
    /// 8.11.2) and tells every island. A failed read keeps the answer before it.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_key is null)
        {
            return;
        }

        try
        {
            // Taking a side may also have followed the bill (design 8.11.2): the panel reads the follows again.
            _panel = null;
            if (await FetchAsync(_numbers) is { } fresh)
            {
                _mine = Task.FromResult<MyAgendaAnswer?>(fresh);
            }
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// "Jūsų projektai" (design 8.7, owner 2026-11-17: followed only): the bills the member follows that are on the agenda
    /// (a planned date from today on) or were voted on in the last week, whichever days and rows the page shows; planned ones
    /// first by date, then by their last vote, newest first. Their own side comes from the page's shared answer for a bill
    /// the page shows, and from <c>/me/bills</c> for the others, a request per <see cref="MyBillsQuery.MaxNumbers"/> bills.
    /// Null when an answer failed.
    /// </summary>
    public Task<IReadOnlyList<MyAgendaRow>?> PanelAsync(IReadOnlyList<string> shown)
    {
        if (_panel is null || _panel.IsFaulted || (_panel.IsCompletedSuccessfully && _panel.Result is null))
        {
            _panel = PanelFetchAsync(shown);
        }

        return _panel;
    }

    /// <summary>The followed bills the panel lists, in its order (a pure rule of the panel's scope, design 8.7).</summary>
    public static IReadOnlyList<FollowedBillResponse> OnTheAgenda(IEnumerable<FollowedBillResponse> followed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(followed);
        var since = now.AddDays(-RecentDays);
        return Ordered(followed.Where(b => b.Planned is not null || b.LastStageResult?.HeldAt >= since));
    }

    /// <summary>
    /// "Tik mano" on the bills list (design 8.12.5): every bill the member follows, planned ones first by date, then the others
    /// by their last vote, newest first, and those with neither by number.
    /// </summary>
    public static IReadOnlyList<FollowedBillResponse> Ordered(IEnumerable<FollowedBillResponse> followed)
    {
        ArgumentNullException.ThrowIfNull(followed);
        var list = followed.ToList();
        return list.Where(b => b.Planned is not null).OrderBy(b => b.Planned!.Date).ThenBy(b => b.Number, StringComparer.Ordinal)
            .Concat(list.Where(b => b.Planned is null).OrderByDescending(b => b.LastStageResult?.HeldAt).ThenBy(b => b.Number, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>
    /// "Tik mano" (design 8.12.5): all the bills the member follows (a first side follows the bill, design 8.11.2) with their own
    /// side, from their follows and <c>/me/bills</c>, a request per <see cref="MyBillsQuery.MaxNumbers"/> bills side by side.
    /// Asked each time the box is ticked; null when an answer failed.
    /// </summary>
    public async Task<IReadOnlyList<MyAgendaRow>?> FollowedAsync()
    {
        if ((await follows.GetMineAsync()).Value is not { } mine)
        {
            return null;
        }

        var listed = Ordered(mine.Bills);
        var answers = await Task.WhenAll(Requests(listed.Select(b => b.Number).ToList()).Select(part => bills.GetAsync(new MyBillsQuery { Numbers = part })));
        var sides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in answers)
        {
            if (answer.Value is not { } value)
            {
                return null;
            }

            foreach (var own in value.Bills)
            {
                sides[own.Number] = own.Side;
            }
        }

        return listed.Select(b => new MyAgendaRow(b, sides.GetValueOrDefault(b.Number))).ToList();
    }

    /// <summary>Bill numbers in requests of at most <see cref="MyBillsQuery.MaxNumbers"/>, none empty.</summary>
    public static IEnumerable<IReadOnlyList<string>> Requests(IReadOnlyList<string> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        for (var i = 0; i < numbers.Count; i += MyBillsQuery.MaxNumbers)
        {
            yield return numbers.Skip(i).Take(MyBillsQuery.MaxNumbers).ToList();
        }
    }

    private async Task<IReadOnlyList<MyAgendaRow>?> PanelFetchAsync(IReadOnlyList<string> shownNumbers)
    {
        if ((await follows.GetMineAsync()).Value is not { } mine)
        {
            return null;
        }

        var listed = OnTheAgenda(mine.Bills, clock.GetUtcNow());
        if (listed.Count == 0)
        {
            return [];
        }

        var shown = await MineAsync(shownNumbers);
        if (shown is null)
        {
            return null;
        }

        var sides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (number, own) in shown.Bills)
        {
            sides[number] = own.Side;
        }

        var asked = new HashSet<string>(shownNumbers, StringComparer.OrdinalIgnoreCase);
        foreach (var part in Requests(listed.Select(b => b.Number).Where(n => !asked.Contains(n)).ToList()))
        {
            if ((await bills.GetAsync(new MyBillsQuery { Numbers = part })).Value is not { } answer)
            {
                return null;
            }

            foreach (var own in answer.Bills)
            {
                sides[own.Number] = own.Side;
            }
        }

        return listed.Select(b => new MyAgendaRow(b, sides.GetValueOrDefault(b.Number))).ToList();
    }

    /// <summary>
    /// Design 8.28.3: the new member's start is shown to a member who has never taken a side, which the answer says by the
    /// positions consent not given (asked before the first side, design 6.5); never to an MP's own account (8.5.4).
    /// </summary>
    public static bool ShowsStart(MyAgendaAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return !answer.HasPositionConsent && !answer.MpAccount;
    }

    /// <summary>Notes the consent as given, or as missing when the server refused a side without it.</summary>
    public void SetConsent(bool given)
    {
        if (_mine is { IsCompletedSuccessfully: true, Result: { } answer } && answer.HasPositionConsent != given)
        {
            _mine = Task.FromResult<MyAgendaAnswer?>(answer with { HasPositionConsent = given });
        }
    }

    private async Task<MyAgendaAnswer?> FetchAsync(IReadOnlyList<string> numbers)
    {
        if (numbers.Count == 0)
        {
            // Nothing on the agenda to ask about: the consent and the MP's own account come from the account's own GET /me.
            return await account.MeAsync() is { } me
                ? new MyAgendaAnswer(new Dictionary<string, MyBillResponse>(StringComparer.OrdinalIgnoreCase), me.HasPositionConsent, me.Mp is { Part: PoliticianParts.Mp })
                : null;
        }

        // The page asks about the rows it shows (design 8.7: twenty on the front page, 8.11.1: ten a day on the agenda page;
        // 50 more a day with each "Rodyti daugiau"), within the API's limit a request, the requests side by side.
        var answers = await Task.WhenAll(Requests(numbers).Select(part => bills.GetAsync(new MyBillsQuery { Numbers = part })));
        var all = new Dictionary<string, MyBillResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in answers)
        {
            if (answer.Value is not { } value)
            {
                return null;
            }

            foreach (var bill in value.Bills)
            {
                all[bill.Number] = bill;
            }
        }

        var first = answers[0].Value!;
        return new MyAgendaAnswer(all, first.HasPositionConsent, first.MpAccount);
    }
}
