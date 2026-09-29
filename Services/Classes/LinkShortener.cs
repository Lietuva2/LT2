using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Services.Classes
{
    public class BitlyResult
    {
        public class ReturnData
        {
            public string Url { get; set; }
            public string Long_Url { get; set; }
            public string Hash { get; set; }
            public string Global_Hash { get; set; }
            public int New_Hash { get; set; }
        }

        public int Status_Code { get; set; }
        public string Status_Txt { get; set; }
        public ReturnData Data { get; set; }

        public BitlyResult()
        {
            Data = new ReturnData();
        }
    }
    public static class LinkShortener
    {
        // The login and API key are read from the BitlyLogin and BitlyApiKey settings.
        private static string BITLY_API_URL
        {
            get
            {
                var login = System.Configuration.ConfigurationManager.AppSettings["BitlyLogin"];
                var key = System.Configuration.ConfigurationManager.AppSettings["BitlyApiKey"];
                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(key))
                {
                    return null;
                }

                return "https://api-ssl.bitly.com/v3/shorten?login=" + Uri.EscapeDataString(login) + "&apiKey=" +
                       Uri.EscapeDataString(key) + "&longUrl={0}";
            }
        }

        public static string GetBitlyLink(string url)
        {
            if (BITLY_API_URL == null)
            {
                return null;
            }

            var client = new HttpClient();
            var bitlyUrl = string.Format(BITLY_API_URL, HttpContext.Current.Server.UrlEncode(url));
            var response = client.GetAsync(bitlyUrl).Result;
            //response.EnsureSuccessStatusCode();

            if (!response.Content.ReadAsStringAsync().Result.Contains("\"status_txt\": \"OK\""))
            {
                return null;
            }

            var content = response.Content.ReadAsAsync<BitlyResult>().Result;

            return content.Data.Url;
        }

        public static async Task<string> GetBitlyLinkAsync(string url)
        {
            if (BITLY_API_URL == null)
            {
                return null;
            }

            var client = new HttpClient();
            var bitlyUrl = string.Format(BITLY_API_URL, HttpContext.Current.Server.UrlEncode(url));
            // Send a request asynchronously continue when complete
            var response = await client.GetAsync(bitlyUrl);

            // Check that response was successful or throw exception
            //response.EnsureSuccessStatusCode();

            // Read response asynchronously as JsonValue and write out top facts for each country
            if(!response.Content.ReadAsStringAsync().Result.Contains("\"status_txt\": \"OK\""))
            {
                return null;
            }

            var content = await response.Content.ReadAsAsync<BitlyResult>();

            return content.Data.Url;
        }
    }
}
