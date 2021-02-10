using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using Globalization.Resources.Shared;

namespace Services.Infrastructure
{
    public static class CustomAppSettings
    {
        public static int PageSizeList { get { return Convert.ToInt32(ConfigurationManager.AppSettings["PageSizeList"] ?? "15"); } }
        public static int PageSizeComment { get { return Convert.ToInt32(ConfigurationManager.AppSettings["PageSizeComment"] ?? "5"); } }
        public static string SearchIndexFolder { get { return ConfigurationManager.AppSettings["SearchIndexFolder"] ?? "C:\\SearchIndex"; } }
        public static int Version { get { return Convert.ToInt16(ConfigurationManager.AppSettings["Version"] ?? "1"); } }
        public static bool UseLocalScripts { get { return Convert.ToBoolean(ConfigurationManager.AppSettings["UseLocalScripts"] ?? "true"); } }
        public static bool RequireUniqueAuthentication { get { return Convert.ToBoolean(ConfigurationManager.AppSettings["RequireUniqueAuthentication"] ?? "false"); } }
        public static int PageSizeNewsFeed { get { return Convert.ToInt32(ConfigurationManager.AppSettings["PageSizeNewsFeed"] ?? "30"); } }
        public static int AutocompleteItemsCount { get { return Convert.ToInt32(ConfigurationManager.AppSettings["AutocompleteItemsCount"] ?? "10"); } }
        public static int MinPasswordLength { get { return Convert.ToInt32(ConfigurationManager.AppSettings["MinPasswordLength"] ?? "6"); } }
        public static int PageSizeStartPage { get { return Convert.ToInt32(ConfigurationManager.AppSettings["PageSizeStartPage"] ?? "10"); } }
        public static string GoogleDocIconUrl { get { return ConfigurationManager.AppSettings["GoogleDocIconUrl"] ?? ""; } }
        public static string GoogleAppId { get { return ConfigurationManager.AppSettings["GoogleAppId"] ?? ""; } }
        public static string GoogleAppSecret { get { return ConfigurationManager.AppSettings["GoogleAppSecret"] ?? ""; } }
        public static string RCAuthUrl { get { return ConfigurationManager.AppSettings["RCAuthUrl"] ?? "https://id3.rcsc.lt/RCSCAuth/explain.do"; } }
        public static string IPasasAuthUrl { get { return ConfigurationManager.AppSettings["IPasasAuthUrl"] ?? "https://ipastest.kada.lt/index.php"; } }
        public static bool RequireSsl { get { return Convert.ToBoolean(ConfigurationManager.AppSettings["RequireSsl"] ?? "false"); } }
        public static string MainIdeaId { get { return ConfigurationManager.AppSettings["MainIdeaId"] ?? ""; } }
        public static string ViispPid { get { return ConfigurationManager.AppSettings["ViispPid"] ?? ""; } }
        public static string ViispPostBack { get { return ConfigurationManager.AppSettings["ViispPostBack"] ?? ""; } }
        public static string SbAuthUrl { get { return ConfigurationManager.AppSettings["SbAuthUrl"] ?? "https://online.sb.lt/ib/site/authorization/login?system=lietuva2.0"; } }
        public static string FbPageId { get { return System.Configuration.ConfigurationManager.AppSettings["FbPageId"] ?? ""; } }
        public static string SsoBaseUrl { get { return ConfigurationManager.AppSettings["SsoBaseUrl"] ?? ""; } }
        public static string SsoSharedSecret { get { return ConfigurationManager.AppSettings["SsoSharedSecret"] ?? ""; } }
        public static bool IsViispEnabled { get { return Convert.ToBoolean(ConfigurationManager.AppSettings["ViispEnabled"] ?? "false"); } }

        public static string SiteName
        {
            get
            {
                var configName = ConfigurationManager.AppSettings["SiteName"];
                if (configName == null)
                {
                    return SharedStrings.SiteName;
                }

                return configName;
            }
        }
    }
}