using System.Security.Cryptography;
using System.Text;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client;
using Lt2.Web.Components;
using Lt2.Web.Components.Parliament;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Previews;

/// <summary>
/// What a bill's link-preview image shows (design 8.22.5, owner 2026-11-27): a title card. The label "Seimo projektas ·
/// XVP-0412", the title the bill's preview names (at most four lines), the stage line as the page's header has it and, while
/// the record is open, one line inviting people to take part. Never the counts: numbers need "not a representative poll"
/// beside them, which an image shared on its own cannot keep.
/// </summary>
/// <param name="Version">A digest of everything drawn, part of the image's address, so a changed image is a new address.</param>
internal sealed record BillPreview(string Number, string Title, string Label, string? Stage, string? Call, string Version)
{
    /// <summary>Raised when the drawing itself changes, so every image gets a new address.</summary>
    private const int Drawing = 2;

    /// <summary>The image's address on this site: under <c>/img</c>, which other sites may fetch (design 8.22.5).</summary>
    public string Path => $"/img/bills/{Uri.EscapeDataString(Number)}/{Version}.png";

    /// <summary>
    /// The title the preview names a bill by (design 8.13.1): its readable title, whoever wrote it, while the record is open or
    /// closed and no report about the description is open; otherwise the official title.
    /// </summary>
    public static string TitleOf(BillResponse bill, BillRecordResponse? record)
    {
        ArgumentNullException.ThrowIfNull(bill);
        return record is { Status: BillRecordStatuses.Open or BillRecordStatuses.Closed, DescriptionReported: false, PlainTitle: { Length: > 0 } plain }
            ? plain
            : bill.Title;
    }

    public static BillPreview Of(BillResponse bill, BillRecordResponse? record, Formats formats, IStringLocalizer<PageText> text)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(text);
        var title = TitleOf(bill, record).Trim();
        var label = $"{text["Preview.Bill.Label"].Value} · {bill.Number}";
        var stage = StageLine(bill, record, formats, text);
        var call = record?.Status == BillRecordStatuses.Open ? text["Preview.Bill.Call"].Value : null;
        return new BillPreview(bill.Number, title, label, stage, call, VersionOf(title, label, stage, call));
    }

    /// <summary>The image's alternative text: the bill's number and title.</summary>
    public string Alt(IStringLocalizer<PageText> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text["Preview.Bill.ImageAlt", Number, Title];
    }

    /// <summary>
    /// The header's stage tags in one line (design 8.12.1): the planned stage and "Planuojama yyyy-MM-dd", else the latest
    /// stage the Seimas voted on; "Uždarytas" for a closed record. Null when there is none of them.
    /// </summary>
    private static string? StageLine(BillResponse bill, BillRecordResponse? record, Formats formats, IStringLocalizer<PageText> text)
    {
        var parts = new List<string>();
        if (bill.Planned is { } plan)
        {
            if (plan.Stage is { } planned)
            {
                parts.Add(formats.Stage(planned));
            }

            parts.Add(text["Preview.Bill.Planned", Display.Day(plan.Date)]);
        }
        else if (BillPage.LastStage(bill) is { } done)
        {
            parts.Add(formats.Stage(done));
        }

        if (record?.Status == BillRecordStatuses.Closed)
        {
            parts.Add(text["Preview.Bill.ClosedTag"]);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string VersionOf(string title, string label, string? stage, string? call)
    {
        var all = new StringBuilder()
            .Append(Drawing).Append('\n').Append(title).Append('\n').Append(label).Append('\n').Append(stage).Append('\n').Append(call);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())))[..16];
    }
}
