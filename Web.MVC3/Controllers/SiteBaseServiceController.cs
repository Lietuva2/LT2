using System;
using System.Globalization;
using System.Threading;
using System.Web.Mvc;
using Elmah;
using Framework;
using Ninject;
using Services.ModelServices;

namespace Web.Controllers
{
    public class SiteBaseServiceController<TService> : SiteBaseController
        where TService : class, IService
    {
        [Inject]
        public TService Service { get; set; }

        protected ActionResult ProcessError(Exception e, bool logError = true, bool deepestMessage = true)
        {
            if (deepestMessage)
            {
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                }
            }

            LastVoteUrl = Request.Url;
            if (logError)
            {
                Logger.Error(e);
                if (HttpContext.IsCustomErrorEnabled)
                {
                    ErrorSignal.FromContext(System.Web.HttpContext.Current).Raise(e, System.Web.HttpContext.Current);
                }
            }

            if (IsAjaxRequest)
            {
                return Jsonp(
                    new
                        {
                            Error = e.GetType().ToString() + ": " + e.Message,
                            StackTrace = e.StackTrace,
                            Url = Request.Url
                        },
                    JsonRequestBehavior.AllowGet
                    );
            }

            throw e;
        }
    }
}