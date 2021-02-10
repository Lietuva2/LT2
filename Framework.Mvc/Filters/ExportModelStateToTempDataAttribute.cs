using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Exports model state to TempData to use with Post-Redirect-Get pattern.
    /// </summary>
    public class ExportModelStateToTempDataAttribute : ModelStateTempDataTransferAttribute
    {
        /// <summary>
        /// Called by the MVC framework after the action method executes.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            // only export when ModelState is not valid
            if (!filterContext.Controller.ViewData.ModelState.IsValid)
            {
                // export if we are redirecting
                if ((filterContext.Result is RedirectResult) || (filterContext.Result is RedirectToRouteResult))
                {
                    filterContext.Controller.TempData[Key] = filterContext.Controller.ViewData.ModelState;
                }
            }

            base.OnActionExecuted(filterContext);
        }
    }
}