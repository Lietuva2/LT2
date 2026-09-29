using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;

namespace Framework.Mvc.Strings
{
    /// <summary>
    /// Filters HTML to the valid html tags set (with only the attributes specified).
    ///
    /// The HTML is parsed and written out again from the parsed tree: only whitelisted tags and attributes are kept,
    /// all text and attribute values are re-encoded, links and images may only use http(s) (and mailto for links),
    /// iframes only embed known video players, and inline styles are limited to simple values.
    /// </summary>
    public static class HtmlSanitizeExtension
    {
        /// <summary>
        /// A dictionary of allowed tags and their allowed attributes and style properties. If nothing is listed,
        /// all attributes are stripped from the allowed tag.
        /// </summary>
        public static Dictionary<string, List<string>> ValidHtmlTags = new Dictionary<string, List<string>> {
            { "p", new List<string>(){"text-align", "margin-left"} },
            { "br", new List<string>() },
            { "strong", new List<string>() },
            { "em", new List<string>() },
            { "u", new List<string>() },
            { "ol", new List<string>() {"margin-left"} },
            { "ul", new List<string>() {"margin-left"}},
            { "li", new List<string>() },
            { "table", new List<string>() },
            { "tbody", new List<string>() },
            { "tr", new List<string>() },
            { "td", new List<string>() },
            { "iframe", new List<string>() {"src", "height", "width", "frameborder"} },
            { "a", new List<string> { "href", "target" } },
            { "img", new List<string> { "alt", "src", "height", "width", "float", "border-style", "border-width", "margin", "margin-left", "margin-top", "margin-right", "margin-bottom"} }
        };

        /// <summary>
        /// Elements removed together with their content.
        /// </summary>
        private static readonly HashSet<string> DroppedWithContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "object", "embed", "applet", "noscript", "noembed", "noframes", "template", "textarea",
            "select", "option", "svg", "math", "head", "title", "xml", "frame", "frameset", "base", "link", "meta"
        };

        private static readonly HashSet<string> VoidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "br", "img" };

        private static readonly HashSet<string> UrlAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src" };

        private static readonly string[] AllowedIframeHosts =
        {
            "www.youtube.com", "youtube.com", "www.youtube-nocookie.com", "player.vimeo.com"
        };

        private static readonly Regex SchemeExpression = new Regex(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):", RegexOptions.Compiled);
        private static readonly Regex ControlOrSpace = new Regex(@"[\x00-\x20\x7f]+", RegexOptions.Compiled);
        private static readonly Regex SafeStyleValue = new Regex(@"^[#a-zA-Z0-9\s.,%\-]+$", RegexOptions.Compiled);
        private static readonly Regex SafeDimension = new Regex(@"^\d{1,4}(%|px)?$", RegexOptions.Compiled);

        /// <summary>
        /// Extension filters your HTML to the whitelist specified in the ValidHtmlTags dictionary
        /// </summary>
        public static string FilterHtmlToWhitelist(this string text)
        {
            if(string.IsNullOrEmpty(text))
            {
                return text;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(text);

            var output = new StringBuilder(text.Length);
            WriteChildren(doc.DocumentNode, output);
            return output.ToString();
        }

        private static void WriteChildren(HtmlNode node, StringBuilder output)
        {
            foreach (var child in node.ChildNodes)
            {
                WriteNode(child, output);
            }
        }

        private static void WriteNode(HtmlNode node, StringBuilder output)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Text:
                    output.Append(HttpUtility.HtmlEncode(HtmlEntity.DeEntitize(((HtmlTextNode)node).Text)));
                    return;
                case HtmlNodeType.Element:
                    break;
                default:
                    // comments and anything else are dropped
                    return;
            }

            var name = node.Name.ToLowerInvariant();
            if (DroppedWithContent.Contains(name))
            {
                return;
            }

            List<string> allowed;
            if (!ValidHtmlTags.TryGetValue(name, out allowed))
            {
                // unknown tag: keep its (sanitised) content only
                WriteChildren(node, output);
                return;
            }

            var attributes = new StringBuilder();
            foreach (var attribute in node.Attributes)
            {
                var attrName = attribute.Name.ToLowerInvariant();
                var value = HtmlEntity.DeEntitize(attribute.Value ?? string.Empty).Trim();

                if (attrName == "style")
                {
                    value = FilterStyle(value, allowed);
                }
                else if (!allowed.Contains(attrName))
                {
                    continue;
                }
                else if (UrlAttributes.Contains(attrName))
                {
                    value = FilterUrl(value, name);
                }
                else if (attrName == "target")
                {
                    value = value == "_blank" ? value : null;
                }
                else if (attrName == "height" || attrName == "width" || attrName == "frameborder")
                {
                    value = SafeDimension.IsMatch(value) ? value : null;
                }

                if (string.IsNullOrEmpty(value))
                {
                    if (name == "iframe" && attrName == "src")
                    {
                        // an iframe without an allowed source is dropped entirely
                        return;
                    }

                    continue;
                }

                attributes.Append(' ').Append(attrName).Append("=\"").Append(HttpUtility.HtmlAttributeEncode(value)).Append('"');
            }

            if (name == "iframe" && node.Attributes["src"] == null)
            {
                return;
            }

            if (name == "a" && node.Attributes["target"] != null)
            {
                attributes.Append(" rel=\"noopener noreferrer\"");
            }

            output.Append('<').Append(name).Append(attributes);
            if (VoidElements.Contains(name))
            {
                output.Append(" />");
                return;
            }

            output.Append('>');
            if (name != "iframe")
            {
                WriteChildren(node, output);
            }

            output.Append("</").Append(name).Append('>');
        }

        private static string FilterUrl(string value, string tagName)
        {
            var normalized = ControlOrSpace.Replace(value, string.Empty);
            var scheme = SchemeExpression.Match(normalized);

            if (tagName == "iframe")
            {
                Uri uri;
                return Uri.TryCreate(normalized, UriKind.Absolute, out uri) &&
                       uri.Scheme == Uri.UriSchemeHttps &&
                       AllowedIframeHosts.Contains(uri.Host.ToLowerInvariant())
                    ? uri.AbsoluteUri
                    : null;
            }

            if (!scheme.Success)
            {
                // relative URL; protocol-relative "//host" is fine as it keeps the page's scheme
                return normalized.StartsWith("\\") ? null : value;
            }

            var s = scheme.Groups[1].Value.ToLowerInvariant();
            if (s == "http" || s == "https" || (s == "mailto" && tagName == "a"))
            {
                return value;
            }

            return null;
        }

        private static string FilterStyle(string style, List<string> allowed)
        {
            var result = new StringBuilder();
            foreach (var declaration in style.Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon < 1)
                {
                    continue;
                }

                var property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
                var value = declaration.Substring(colon + 1).Trim();
                if (allowed.Contains(property) && SafeStyleValue.IsMatch(value))
                {
                    result.Append(property).Append(':').Append(value).Append(';');
                }
            }

            return result.ToString();
        }
    }
}
