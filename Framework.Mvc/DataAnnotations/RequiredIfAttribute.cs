using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

namespace Framework.Mvc.DataAnnotations
{
    public class RequiredIfAttribute : ValidationAttribute
    {
        public string OtherProperty { get; set; }

        public RequiredIfAttribute(string otherProperty)
            : base()
        {
            OtherProperty = otherProperty;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            PropertyInfo property = validationContext.ObjectType.GetProperty(this.OtherProperty);
            var isDeletedProperty = validationContext.ObjectType.GetProperty("IsDeleted");
            if (isDeletedProperty != null)
            {
                if ((bool)isDeletedProperty.GetValue(validationContext.ObjectInstance, null))
                {
                    return null;
                }
            }

            if (property == null)
            {
                return new ValidationResult(string.Format(CultureInfo.CurrentCulture, "RequiredIfAttribute_UnknownProperty", new object[] { this.OtherProperty }));
            }

            //the OtherProperty value, if it's filled, the "value" is required to not be null
            object objB = property.GetValue(validationContext.ObjectInstance, null);
            bool isFilled = false;
            bool filled = false;
            if (objB != null)
            {
                //this returns true if the value is not null, empty, and if the boolean value is true
                if (!string.IsNullOrEmpty(objB.ToString()))
                {
                    filled = true;
                }

                if (bool.TryParse(objB.ToString(), out isFilled))
                {
                    filled = isFilled;
                }
            }

            if ((objB != null && filled) && value == null)
            {
                return new ValidationResult(this.FormatErrorMessage(validationContext.DisplayName));
            }
            return null;
        }

        public override string FormatErrorMessage(string name)
        {
            if (string.IsNullOrEmpty(this.ErrorMessageResourceName) && this.ErrorMessageResourceType == null)
            {
                return string.Format("{0} is required", name);
            }

            return base.FormatErrorMessage(name);
        }
    }
}
