using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Lt2.SharedKernel;

namespace Lt2.Parliament.Import.Parsing;

/// <summary>A response did not have the shape the parsers rely on (see the register, Seimas/ASSUMPTIONS.md).</summary>
internal sealed class SeimasFormatException(string message) : Exception(message);

/// <summary>One element or attribute name a parser read: what the register of assumptions must list.</summary>
/// <param name="Kind"><c>element</c> or <c>attribute</c>.</param>
/// <param name="Name">The element or attribute name.</param>
/// <param name="Within">For an element, the element it was looked for under; for an attribute, its element.</param>
internal sealed record XmlUse(string Kind, string Name, string Within);

/// <summary>
/// The only way the parsers read XML. Every element and attribute name passes through here, so a test can record what
/// the parsers rely on and compare it with the register of assumptions. Values are read in the Seimas formats, which
/// are themselves assumptions (register, kind <c>format</c>).
/// </summary>
internal static class XmlRead
{
    /// <summary>The "within" of an element looked for anywhere: neither the root's name nor the depth is relied on.</summary>
    public const string AnywhereInDocument = "(document)";

    private static readonly AsyncLocal<HashSet<XmlUse>?> Recorder = new();

    /// <summary>Tests: records every name read on this async flow until disposed.</summary>
    public static IDisposable Record(HashSet<XmlUse> uses)
    {
        var previous = Recorder.Value;
        Recorder.Value = uses;
        return new Restore(() => Recorder.Value = previous);
    }

    public static XDocument Load(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var stream = new MemoryStream(body, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    /// <summary>Every element of this name anywhere below <paramref name="container"/> (nesting depth is not relied on).</summary>
    public static IEnumerable<XElement> All(this XContainer container, string name)
    {
        Use("element", name, container is XElement e ? e.Name.LocalName : AnywhereInDocument);
        return container.Descendants(name);
    }

    /// <summary>The direct children of this name: nesting relied on (for example an MP's memberships).</summary>
    public static IEnumerable<XElement> Children(this XElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        Use("element", name, element.Name.LocalName);
        return element.Elements(name);
    }

    /// <summary>The nearest enclosing element of this name: nesting relied on (a vote inside its agenda item).</summary>
    public static XElement? Enclosing(this XElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        Use("element", element.Name.LocalName, name);
        return element.Ancestors(name).FirstOrDefault();
    }

    public static string? TextOf(this XElement element, string name)
    {
        var value = element.Children(name).FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>An attribute that must be present and not empty.</summary>
    public static string Required(this XElement element, string attribute) =>
        element.Optional(attribute) ?? throw new SeimasFormatException($"<{element.Name.LocalName}> has no {attribute}.");

    /// <summary>An attribute that may be missing; empty counts as missing.</summary>
    public static string? Optional(this XElement element, string attribute)
    {
        ArgumentNullException.ThrowIfNull(element);
        Use("attribute", attribute, element.Name.LocalName);
        var value = element.Attribute(attribute)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>An attribute whose presence matters even when empty (an empty <c>kaip_balsavo</c> means "did not vote").</summary>
    public static string? Raw(this XElement element, string attribute)
    {
        ArgumentNullException.ThrowIfNull(element);
        Use("attribute", attribute, element.Name.LocalName);
        return element.Attribute(attribute)?.Value.Trim();
    }

    public static long RequiredId(this XElement element, string attribute) => Integer(element, attribute, element.Required(attribute));

    public static long? OptionalId(this XElement element, string attribute) =>
        element.Optional(attribute) is { } text ? Integer(element, attribute, text) : null;

    public static int? OptionalCount(this XElement element, string attribute) =>
        element.Optional(attribute) is { } text ? checked((int)Integer(element, attribute, text)) : null;

    /// <summary>Dates are <c>yyyy-MM-dd</c> (assumed).</summary>
    public static DateOnly RequiredDate(this XElement element, string attribute) => Date(element, attribute, element.Required(attribute));

    public static DateOnly? OptionalDate(this XElement element, string attribute) =>
        element.Optional(attribute) is { } text ? Date(element, attribute, text) : null;

    /// <summary>Times are Vilnius local time, <c>yyyy-MM-dd HH:mm</c> or with seconds (assumed); returned in UTC.</summary>
    public static DateTimeOffset? OptionalTime(this XElement element, string attribute) =>
        element.Optional(attribute) is { } text ? Time(element, attribute, text) : null;

    public static DateTimeOffset RequiredTimeOf(this XElement element, string name) =>
        Time(element, name, element.TextOf(name) ?? throw new SeimasFormatException($"<{element.Name.LocalName}> has no <{name}>."));

    private static DateTimeOffset VilniusToUtc(DateTime local)
    {
        var offset = VilniusTime.Zone.GetUtcOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }

    private static readonly string[] TimeFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"];

    private static DateTimeOffset Time(XElement element, string attribute, string text) =>
        DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? VilniusToUtc(local)
            : throw new SeimasFormatException($"<{element.Name.LocalName} {attribute}=\"{text}\"> is not a time.");

    private static DateOnly Date(XElement element, string attribute, string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new SeimasFormatException($"<{element.Name.LocalName} {attribute}=\"{text}\"> is not a date.");

    private static long Integer(XElement element, string attribute, string text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new SeimasFormatException($"<{element.Name.LocalName} {attribute}=\"{text}\"> is not a number.");

    private static void Use(string kind, string name, string within) => Recorder.Value?.Add(new XmlUse(kind, name, within));

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
