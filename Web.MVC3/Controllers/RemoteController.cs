using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        [OutputCache(Duration = 60, Location = OutputCacheLocation.ServerAndClient, VaryByParam = "url")]
        public virtual ActionResult GetImage(string url)
        {
            if (url.Contains(Links.Content.Images.noimage_png))
            {
                url = Server.MapPath(Links.Content.Images.noimage_png);
            }

            byte[] image = null;
            try
            {
                image = GetHtmlData(url);
            }
            catch
            {
            }
            if (image == null)
            {
                image = GetHtmlData(Server.MapPath(Links.Content.Images.noimage_png));
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
            var title = doc.DocumentNode.ChildNodes.FindFirst("title").InnerText.Trim();

            return Json(title);
        }

        private static string GetHtmlPage(string strURL)
        {

            String strResult;
            WebResponse objResponse;
            
            try
            {
                WebRequest objRequest = HttpWebRequest.Create(strURL);
                objResponse = objRequest.GetResponse();
            }
            catch (Exception e)
            {
                return string.Empty;
            }

            using (StreamReader sr = new StreamReader(objResponse.GetResponseStream()))
            {
                strResult = sr.ReadToEnd();
                sr.Close();
            }
            return strResult;
        }

        public static byte[] GetHtmlData(string strURL)
        {
            if (string.IsNullOrEmpty(strURL))
            {
                return null;
            }

            WebClient wc = new WebClient();
            return wc.DownloadData(strURL);
            String strResult;
            WebResponse objResponse;

            try
            {
                WebRequest objRequest = HttpWebRequest.Create(strURL);
                objResponse = objRequest.GetResponse();
            }
            catch (Exception e)
            {
                return null;
            }
            MemoryStream memStream = new MemoryStream();
            var stream = objResponse.GetResponseStream();
            byte[] data = new byte[1024];
            while(true)
            {
                int bytesRead = stream.Read(data, 0, data.Length);
                if(bytesRead == 0)
                {
                    break;
                }

                memStream.Write(data, 0, bytesRead);
            }

            return memStream.ToArray();
        }
    }
}
