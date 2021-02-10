using System.Web;
using System.Web.Mvc;
using Framework.Mvc.Strings;
using Microsoft.Security.Application;

namespace Web.Helpers {
    public static class XSSHelper {
        public static string h(this HtmlHelper helper, string input)
        {
            return AntiXss.HtmlEncode(input); 
        }
        public static IHtmlString Sanitize(this HtmlHelper helper, string input) {
            return helper.Raw(AntiXss.GetSafeHtmlFragment(input));
        }
        /// <summary>
        /// Encodes Javascript
        /// </summary>
        public static string hscript(this HtmlHelper helper, string input) {
            return AntiXss.JavaScriptEncode(input);
        }
    }
}
