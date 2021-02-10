using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace Data.ViewModels.Account
{
    /// <summary>
    /// View model for remind password action.
    /// </summary>
    public class ChangePasswordModel
    {
        public string OldPassword { get; set; }
        [Required]
        [StringLength(int.MaxValue, MinimumLength = 6)]
        [RegularExpression(@"((?=.*[a-z])(?=.*[A-Z])(?=.*[\W\d]).*)")]
        public string NewPassword { get; set; }
        [System.Web.Mvc.Compare("NewPassword")]
        public string ConfirmPassword { get; set; }
    }
}
