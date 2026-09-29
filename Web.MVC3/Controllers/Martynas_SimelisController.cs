using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Web.Controllers
{
    public partial class Martynas_SimelisController : SiteBaseController
    {
        public virtual ActionResult Award()
        {
            return View();
        }

        public virtual ActionResult Biography()
        {
            return View();
        }

        public virtual ActionResult Portfolio()
        {
            return View();
        }
    }
}
