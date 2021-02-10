using System;
using System.Web;
using System.Web.Mvc;
using Data.ViewModels.Account;
using Framework.Infrastructure.Logging;

using Services;
using Services.ModelServices;
using Web.Infrastructure.Authentication;
using System.Web.Security;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class AddressController : SiteBaseServiceController<AddressService>
    {
        public virtual ActionResult Municipality(string name, string returnTo)
        {
            Service.SetMunicipality(name);
            if(!string.IsNullOrEmpty(returnTo))
            {
                return Redirect(returnTo);
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult GetAllMunicipalities(string returnTo)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Common.Start());
            }

            var model = Service.GetAllMunicipalities();
            ViewBag.ReturnTo = returnTo;
            return Json(new { Content = RenderPartialViewToString(MVC.Shared.Views.Municipalities, model) });
        }

        public virtual ActionResult GetCountries(string prefix)
        {
            var countries = Service.GetCountries(prefix);
            return Json(countries);
        }

        public virtual ActionResult GetCities(string country, string municipality, string prefix)
        {
            var cities = Service.GetCities(country, municipality, prefix);
            return Json(cities);
        }

        public virtual ActionResult GetMunicipalities(string country, string prefix)
        {
            var cities = Service.GetMunicipalities(country, prefix);
            return Json(cities);
        }
    }
}
