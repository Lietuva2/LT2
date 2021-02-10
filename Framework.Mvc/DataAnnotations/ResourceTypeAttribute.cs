using System;
using System.Web.Mvc;

namespace Framework.DataAnnotations
{
    /// <summary>
    /// Base class for custom MetadataAttributes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ResourceTypeAttribute : Attribute
    {
        public Type Type { get; set; }

        public ResourceTypeAttribute(Type type)
        {
            this.Type = type;
        }
    }
}
