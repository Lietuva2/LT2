using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Imports model state from TempData to use with Post-Redirect-Get pattern.
    /// </summary>
    public class ImportModelStateFromTempDataAttribute : ModelStateTempDataTransferAttribute
    {
        /// <summary>
        /// Called by the MVC framework after the action method executes.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            var modelState = filterContext.Controller.TempData[Key] as ModelStateDictionary;

            if (modelState != null)
            {
                // only Import if we are viewing
                if (filterContext.Result is ViewResult)
                {
                    filterContext.Controller.ViewData.ModelState.Merge(modelState);
                    filterContext.Controller.TempData.Remove(Key);
                }
            }

            base.OnActionExecuted(filterContext);
        }
    }
}