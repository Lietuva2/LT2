using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Framework.Mvc.Strings
{
    /// <summary>
    /// Filters HTML to the valid html tags set (with only the attributes specified)
    /// 
    /// Thanks to http://refactormycode.com/codes/333-sanitize-html for the original
    /// </summary>
    public static class HtmlSanitizeExtension
    {
        private const string HTML_TAG_PATTERN = @"(?'tag_start'</?)(?'tag'\w+)((\s+(?'attr'(?'attr_name'\w+)(\s*=\s*(?:"".*?""|'.*?'|[^'"">\s]+)))?)+\s*|\s*)(?'tag_end'/?>)";

        /// <summary>
        /// A dictionary of allowed tags and their respectived allowed attributes.  If no
        /// attributes are provided, all attributes will be stripped from the allowed tag
        /// </summary>
        public static Dictionary<string, List<string>> ValidHtmlTags = new Dictionary<string, List<string>> {
            { "p", new List<string>(){"text-align", "margin-left"} },
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
        /// Extension filters your HTML to the whitelist specified in the ValidHtmlTags dictionary
        /// </summary>
        public static string FilterHtmlToWhitelist(this string text)
        {
            if(string.IsNullOrEmpty(text))
            {
                return text;
            }

            Regex htmlTagExpression = new Regex(HTML_TAG_PATTERN, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

            return htmlTagExpression.Replace(text, m =>
            {
                if (!ValidHtmlTags.ContainsKey(m.Groups["tag"].Value))
                    return String.Empty;

                StringBuilder generatedTag = new StringBuilder(m.Length);

                Group tagStart = m.Groups["tag_start"];
                Group tagEnd = m.Groups["tag_end"];
                Group tag = m.Groups["tag"];
                Group tagAttributes = m.Groups["attr"];

                generatedTag.Append(tagStart.Success ? tagStart.Value : "<");
                generatedTag.Append(tag.Value);

                foreach (Capture attr in tagAttributes.Captures)
                {
                    int indexOfEquals = attr.Value.IndexOf('=');

                    // don't proceed any futurer if there is no equal sign or just an equal sign
                    if (indexOfEquals < 1)
                        continue;

                    string attrName = attr.Value.Substring(0, indexOfEquals).Trim();

                    // check to see if the attribute name is allowed and write attribute if it is
                    if (ValidHtmlTags[tag.Value].Contains(attrName))
                    {
                        generatedTag.Append(' ');
                        generatedTag.Append(attr.Value);
                    }

                    if (attrName == "style")
                    {
                        generatedTag.Append(" style=\"");
                        var styles = attr.Value.Substring(indexOfEquals + 1).Trim('"').Trim('\'').Split(';');
                        foreach (var style in styles)
                        {
                            int indexOfStyleEquals = style.IndexOf(':');

                            // don't proceed any futurer if there is no equal sign or just an equal sign
                            if (indexOfStyleEquals < 1)
                                continue;

                            string styleName = style.Substring(0, indexOfStyleEquals).Trim();
                            if (ValidHtmlTags[tag.Value].Contains(styleName))
                            {
                                generatedTag.Append(style);
                                generatedTag.Append(";");
                            }
                        }
                        generatedTag.Append("\"");
                    }
                }

                generatedTag.Append(tagEnd.Success ? tagEnd.Value : ">");

                return generatedTag.ToString();
            });
        }
    }
}
