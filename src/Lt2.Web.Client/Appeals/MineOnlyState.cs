namespace Lt2.Web.Client.Appeals;

/// <summary>
/// Whether "Tik mano" is ticked on the bills list (design 8.12.5): shared by the box in the search form and the section "Mano
/// projektai" under it, two islands of one page. Kept only in the page being read: nothing is stored, and it is off again
/// whenever the box is taken away.
/// </summary>
public sealed class MineOnlyState
{
    public bool On { get; private set; }

    /// <summary>The box was ticked or cleared.</summary>
    public event Action? Changed;

    public void Set(bool on)
    {
        if (On == on)
        {
            return;
        }

        On = on;
        Changed?.Invoke();
    }
}
