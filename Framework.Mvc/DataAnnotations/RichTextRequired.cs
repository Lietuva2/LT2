using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace Framework.Mvc.DataAnnotations
{
    public class RichTextRequiredAttribute : RequiredAttribute, IClientValidatable
    {

        public RichTextRequiredAttribute()
        {

        }

        public override bool IsValid(object value)
        {
            if(value == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(value.ToString().Replace("<br>", ""));
        }

        public IEnumerable<ModelClientValidationRule> GetClientValidationRules(ModelMetadata metadata, ControllerContext context)
        {
            return new ModelClientValidationRule[] { new ModelClientValidationRule() { ValidationType = "rtrequired", ErrorMessage = FormatErrorMessage(metadata.GetDisplayName())} };
        }
    }
}
