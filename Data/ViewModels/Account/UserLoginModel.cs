using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Data.ViewModels.Account
{
    public class UserLoginModel
    {
        /// <summary>
        /// Gets or sets the name of the user.
        /// </summary>
        /// <value>The name of the user.</value>
        [Required]
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        /// <value>The password.</value>
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public bool RememberMe { get; set; }

        public string ReturnTo { get; set; }

        //public OAuthLoginModel OAuthLogin { get; set; }

        public string Json { get; set; }

        public IEnumerable<ExternalAuthenticationDescription> ExternalAuthenticationTypes { get; set; }

        public UserLoginModel()
        {
            ExternalAuthenticationTypes = new List<ExternalAuthenticationDescription>();
            //OAuthLogin = new OAuthLoginModel();
        }
    }
}
