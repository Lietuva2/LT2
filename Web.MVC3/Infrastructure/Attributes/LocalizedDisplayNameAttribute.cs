using System;
using System.ComponentModel;
using System.Resources;

namespace Web.Infrastructure.Attributes
{
    public class LocalizedDisplayNameAttribute : DisplayNameAttribute
    {
        public LocalizedDisplayNameAttribute(string resourceType, string resourceKey)
        {
            ResourceKey = resourceKey;
            ResourceType = resourceType;
        }

        public LocalizedDisplayNameAttribute(Type containerClassType, string resourceKey)
        {
            ResourceKey = resourceKey;
            ResourceType = containerClassType.FullName.Replace("Web.Model", "Web.Resources.Model");
        }

        public override string DisplayName
        {
            get
            {
                string displayName = string.Empty;
                var type = Type.GetType(ResourceType);
                if(type == null)
                {
                    type = Type.GetType("Web.Resources.Model." + ResourceType);
                }
                if (type != null)
                {
                    var resMgr = new ResourceManager(type);
                    displayName = resMgr.GetString(ResourceKey);
                }

                return string.IsNullOrEmpty(displayName)
                               ? string.Format("[[{0}]]", ResourceKey)
                               : displayName;
            }
        }

        private string ResourceKey { get; set; }
        private string ResourceType { get; set; }
    }
}