using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

namespace Framework.Mvc.DataAnnotations
{
    public class RequiredIfNotAttribute : ValidationAttribute
    {
        public string OtherProperty { get; set; }

        public RequiredIfNotAttribute(string otherProperty)
            : base()
        {
            OtherProperty = otherProperty;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            PropertyInfo property = validationContext.ObjectType.GetProperty(this.OtherProperty);
            var isDeletedProperty = validationContext.ObjectType.GetProperty("IsDeleted");
            if(isDeletedProperty != null)
            {
                if((bool)isDeletedProperty.GetValue(validationContext.ObjectInstance, null))
                {
                    return null;
                }
            }

            if (property == null)
            {
                return new ValidationResult(string.Format(CultureInfo.CurrentCulture, "RequiredIfAttribute_UnknownProperty", new object[] { this.OtherProperty }));
            }
            object objB = property.GetValue(validationContext.ObjectInstance, null);
            if(objB is bool)
            {
                if ((bool)objB == false && value == null)
                {
                    return new ValidationResult(this.FormatErrorMessage(validationContext.DisplayName));
                }
                return null;
            }

            bool isTrue = false;
            if(objB != null && !string.IsNullOrEmpty(objB.ToString()))
            {
                return null;
            }

            if (objB != null)
            {
                bool.TryParse(objB.ToString(), out isTrue);
            }

            if ((objB != null && isTrue == false) && value == null)
            {
                return new ValidationResult(this.FormatErrorMessage(validationContext.DisplayName));
            }
            return null;
        }

        public override string FormatErrorMessage(string name)
        {
            if(string.IsNullOrEmpty(this.ErrorMessageResourceName) && this.ErrorMessageResourceType == null)
            {
                return string.Format("{0} is required", name);
            }

            return base.FormatErrorMessage(name);
        }
    }
}
