using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using Framework.Other;

namespace Framework.Strings
{
    public static class StringExtensions
    {
        private static Regex _urlRegEx;
        private static Regex UrlRegEx
        {
            get
            {
                if (_urlRegEx == null)
                {
                    var reg = ConfigurationManager.AppSettings["UrlRegEx"];
                    if (string.IsNullOrEmpty(reg))
                    {
                        reg =
                            @"((https?|ftp)://(-\.)?([^\s/?\.#-]+\.?)+(/[^\s]*)?$)|(((http|https|ftp)\://)?([a-zA-Z0-9\.\-]+(\:[a-zA-Z0-9\.&%\$\-]+)*@)*((25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9])\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9]|0)\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[1-9]|0)\.(25[0-5]|2[0-4][0-9]|[0-1]{1}[0-9]{2}|[1-9]{1}[0-9]{1}|[0-9])|localhost|([a-zA-Z0-9\-]+\.)*[a-zA-Z0-9\-]+\.(com|lt|edu|gov|int|mil|net|org|biz|arpa|info|name|pro|aero|coop|museum|[a-zA-Z]{2}($/)))(\:[0-9]+)*(/($|[a-zA-Z0-9\.\,\?\'\\\+&%\$#\=~_\-]+))*)";
                    }

                    _urlRegEx = new Regex(reg, RegexOptions.IgnoreCase);
                }

                return _urlRegEx;
            }
        }

        public static string ToTitleCase(this string str)
        {
            if(string.IsNullOrEmpty(str))
            {
                return str;
            }

            CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
            TextInfo textInfo = cultureInfo.TextInfo;
            return textInfo.ToTitleCase(str.ToLower());
        }

        public static string FirstCharToUpper(this string input)
        {
            if (String.IsNullOrEmpty(input))
                return input;
            return input.First().ToString().ToUpper() + String.Join("", input.Skip(1));
        }

        /// <summary>
        /// Remove HTML tags from string using char array.
        /// </summary>
        public static string StripHtml(this string source)
        {
            char[] array = new char[source.Length];
            int arrayIndex = 0;
            bool inside = false;

            for (int i = 0; i < source.Length; i++)
            {
                char let = source[i];
                if (let == '<')
                {
                    inside = true;
                    continue;
                }
                if (let == '>')
                {
                    inside = false;
                    continue;
                }
                if (!inside)
                {
                    array[arrayIndex] = let;
                    arrayIndex++;
                }
            }
            return new string(array, 0, arrayIndex);
        }

        public static bool IsNullOrEmpty(this string s)
        {
            return string.IsNullOrEmpty(s);
        }

        public static string NewLineToHtml(this string s)
        {
            if(string.IsNullOrEmpty(s))
            {
                return s;
            }

            return s.Replace(Environment.NewLine, "<br/>").Replace("\n", "<br/>");
        }

        public static string RemoveNewLines(this string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            return s.Replace(Environment.NewLine, string.Empty).Replace("\n", string.Empty);
        }

        public static string ActivateLinks(this string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("iframe", StringComparison.InvariantCultureIgnoreCase) >= 0 || s.IndexOf(@"</a>") >= 0)
            {
                return s;
            }

            var regex = UrlRegEx;
            var processedString = s;
            var lastProcessedIndex = -1;
            foreach(Match match in regex.Matches(s))
            {
                if(processedString.IndexOf(match.Value) > lastProcessedIndex)
                {
                    string val = match.Value;
                    if(!Regex.IsMatch(val.Substring(val.Length - 1), @"\w"))
                    {
                        val = val.Substring(0, val.Length - 1);
                    }
                    string href = string.Format("<a href='{0}' target='_blank'>{1}</a>",
                                                val.AddUrlProtocol(), val);
                    lastProcessedIndex = processedString.IndexOf(val);
                    processedString = processedString.Replace(val, href);
                    lastProcessedIndex += href.Length;
                }
            }

            return processedString;
        }

        public static string AddUrlProtocol(this string url)
        {
            if (!url.Trim().StartsWith("http"))
            {
                url = "http://" + url.Trim();
            }

            return url;
        }

        public static string ToSeoUrl(this string url)
        {
            // make the url lowercase
            string encodedUrl = (url ?? "").ToLower();

            // replace & with and
            encodedUrl = Regex.Replace(encodedUrl, @"\&+", "and");

            // remove characters
            encodedUrl = encodedUrl.Replace("'", "");

            encodedUrl = encodedUrl.Replace('ą', 'a');
            encodedUrl = encodedUrl.Replace('č', 'c');
            encodedUrl = encodedUrl.Replace('ę', 'e');
            encodedUrl = encodedUrl.Replace('ė', 'e');
            encodedUrl = encodedUrl.Replace('į', 'i');
            encodedUrl = encodedUrl.Replace('š', 's');
            encodedUrl = encodedUrl.Replace('ų', 'u');
            encodedUrl = encodedUrl.Replace('ū', 'u');
            encodedUrl = encodedUrl.Replace('ž', 'z');

            // remove invalid characters
            encodedUrl = Regex.Replace(encodedUrl, @"[^a-z0-9]", "-");

            // remove duplicates
            encodedUrl = Regex.Replace(encodedUrl, @"-+", "-");

            // trim leading & trailing characters
            encodedUrl = encodedUrl.Trim('-');
            encodedUrl = encodedUrl.LimitLength(80, "");

            return encodedUrl;
        }

