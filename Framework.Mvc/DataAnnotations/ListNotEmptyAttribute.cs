using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Framework.Mvc.DataAnnotations
{
    public class ListNotEmptyAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var val = (IList)value;
            if(val == null || val.Count == 0)
            {
                return new ValidationResult(this.FormatErrorMessage(validationContext.DisplayName));
            }

            return null;
        }

        public override string FormatErrorMessage(string name)
        {
            if(string.IsNullOrEmpty(this.ErrorMessageResourceName) && this.ErrorMessageResourceType == null && string.IsNullOrEmpty(this.ErrorMessage))
            {
                return string.Format("{0} must be selected", name);
            }

            return base.FormatErrorMessage(name);
        }
    }
}
