using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Web.Mvc;
using Framework.DataAnnotations;
using Framework.Mvc.DataAnnotations;
using CompareAttribute = System.Web.Mvc.CompareAttribute;

namespace Web.Infrastructure.Meta
{
    public class CustomModelMetadataProvider :
             DataAnnotationsModelMetadataProvider
    {
        protected override ModelMetadata CreateMetadata(
            IEnumerable<Attribute> attributes,
            Type containerType,
           Func<object> modelAccessor,
            Type modelType,
            string propertyName)
        {
            //CustomModelMetadata
            var modelMetadata = base.CreateMetadata(attributes, containerType, modelAccessor, modelType, propertyName);
            attributes.OfType<MetadataAttribute>().ToList().ForEach(x => x.Process(modelMetadata));

            //LocalizedDataAnotations
            var meta = base.CreateMetadata
                (attributes, containerType, modelAccessor, modelType, propertyName);

            if (string.IsNullOrEmpty(propertyName))
                return meta;

            var type = GetResourceType(meta);

            if (meta.DisplayName == null)
                GetLocalizedDisplayName(meta, type, propertyName);

            if (string.IsNullOrEmpty(meta.DisplayName))
                meta.DisplayName = string.Format("[[{0}]]", propertyName);

            foreach (ValidationAttribute attr in attributes.Where(a => a.GetType().IsSubclassOf(typeof(ValidationAttribute))))
            {
                if(string.IsNullOrEmpty(attr.ErrorMessage) && string.IsNullOrEmpty(attr.ErrorMessageResourceName) && attr.ErrorMessageResourceType == null)
                {
                    if (type != null)
                    {
                        if (attr is RichTextRequiredAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "IsRequired";
                        }
                        else if (attr is RequiredAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "IsRequired";
                        }
                        else if (attr is RequiredIfAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "IsRequired";
                        }
                        else if (attr is RequiredIfNotAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "IsRequired";
                        }
                        else if(attr is RangeAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "RangeIsNotValid";
                        }
                        else if(attr is RegularExpressionAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "RegexpIsNotValid";
                        }
                        else if(attr is StringLengthAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "LengthIsNotValid";
                        }
                        else if (attr is RemoteAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "RemoteValidationFailed";
                        }
                        else if (attr is ListNotEmptyAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "IsEmpty";
                        }
                        else if (attr is CompareAttribute)
                        {
                            attr.ErrorMessageResourceName = propertyName + "CompareFailed";
                        }
                        else
                        {
                            continue;
                        }

                        attr.ErrorMessageResourceType = type;
                    }
                }
            }

            return meta;
        }

        private static void GetLocalizedDisplayName(ModelMetadata meta, Type type, string propertyName)
        {
            if (type != null)
            {
                ResourceManager resourceManager = new ResourceManager(type);
                CultureInfo culture = Thread.CurrentThread.CurrentUICulture;

                meta.DisplayName = resourceManager.GetString(propertyName, culture);
            }
        }

        private static Type GetResourceType(ModelMetadata meta)
        {
            var attr = (ResourceTypeAttribute[])meta.ContainerType.GetCustomAttributes(typeof(ResourceTypeAttribute), false);
            if (attr.Length > 0)
            {
                return attr.First().Type;
            }
            //Data.ViewModels.User.LogOnModel
            //Globalization.Resources.Model.User.Resource
            var containerName = meta.ContainerType.FullName;
            var resourceName = containerName.Replace("Data.ViewModels", "Globalization.Resources").Replace("+", "_");
            resourceName = resourceName.Substring(0, resourceName.LastIndexOf('.'));
            resourceName += ".Resource, LT2.Globalization";
            return Type.GetType(resourceName);
        }
    }
}