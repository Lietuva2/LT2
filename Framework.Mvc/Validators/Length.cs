using System;
using System.ComponentModel.DataAnnotations;

namespace Framework.Mvc.Validators
{
    /// <summary>
    /// The text length validator.
    /// </summary>
    public class Length : StringLengthAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Length"/> class.
        /// </summary>
        /// <param name="maximumLength">The maximum length of a string.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 	<paramref name="maximumLength"/> is negative.
        /// </exception>
        public Length(int maximumLength) 
            : base(maximumLength)
        {
        }

        /// <summary>
        /// Applies formatting to a specified error message.
        /// </summary>
        /// <param name="name">The error message to format.</param>
        /// <returns>The formatted error message.</returns>
        public override string FormatErrorMessage(string name)
        {
            return ErrorMessage.Replace("{0}", MaximumLength.ToString());
        }
    }
}
