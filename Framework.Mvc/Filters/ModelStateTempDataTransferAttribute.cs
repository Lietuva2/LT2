using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Base class for the model state transfer with Post-Redirect-Get pattern.
    /// </summary>
    public abstract class ModelStateTempDataTransferAttribute : ActionFilterAttribute
    {
        protected static readonly string Key = typeof(ModelStateTempDataTransferAttribute).FullName;
    }
}
