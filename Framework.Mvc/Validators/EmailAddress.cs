using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Framework.Mvc.Validators
{
    /// <summary>
    /// Email address validator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class EmailAddressAttribute : DataTypeAttribute
    {
        /// <summary>
        /// Email regular expression.
        /// </summary>
        private readonly Regex regex = new Regex(@"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="EmailAddressAttribute"/> class.
        /// </summary>
        public EmailAddressAttribute()
            : base(DataType.EmailAddress)
        {
        }

        /// <summary>
        /// Checks that the value of the data field is valid.
        /// </summary>
        /// <param name="value">The data field value to validate.</param>
        /// <returns>true always.</returns>
        public override bool IsValid(object value)
        {
            var str = Convert.ToString(value, CultureInfo.CurrentCulture);

            if (String.IsNullOrEmpty(str))
            {
                return true;
            }

            var match = regex.Match(str);

            return ((match.Success && (match.Index == 0)) && (match.Length == str.Length));
        }
    }
}
