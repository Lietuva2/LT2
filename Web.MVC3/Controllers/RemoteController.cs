using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.UI;
using Framework.Strings;
using HtmlAgilityPack;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class RemoteController : Controller
    {
        private const int MaxRemoteBytes = 2 * 1024 * 1024;

        [OutputCache(Duration = 60, Location = OutputCacheLocation.ServerAndClient, VaryByParam = "url")]
        public virtual ActionResult GetImage(string url)
        {
            Response.AppendHeader("X-Content-Type-Options", "nosniff");

            var image = GetHtmlData(url);
            if (image == null)
            {
                return File(Server.MapPath(Links.Content.Images.noimage_png), "image/png");
            }

            return File(image, System.Net.Mime.MediaTypeNames.Image.Jpeg);
        }

        public virtual ActionResult GetRemoteTitle(string url)
        {
            string html = GetHtmlPage(url.AddUrlProtocol());
            if(string.IsNullOrEmpty(html))
            {
                return Json(html);
            }

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);
            var titleNode = doc.DocumentNode.ChildNodes.FindFirst("title");
            var title = titleNode != null ? titleNode.InnerText.Trim() : string.Empty;

            return Json(title);
        }

        private static string GetHtmlPage(string strURL)
        {
            var data = GetHtmlData(strURL);
            return data != null ? Encoding.UTF8.GetString(data) : string.Empty;
        }

        /// <summary>
        /// Downloads a public http(s) resource. Local files, other schemes, non-default ports,
        /// private, loopback and link-local addresses and redirects are refused, so a user-supplied
        /// URL cannot be used to read files on the server or reach internal services.
        /// </summary>
        public static byte[] GetHtmlData(string strURL)
        {
            var uri = ToSafeRemoteUri(strURL);
            if (uri == null)
            {
                return null;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300)
                    {
                        return null;
                    }

                    using (var stream = response.GetResponseStream())
                    using (var memStream = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (memStream.Length + bytesRead > MaxRemoteBytes)
                            {
                                return null;
                            }

                            memStream.Write(buffer, 0, bytesRead);
                        }

                        return memStream.ToArray();
                    }
                }
            }
            catch (WebException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static Uri ToSafeRemoteUri(string url)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                return null;
            }

            if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !uri.IsDefaultPort)
            {
                return null;
            }

            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            }
            catch (Exception)
            {
                return null;
            }

            if (addresses.Length == 0 || addresses.Any(IsNonPublicAddress))
            {
                return null;
            }

            return uri;
        }

        private static bool IsNonPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            var b = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                       address.Equals(IPAddress.IPv6Any) || (b[0] & 0xfe) == 0xfc;
            }

            return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224 ||
                   (b[0] == 169 && b[1] == 254) ||
                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                   (b[0] == 192 && b[1] == 168) ||
                   (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }
    }
}
