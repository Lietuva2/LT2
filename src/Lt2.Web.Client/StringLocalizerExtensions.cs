using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Client;

/// <summary>Filling the placeholders of an interface text: the one place the pages format a localised string.</summary>
public static class StringLocalizerExtensions
{
    /// <summary>The text under <paramref name="key"/> with <c>{0}</c>, <c>{1}</c>… filled in, culture-invariant.</summary>
    public static string Format(this IStringLocalizer localizer, string key, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return string.Format(CultureInfo.InvariantCulture, localizer[key], values);
    }

    /// <summary>The text under <paramref name="key"/>, or <paramref name="fallback"/> when the resource has none.</summary>
    public static string OrElse(this IStringLocalizer localizer, string key, string fallback)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer[key] is { ResourceNotFound: false } text ? text.Value : fallback;
    }
}
