using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Agenda;

/// <summary>
/// One public bill of the front page's agenda as the member's panel shows it (design 8.7): number, title, the planned date
/// and the last week's vote, already worded by the server. Public, the same for every member; the member's own side and
/// follow are not in it.
/// </summary>
public sealed record MyAgendaBill(string Number, string Title, string? Planned, string? LastVote);

/// <summary>
/// The member's own answer for the front page: their rows by bill number, and whether they have given the positions
/// consent (design 6.5), so a row knows whether to ask for it before a first side; and whether the caller is an MP's own
/// account (design 8.5.4, owner 2026-11-17), whose rows offer no sides.
/// </summary>
public sealed record MyAgendaAnswer(IReadOnlyDictionary<string, MyBillResponse> Bills, bool HasPositionConsent, bool MpAccount = false);

/// <summary>
/// The member's own answers for the front page (design 8.7), asked once in the browser and shared by its islands (the panel
/// "Jūsų projektai", the marks under the titles and the side columns of the timeline's rows): one <c>/me/bills</c> request
/// for all the bills shown, and the invitation answer for the share link. Never asked while the server prerenders, so
/// nothing of it is in the page's HTML. A failed answer is not kept, so the next island asks again. After a member's own
/// action in a row the answer is read again (<see cref="RefreshAsync"/>) and <see cref="Changed"/> tells every island.
/// </summary>
public sealed class MyAgendaState(IMyBillsApi bills, IMeApi me)
{
    private IReadOnlyList<string> _numbers = [];
    private string? _key;
    private Task<MyAgendaAnswer?>? _mine;
    private Task<MyInvitationResponse?>? _invitation;

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

    /// <summary>Notes the consent as given, or as missing when the server refused a side without it.</summary>
    public void SetConsent(bool given)
    {
        if (_mine is { IsCompletedSuccessfully: true, Result: { } answer } && answer.HasPositionConsent != given)
        {
            _mine = Task.FromResult<MyAgendaAnswer?>(answer with { HasPositionConsent = given });
        }
    }

    /// <summary>The caller's own invitation answer, for the share link.</summary>
    public Task<MyInvitationResponse?> InvitationAsync()
    {
        if (_invitation is null || _invitation.IsFaulted || (_invitation.IsCompletedSuccessfully && _invitation.Result is null))
        {
            _invitation = InvitationFetchAsync();
        }

        return _invitation;
    }

    private async Task<MyAgendaAnswer?> FetchAsync(IReadOnlyList<string> numbers)
    {
        if (numbers.Count == 0)
        {
            return new MyAgendaAnswer(new Dictionary<string, MyBillResponse>(StringComparer.OrdinalIgnoreCase), false);
        }

        var answer = await bills.GetAsync(new MyBillsQuery { Numbers = numbers.Take(MyBillsQuery.MaxNumbers).ToList() });
        return answer.Value is { } value
            ? new MyAgendaAnswer(value.Bills.ToDictionary(b => b.Number, StringComparer.OrdinalIgnoreCase), value.HasPositionConsent, value.MpAccount)
            : null;
    }

    private async Task<MyInvitationResponse?> InvitationFetchAsync() => (await me.GetInvitationAsync()).Value;
}
