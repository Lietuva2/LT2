using System.ComponentModel.DataAnnotations;

namespace Data.ViewModels.Account
{
    public class OAuthLoginModel
    {
        //string facebookId, string firstName, string lastName, string userName, bool isPageLiked, string returnTo, bool doRegister
        public string FacebookId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public bool IsPageLiked { get; set; }
        public bool DoRegister { get; set; }
        public string FullName { get { return FirstName + " " + LastName; } }
        public string ReturnTo { get; set; }
        public bool RememberMe { get; set; }
    }
}
