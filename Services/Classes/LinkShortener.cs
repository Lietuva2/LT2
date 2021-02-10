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
        private const string BITLY_API_URL =
            "https://api-ssl.bitly.com/v3/shorten?login=satanod&apiKey=R_5db2d179fdd0797d3524b70b53210f73&longUrl={0}";

        public static string GetBitlyLink(string url)
        {
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
