using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Framework.Infrastructure.Logging;

//Controller for a Error
namespace Web.Controllers
{
    public partial class ErrorController : SiteBaseController
    {
        ILogger _logger;
        
        public ErrorController(ILogger logger) {
            _logger = logger;
        }

        /// <summary>
        /// This is fired when the site hits a 500
        /// </summary>
        /// <returns></returns>
        public virtual ActionResult Problem()
        {
            //no logging here - let the app do it 
            //we don't get reliable error traps here
            return View();
        }
        /// <summary>
        /// This is fired when the site gets a bad URL
        /// </summary>
        /// <returns></returns>
        public virtual ActionResult NotFound(string aspxerrorpath)
        {
            //you should probably log this - if you're getting 
            //bad links you'll want to know...
            _logger.Warning(string.Format("404 - {0}", aspxerrorpath));
            return View();
        }
    }
}
