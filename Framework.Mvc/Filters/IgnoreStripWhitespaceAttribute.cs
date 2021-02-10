using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Strips whitespaces from the HTML markup.
    /// </summary>
    public class IgnoreStripWhitespaceAttribute : ActionFilterAttribute
    {
    }
}
