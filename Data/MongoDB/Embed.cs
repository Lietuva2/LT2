using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Framework.Mvc.Strings;

namespace Data.MongoDB
{
    public class Embed
    {
        public string Type { get; set; }
        public string Version { get; set; }
        [AllowHtml]
        public string Title { get; set; }
        public string Author_Name { get; set; }
        public string Author_Url { get; set; }
        public string Provider_Name { get; set; }
        public string Provider_Url { get; set; }
        public int? Cache_Age { get; set; }
        public string Thumbnail_Url { get; set; }
        
        private string _description = string.Empty;
        [AllowHtml]
        public string Description
        {
            get
            {
                if (String.IsNullOrEmpty(_description))
                    return _description;
                if (!string.IsNullOrWhiteSpace(_description.HtmlDecode()))
                {
                    return _description.HtmlDecode();
                }

                return _description;
            }
            set { _description = value; }
        }

        public string Url { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Html { get; set; }
        public bool IsEmpty {get { return string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(Html); }}
    }
}
