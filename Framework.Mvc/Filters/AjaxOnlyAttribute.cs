using System.Reflection;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    public class AjaxOnlyAttribute : ActionMethodSelectorAttribute
    {
        #region " Methods "
        public override bool IsValidForRequest(ControllerContext controllerContext, MethodInfo methodInfo)
        {
            return controllerContext.HttpContext.Request.IsAjaxRequest();
        }
        #endregion
    }
}
