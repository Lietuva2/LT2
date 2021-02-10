using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Framework.Mvc.DataAnnotations;
using Framework.Mvc.Validators;

namespace Data.ViewModels.Account
{
    /// <summary>
    /// View model for remind password action.
    /// </summary>
    public class PasswordResetModel
    {
        /// <summary>
        /// Gets or sets current user name.
        /// </summary>
        /// <value>The name of the user.</value>
        [RequiredIfNot("Email")]
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        /// <value>The password.</value>
        [RequiredIfNot("Email")]
        [StringLength(int.MaxValue, MinimumLength = 3)]
        [RegularExpression(@"((?=.*[a-z])(?=.*[A-Z])(?=.*[\W\d]).*)")]
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the confirm password.
        /// </summary>
        /// <value>The password.</value>
        [RequiredIfNot("Email")]
        [System.Web.Mvc.Compare("Password")]
        public string ConfirmPassword { get; set; }

        /// <summary>
        /// Gets or sets current user first name.
        /// </summary>
        /// <value>The user full name.</value>
        [RequiredIfNot("Email")]
        public string FirstName { get; set; }

        /// <summary>
        /// Gets or sets the last name.
        /// </summary>
        /// <value>The last name.</value>
        [RequiredIfNot("Email")]
        public string LastName { get; set; }

        [RegularExpression(@"^([0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*@([0-9a-zA-Z][-\w]*[0-9a-zA-Z]\.)+[a-zA-Z]{2,9})$")]
        [RequiredIfNot("UserName")]
        public string Email { get; set; }

        public int MinPasswordLength { get; set; }
    }
}
