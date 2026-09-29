using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Data.Enums;
using Data.ViewModels.Account;
using Framework.Enums;
using Framework.Strings;
using Services.Infrastructure;

namespace Services.Session
{
    /// <summary>
    /// User information.
    /// </summary>
    public class UserInfo
    {
        /// <summary>
        /// Gets or sets the user ID.
        /// </summary>
        /// <value>The user ID.</value>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the db id.
        /// </summary>
        /// <value>The db id.</value>
        public int? DbId { get; set; }

        /// <summary>
        /// Gets or sets current user name.
        /// </summary>
        /// <value>The name of the user.</value>
        public string UserName { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        /// <summary>
        /// Gets or sets current user full name.
        /// </summary>
        /// <value>The user full name.</value>
        public string FullName { get { return FirstName + " " + LastName; } }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        /// <value>The password.</value>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is authenticated.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is authenticated; otherwise, <c>false</c>.
        /// </value>
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// Gets or sets the ip.
        /// </summary>
        /// <value>The ip.</value>
        public string Ip { get; set; }

        public long? FacebookId { get; set; }

        public long? ConnectedFacebookId { get; set; }

        public bool? IsConnectedToFacebook { get; set; }

        public string Email { get; set; }

        public UserRoles Role { get; set; }

        public List<int> ExpertCategoryIds { get; set; }

        public List<int> SelectedCategoryIds { get; set; }

        public List<int> Municipalities { get; set; }

        public string LanguageCode { get; set; }

        public string LanguageName { get; set; }

        public List<UserOrganization> Organizations { get; set; }

        public List<string> Projects { get; set; }

        public bool RequireChangePassword { get; set; }

        public bool HasSigned { get; set; }

        public bool IsAmbasador { get; set; }

        public bool PostPermissionGranted { get; set; }

        public bool IsUnique { get { return !string.IsNullOrEmpty(PersonCode); } }

        public List<int> LikedByUsers { get; set; }

        public bool VotesArePublic { get; set; }

        public string SupportedIdeaText { get; set; }

        public string VotedText { get; set; }

        public bool RequireUniqueAuthentication { get; set; }

        public bool IsMainIdeaVoted { get; set; }

        public List<string> OrganizationIds
        {
            get
            {
                if (Organizations == null)
                {
                    return new List<string>();
                }

                return Organizations.Where(o => o.IsMember).Select(o => o.OrganizationId).ToList();
            }
        }

        private string personCode;
        public string PersonCode
        {
            get
            {
                if (!string.IsNullOrEmpty(personCode))
                {
                    return personCode;
                }

                if (!CustomAppSettings.RequireUniqueAuthentication && IsAuthenticated)
                {
                    return DbId.ToString();
                }

                return null;
            }
            set { personCode = value; }
        }

        public bool CanSign { get { return PersonCode.ConvertPersonCodeToBirthDate().Date.AddYears(18) <= DateTime.Now.Date; } }

        public bool IsConfirmedThisSession
        {
            get { return (bool?) HttpContext.Current.Session["IsConfirmedThisSession"] ?? false; }
            set { HttpContext.Current.Session["IsConfirmedThisSession"] = value; }
        }

        public bool IsViispConfirmed { get; set; }

        public AdditionalUniqueInfoModel AdditionalInfo { get; set; }

        public List<PointsPerCategoryModel> Points { get; set; }
        public int Reputation { get; set; }

        private string authSource;
        public string AuthenticationSource
        {
            get
            {
                if (string.IsNullOrEmpty(authSource))
                {
                    return "None";
                }

                return authSource;
            }
            set { authSource = value; }
        }

        public bool VerificationPending { get; set; }

        public bool IsUserInOrganization(string organizationId)
        {
            if(Organizations == null)
            {
                return false;
            }

            return Organizations.Where(o => o.IsMember).Select(o => o.OrganizationId).Contains(organizationId);
        }

        public DateTime? PostedToFacebookDate { get; set; }

        public bool TutorialShown { get; set; }

        public bool CanPostToFacebook
        {
            get
            {
                //return IsAmbasador &&
                //       (!PostedToFacebookDate.HasValue ||
                //        (DateTime.Now - PostedToFacebookDate.Value) > TimeSpan.FromDays(1));
                return false;
            }
        }

        public bool? FacebookPageLiked { get; set; }

        public UserInfo()
        {
            ExpertCategoryIds = new List<int>();
            SelectedCategoryIds = new List<int>();
            AdditionalInfo = new AdditionalUniqueInfoModel();
            VotesArePublic = false;
            Organizations = new List<UserOrganization>();
        }
    }
}
