using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework.Mvc.Strings;

namespace Services.Classes
{
    public static class AntiXssExtensions
    {
        public static string Sanitize(this string text)
        {
            return text.FilterHtmlToWhitelist();
        }
    }
}
