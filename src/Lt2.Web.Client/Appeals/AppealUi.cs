using System.Globalization;
using Lt2.Api.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Client.Appeals;

/// <summary>Marker type for the Lithuanian texts of the appeal components in AppealUi.resx.</summary>
public sealed class AppealUi;

/// <summary>Numbers as the pages write them: tabular figures, a no-break space for thousands (design/ui/README.md).</summary>
public static class AppealNumbers
{
    private static readonly NumberFormatInfo Format = new() { NumberGroupSeparator = " ", NumberGroupSizes = [3] };

    public static string Of(int value) => value.ToString("#,0", Format);
}

/// <summary>
/// Dates as the pages write them in numbers (<c>AGENTS.md</c> "Wording"): <c>yyyy-MM-dd</c>, with <c>HH:mm</c> in Vilnius
/// time; and the one typed form, a text field in <c>yyyy-MM-dd</c>, never the browser's date control.
/// </summary>
public static class Dates
{
    /// <summary>The placeholder of a typed date field.</summary>
    public const string Example = "2026-11-19";

    /// <summary>The pattern a typed date field checks before it is sent (the server checks again).</summary>
    public const string Pattern = @"\d{4}-\d{2}-\d{2}";

    private static readonly TimeZoneInfo Vilnius = FindVilnius();

    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The Vilnius day of a moment.</summary>
    public static string Day(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Vilnius).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The Vilnius day and time of a moment.</summary>
    public static string DayTime(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Vilnius).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Reads a typed <c>yyyy-MM-dd</c> date; spaces around it are ignored, nothing else is accepted.</summary>
    public static bool TryRead(string? text, out DateOnly day) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static TimeZoneInfo FindVilnius()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>
/// What every interactive appeal component shares: one action at a time, controls disabled until WebAssembly has taken
/// over the prerendered markup (anything typed before would be lost), and a message for every failure.
/// </summary>
public abstract class InteractiveBase : ComponentBase
{
    [Inject]
    protected IStringLocalizer<AppealUi> L { get; set; } = default!;

    protected bool Busy { get; private set; }

    protected string? Message { get; set; }

    protected string? Info { get; set; }

    protected IReadOnlyDictionary<string, string[]> Errors { get; set; } = new Dictionary<string, string[]>();

    protected bool Locked => Busy || !RendererInfo.IsInteractive;

    protected async Task RunAsync(Func<Task> action)
    {
        Message = null;
        Info = null;
        Errors = new Dictionary<string, string[]>();
        Busy = true;
        try
        {
            await action();
        }
        catch (HttpRequestException)
        {
            Message = L["Error.Network"];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            Message = L["Error.Generic"];
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Shows a failed answer: field errors under their fields, anything else as the note.</summary>
    protected void Show(ApiProblem? problem, params string[] fields)
    {
        if (problem?.Errors is { Count: > 0 } errors && errors.Keys.All(k => fields.Contains(k, StringComparer.OrdinalIgnoreCase)))
        {
            Errors = errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
            return;
        }

        Message = problem?.Errors?.Values.FirstOrDefault()?.FirstOrDefault() ?? problem?.Detail ?? L["Error.Generic"];
    }

    protected string Text(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, L[key], values);
}
