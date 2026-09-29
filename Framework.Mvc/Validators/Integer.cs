using System.ComponentModel.DataAnnotations;

namespace Framework.Mvc.Validators
{
    /// <summary>
    /// Integer validator.
    /// </summary>
    public class Integer : DataTypeAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Integer"/> class.
        /// </summary>
        public Integer()
            : base(DataType.Custom)
        {
        }

        /// <summary>
        /// Checks that the value of the data field is valid.
        /// </summary>
        /// <param name="value">The data field value to validate.</param>
        /// <returns>true always.</returns>
        public override bool IsValid(object value)
        {
            return true;
        }
    }
}
