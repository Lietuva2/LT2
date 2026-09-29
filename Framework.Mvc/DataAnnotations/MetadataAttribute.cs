using System;
using System.Web.Mvc;

namespace Framework.Mvc.DataAnnotations
{
    /// <summary>
    /// Base class for custom MetadataAttributes.
    /// </summary>
    public abstract class MetadataAttribute : Attribute
    {
        /// <summary>
        /// Method for processing custom attribute data.
        /// </summary>
        /// <param name="modelMetaData">A ModelMetaData instance.</param>
        public abstract void Process(ModelMetadata modelMetaData);
    }
}
