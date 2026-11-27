using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Lt2.Web.Components;
using Lt2.Web.Components.Parliament;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Previews;

/// <summary>One choice in the image's legend: its words and how many MPs made it.</summary>
internal sealed record VotePreviewChoice(string Code, string Label, int Count);

/// <summary>
/// What a vote's link preview shows (design 8.22.5, owner 2026-11-26): its title, a line with the day, the seat map coloured by
/// each MP's choice and the legend with the numbers. Only a vote with per-MP ballots has one; the rest keep the site's image.
/// The MPs are not named: the image is the seats' colours only.
/// </summary>
/// <param name="Choices">Each seat's choice, in the order <see cref="Hemicycle.Arrange"/> seats them.</param>
/// <param name="Version">A digest of everything drawn, part of the image's address, so a changed image is a new address.</param>
internal sealed record VotePreview(long VoteId, string Title, string Label, IReadOnlyList<VotePreviewChoice> Legend, IReadOnlyList<string> Choices, string Version)
{
    /// <summary>Raised when the drawing itself changes, so every image gets a new address.</summary>
    private const int Drawing = 2;

    /// <summary>The image's address on this site: under <c>/img</c>, which other sites may fetch (design 8.22.5).</summary>
    public string Path => $"/img/votes/{VoteId.ToString(CultureInfo.InvariantCulture)}/{Version}.png";

    /// <summary>
    /// The title the preview names a vote by, as the vote's page has it (design 8.13.1): the bill's readable title, whoever wrote
    /// it, unless a report about the bill's description is open; otherwise the page's heading.
    /// </summary>
    public static string TitleOf(VoteResponse vote, bool reported)
    {
        ArgumentNullException.ThrowIfNull(vote);
        var link = vote.Summary.Vote;
        return link.Bill?.PlainTitle is { } plain && !reported ? plain : VotePage.Heading(link.Bill?.Title ?? vote.AgendaItemTitle, link.Wording).Heading;
    }

    /// <summary>The vote's preview, or null when it has no per-MP ballots (a consensus, or a vote without results yet).</summary>
    public static VotePreview? Of(VoteResponse vote, bool reported, Formats formats, IStringLocalizer<PageText> text)
    {
        ArgumentNullException.ThrowIfNull(vote);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(text);
        if (vote.Ballots.Count == 0)
        {
            return null;
        }

        var link = vote.Summary.Vote;
        var title = TitleOf(vote, reported).Trim();
        var label = string.Join(" · ", new[] { text["Preview.Vote.Label"].Value, link.Stage is null ? null : formats.Stage(link.Stage), Display.Day(link.HeldAt) }
            .Where(part => !string.IsNullOrEmpty(part)));
        var legend = Hemicycle.ChoiceOrder
            .Select(code => new VotePreviewChoice(code, formats.Choice(code), vote.Ballots.Count(b => b.Choice == code)))
            .Where(choice => choice.Count > 0)
            .ToList();
        var choices = Hemicycle.Arrange(vote.Ballots).Seats.Select(s => s.Ballot.Choice).ToList();
        return new VotePreview(link.Id, title, label, legend, choices, VersionOf(title, label, legend, choices));
    }

    /// <summary>
    /// The preview's text: the day and the Seimas's numbers (the vote's own totals, as its page shows them), or that it was
    /// approved by consensus, then the bill's description when it has one (<paramref name="billDescription"/>, the same as the
    /// bill's preview: none while a report about it is open). Null when there is neither, which keeps the site's lead
    /// sentence. Never a majority's word: the page explains what it means one click away, a preview cannot (8.18).
    /// </summary>
    public static string? Description(VoteResponse vote, string? billDescription, IStringLocalizer<PageText> text)
    {
        ArgumentNullException.ThrowIfNull(vote);
        ArgumentNullException.ThrowIfNull(text);
        var link = vote.Summary.Vote;
        var day = Display.Day(link.HeldAt);
        var totals = vote.Summary.Totals;
        var numbers = link.IsConsensus ? text["Preview.Vote.Consensus", day].Value
            : link.HasResult && totals is { For: { } @for, Against: { } against, Abstain: { } abstain }
                ? text["Preview.Vote", day, Display.Number(@for), Display.Number(against), Display.Number(abstain)].Value
                : null;
        var about = string.IsNullOrWhiteSpace(billDescription) ? null : billDescription.Trim();
        return numbers is null ? about : about is null ? numbers : numbers + " " + about;
    }

    /// <summary>
    /// What the bill's record lends a vote's preview (design 8.13.1, "Off the site"): whether a report about the description is
    /// open, and the description while the record is open or closed and nothing is reported.
    /// </summary>
    public static (bool Reported, string? Description) FromRecord(Lt2.Api.Contracts.Appeals.BillRecordResponse? record) =>
        record is null ? (false, null)
            : (record.DescriptionReported,
                record is { Status: BillRecordStatuses.Open or BillRecordStatuses.Closed, DescriptionReported: false, Description.Length: > 0 } ? record.Description : null);

    /// <summary>The image's alternative text: what it shows and the legend's numbers.</summary>
    public string Alt(IStringLocalizer<PageText> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text["Preview.Vote.ImageAlt", string.Join(", ", Legend.Select(c => $"{c.Label.ToLower(CultureInfo.GetCultureInfo("lt-LT"))} {Display.Number(c.Count)}"))];
    }

    private static string VersionOf(string title, string label, IReadOnlyList<VotePreviewChoice> legend, IReadOnlyList<string> choices)
    {
        var all = new StringBuilder()
            .Append(Drawing).Append('\n').Append(title).Append('\n').Append(label).Append('\n')
            .AppendJoin(';', legend.Select(c => $"{c.Code}={c.Label}={c.Count}")).Append('\n')
            .AppendJoin(',', choices);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())))[..16];
    }
}
