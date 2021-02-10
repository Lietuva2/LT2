using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace Data.ViewModels.Account
{
    public class UserCreateModel
    {
        /// <summary>
        /// Gets or sets current user name.
        /// </summary>
        /// <value>The name of the user.</value>
        [Required]
        [Remote("ValidateUserName", "Account")]
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        /// <value>The password.</value>
        [Required]
        [StringLength(int.MaxValue, MinimumLength = 6)]
        [RegularExpression(@"((?=.*[a-z])(?=.*[A-Z])(?=.*[\W\d]).*)")]
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the confirm password.
        /// </summary>
        /// <value>The password.</value>
        [Required]
        [System.Web.Mvc.Compare("Password")]
        public string ConfirmPassword { get; set; }

        /// <summary>
        /// Gets or sets current user first name.
        /// </summary>
        /// <value>The user full name.</value>
        [Required]
        public string FirstName { get; set; }

        /// <summary>
        /// Gets or sets the last name.
        /// </summary>
        /// <value>The last name.</value>
        [Required]
        public string LastName { get; set; }

        [Required]
        [RegularExpression(@"^([0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*@([0-9a-zA-Z][-\w]*[0-9a-zA-Z]\.)+[a-zA-Z]{2,9})$")]
        [Remote("ValidateEmail", "Account")]
        public string Email { get; set; }

        public int MinPasswordLength { get; set; }

        public bool SendMail { get; set; }

        public string ReturnTo { get; set; }
    }
}
