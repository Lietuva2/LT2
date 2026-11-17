using System.Text.Json;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// One kind's part of the gathered email (design 8.24): the things of that kind, each the payload of the email it would be
/// alone, written as a heading and a line per thing. Registered with <c>AddNoticePart</c> beside the kind's own email.
/// </summary>
internal interface INoticePart
{
    /// <summary>The outbox kind of the email the thing is alone.</summary>
    string Kind { get; }

    /// <summary>The part's place in the email: the switches' order.</summary>
    int Order { get; }

    string Render(IReadOnlyList<JsonElement> payloads);
}

/// <summary>The one <see cref="INoticePart"/>: reads each payload as <typeparamref name="TModel"/> and hands them to the template.</summary>
internal sealed class NoticePart<TModel>(string kind, int order, Func<IReadOnlyList<TModel>, string> part) : INoticePart
    where TModel : class
{
    public string Kind => kind;

    public int Order => order;

    public string Render(IReadOnlyList<JsonElement> payloads) =>
        part(payloads.Select(p => p.Deserialize<TModel>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException($"Empty payload of kind '{kind}'.")).ToList());
}

/// <summary>
/// The gathered email (design 8.24): what one run, one morning or one week holds for a member, a part per kind in the switches'
/// order, each line with its day and its link, ending with the link to the switches and the choice. It holds only what the
/// single emails would: nothing about the person's side, a bill's counts or other people. No tracking; full addresses from
/// <c>Site:Address</c>.
/// </summary>
internal sealed class GatheredTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site, IEnumerable<INoticePart> parts)
{
    private readonly Dictionary<string, INoticePart> _parts = parts.ToDictionary(p => p.Kind, StringComparer.Ordinal);

    public EmailMessage Render(GatheredNoticesEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var rhythm = model.Rhythm is "daily" or "weekly" ? model.Rhythm : "immediate";
        var paragraphs = new List<string> { l[EmailText.Greeting], l["Gathered.Intro." + rhythm] };
        foreach (var group in model.Notices.GroupBy(n => n.Kind, StringComparer.Ordinal)
                     .Select(g => (Part: _parts.GetValueOrDefault(g.Key) ?? throw new InvalidOperationException($"No part for '{g.Key}' in a gathered email."), Items: g.Select(n => n.Payload).ToList()))
                     .OrderBy(g => g.Part.Order))
        {
            paragraphs.Add(group.Part.Render(group.Items));
        }

        paragraphs.Add(EmailLayout.WithStopLine(l, site.Value.Address, l.Format("Gathered.Footer", SiteLinks.Absolute(site.Value.Address, model.AccountPath)), model.StopToken));
        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(
            model.To, l["Gathered.Subject." + rhythm], paragraphs, EmailLayout.Footer(l, site.Value.Address), headers: EmailLayout.StopHeaders(site.Value.Address, model.StopToken));
    }
}
