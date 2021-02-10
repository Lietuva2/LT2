using System.Linq;
using System.Web;
using Framework.Strings;
using HtmlAgilityPack;
using Microsoft.Security.Application;

namespace Framework.Mvc.Strings
{
    public static class StringExtensions
    {
        public static string GetSafeHtml(this string s)
        {
            return AntiXss.GetSafeHtmlFragment(s);
        }

        public static string GetPlainText(this string html, int? length = null, bool firstParagraphOnly = true)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);
            if (firstParagraphOnly)
            {
                var ps = doc.DocumentNode.Elements("p");
                if (ps.Any())
                {
                    foreach (var p in ps)
                    {
                        var text = HttpUtility.HtmlDecode(p.InnerText.Trim());
                        if (text.Length > 0)
                        {
                            if (length.HasValue)
                            {
                                text = text.LimitLength(length.Value);
                            }

                            return text;
                        }
                    }
                }
            }
            var txt = HttpUtility.HtmlDecode(doc.DocumentNode.InnerText.Trim());
            if(length.HasValue)
            {
                txt = txt.LimitLength(length.Value);
            }

            return txt;
        }

        public static string HtmlDecode(this string html)
        {
            return HttpUtility.HtmlDecode(html);
        }
    }
}
