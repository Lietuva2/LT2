using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Web.Controllers
{
    public partial class AboutController : SiteBaseController
    {
        public virtual ActionResult About()
        {
            return View();
        }

        public virtual ActionResult Video()
        {
            return View();
        }

        public virtual ActionResult Ideas()
        {
            return View();
        }

        public virtual ActionResult Manifest()
        {
            return View();
        }

        public virtual ActionResult RoadMap()
        {
            return View();
        }

        public virtual ActionResult Contacts()
        {
            return View();
        }

        public virtual ActionResult Faq()
        {
            return View();
        }
    }
}
