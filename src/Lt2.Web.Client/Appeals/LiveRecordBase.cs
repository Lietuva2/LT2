using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client.Components;
using Microsoft.AspNetCore.Components;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// A part of a bill's page that shows the bill's public numbers (the counts, the threshold, what is still missing to it): the
/// server's copy given as parameters at first; the bill's public answer once another island announces a change
/// (<see cref="MyBillState.Announce"/>, a side taken, changed or withdrawn), so the page need not be reloaded; the server's
/// copy again when the page is read again in place.
/// </summary>
public abstract class LiveRecordBase : InteractiveBase<AppealUi>, IDisposable
{
    [Inject] private MyBillState BillState { get; set; } = default!;

    [Parameter, EditorRequired] public string Number { get; set; } = "";

    /// <summary>The bill's public answer read after a change; null while the server's copy (the parameters) stands.</summary>
    protected BillRecordResponse? Live { get; private set; }

    protected override void OnInitialized()
    {
        if (RendererInfo.IsInteractive)
        {
            BillState.Changed += OnChanged;
        }
    }

    /// <summary>What a part reads besides the bill's live answer once it has come, before it is shown again (none by default).</summary>
    protected virtual Task OnLiveAsync(BillRecordResponse fresh) => Task.CompletedTask;

    /// <summary>New parameters are the server's copy, read with the page: newer than anything read before.</summary>
    protected override void OnParametersSet() => Live = null;

    public void Dispose()
    {
        BillState.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }

    private void OnChanged(BillChange change)
    {
        if (!change.Concerns(Number, this))
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            try
            {
                if (await BillState.RecordAsync(Number) is { } fresh)
                {
                    Live = fresh;
                    await OnLiveAsync(fresh);
                    StateHasChanged();
                }
            }
            catch (HttpRequestException)
            {
                // The numbers shown stay; the next change reads again.
            }
        });
    }
}