        public static string LimitLength(this string s, int length, string postfix = "..")
        {
            if(string.IsNullOrEmpty(s))
            {
                return s;
            }

            var retString = s.Substring(0, s.Length > length ? length : s.Length);
            if(s.Length > length)
            {
                retString += postfix;
            }

            return retString;
        }

        public static string LimitLength(this string s)
        {
            return LimitLength(s, 25);
        } 

        public static string FirstParagraph(this string s)
        {
            if(string.IsNullOrEmpty(s))
            {
                return s;
            }

            if(s.Contains(@"<p>"))
            {
                return s.Substring(s.IndexOf(@"<p>"), s.IndexOf(@"</p>") + 4);
            }

            if(s.Contains(@"<br"))
            {
                return s.Substring(0, s.IndexOf(@"<br"));
            }

            var i = s.IndexOf(@"\n");
            return s.Substring(0, i > 0 ? i : s.Length);
        }

        public static DateTime ConvertPersonCodeToBirthDate(this string personCode)
        {
            if (string.IsNullOrEmpty(personCode))
            {
                throw new ArgumentException("personCode cannot be empty");
            }

            if (personCode.Length != 11)
            {
                throw new ArgumentException("personCode must be 11 numbers");
            }

            if (!personCode.First().In('1', '2', '3', '4', '5', '6'))
            {
                throw new ArgumentException("personCode must start with a number from 1 to 6");
            }

            var is2000 = personCode.First() == '5' || personCode.First() == '6';
            var is1800 = personCode.First() == '1' || personCode.First() == '2';
            var is1900 = personCode.First() == '3' || personCode.First() == '4';
            string yearString = personCode.Substring(1, 2);
            string monthString = personCode.Substring(3, 2);
            string dayString = personCode.Substring(5, 2);

            var year = is1900
                           ? Convert.ToInt32("19" + yearString)
                           : is2000
                                 ? Convert.ToInt32("20" + yearString)
                                 : is1800 ? Convert.ToInt32("18" + yearString) : 0;

            return new DateTime(year, Convert.ToInt32(monthString), Convert.ToInt32(dayString));
        }

        public static bool IsValidLtPersonCode(object value)
        {
            int century;

            // Make it not required
            if (value == null || value.ToString().Length == 0)
            {
                return true;
            }

            var personCode = value.ToString();
            if (personCode.Length != 11)
            {
                return false;
            }

            if (!Regex.IsMatch(personCode, @"^\d+$"))
            {
                return false;
            }

            var firstDigit = personCode.First();

            switch (firstDigit)
            {
                case '1':
                case '2':
                    century = 18;
                    break;
                case '3':
                case '4':
                    century = 19;
                    break;
                case '5':
                case '6':
                    century = 20;
                    break;
                case '7':
                case '8':
                    century = 21;
                    break;
                case '9':
                    return true;
                default:
                    return false;
            }

            var birthDateString = personCode.Substring(1, 6);
            var birthDateWithoutYearString = birthDateString.Substring(2, 4);

            if (birthDateString != "000000" && birthDateWithoutYearString != "0000")
            {
                DateTime birthDate;
                if (DateTime.TryParseExact(century + birthDateString, "yyyyMMdd", null, DateTimeStyles.None, out birthDate))
                {
                    if (birthDate > DateTime.Now)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (birthDateWithoutYearString == "0000" && int.Parse(century + personCode.Substring(0, 2)) > DateTime.Now.Year)
            {
                return false;
            }

            var digits = personCode.Select(x => int.Parse(x.ToString())).ToList();
            var checksumDefault =
                (digits[0] +
                (digits[1] * 2) +
                (digits[2] * 3) +
                (digits[3] * 4) +
                (digits[4] * 5) +
                (digits[5] * 6) +
                (digits[6] * 7) +
                (digits[7] * 8) +
                (digits[8] * 9) +
                 digits[9]) % 11;

            if (checksumDefault < 10 && checksumDefault == digits[10])
            {
                return true;
            }

            if (checksumDefault == 10)
            {
                var checksumDerived =
                    ((digits[0] * 3) +
                     (digits[1] * 4) +
                     (digits[2] * 5) +
                     (digits[3] * 6) +
                     (digits[4] * 7) +
                     (digits[5] * 8) +
                     (digits[6] * 9) +
                      digits[7] +
                     (digits[8] * 2) +
                     (digits[9] * 3)) % 11;

                if (checksumDerived == 10)
                {
                    checksumDerived = 0;
                }

                if (checksumDerived == digits[10])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
