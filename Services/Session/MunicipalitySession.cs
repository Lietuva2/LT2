using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using Framework.Data;
using iTextSharp.text;
using Services.ModelServices;

namespace Services.Session
{
    public static class WorkContext
    {
        public static MunicipalityInfo Municipality
        {
            get { return HttpContext.Current != null && HttpContext.Current.Session != null ? HttpContext.Current.Session["Municipality"] as MunicipalityInfo : null; }
            set { HttpContext.Current.Session["Municipality"] = value; }
        }

        public static List<int> CategoryIds
        {
            get
            {
                var categoryIds = ConfigurationManager.AppSettings["CategoryIds"];
                if (categoryIds == null)
                {
                    return null;
                }

                var ids = categoryIds.Split(',').Select(s => Convert.ToInt32(s)).ToList();

                return ids;
            }
        }
    }
}
