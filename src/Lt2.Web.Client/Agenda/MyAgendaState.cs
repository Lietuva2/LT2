using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Agenda;

/// <summary>
/// One public bill of the front page's agenda as the member's panel shows it (design 8.7): number, title, the planned date
/// and the last week's vote, already worded by the server. Public, the same for every member; the member's own side and
/// follow are not in it.
/// </summary>
public sealed record MyAgendaBill(string Number, string Title, string? Planned, string? LastVote);

/// <summary>
/// The member's own answers for the front page (design 8.7), asked once in the browser and shared by its islands (the panel
/// "Jūsų projektai" and the marks in the timeline's rows): one <c>/me/bills</c> request for all the bills shown, and the
/// invitation answer for the share link. Never asked while the server prerenders, so nothing of it is in the page's HTML.
/// A failed answer is not kept, so the next island asks again.
/// </summary>
public sealed class MyAgendaState(IMyBillsApi bills, IMeApi me)
{
    private string? _key;
    private Task<IReadOnlyDictionary<string, MyBillResponse>?>? _mine;
    private Task<MyInvitationResponse?>? _invitation;

    /// <summary>The caller's own rows for the given bills, by number; null when the answer failed.</summary>
    public Task<IReadOnlyDictionary<string, MyBillResponse>?> MineAsync(IReadOnlyList<string> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        var key = string.Join(",", numbers);
        if (_mine is null || _key != key || _mine.IsFaulted || (_mine.IsCompletedSuccessfully && _mine.Result is null))
        {
            _key = key;
            _mine = FetchAsync(numbers);
        }

        return _mine;
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

    private async Task<IReadOnlyDictionary<string, MyBillResponse>?> FetchAsync(IReadOnlyList<string> numbers)
    {
        if (numbers.Count == 0)
        {
            return new Dictionary<string, MyBillResponse>(StringComparer.OrdinalIgnoreCase);
        }

        var answer = await bills.GetAsync(new MyBillsQuery { Numbers = numbers.Take(MyBillsQuery.MaxNumbers).ToList() });
        return answer.Value?.Bills.ToDictionary(b => b.Number, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<MyInvitationResponse?> InvitationFetchAsync() => (await me.GetInvitationAsync()).Value;
}
