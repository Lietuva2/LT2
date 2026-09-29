using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.MongoDB;
using Data.ViewModels.Comments;
using Data.ViewModels.NewsFeed;
using Framework.Lists;

namespace Data.ViewModels.Account
{
    public class UserAccountViewModel
    {
        public string FullName { get; set; }
        public string UserObjectId { get; set; }
        public PersonalInfoViewModel PersonalInfo { get; set; }
        public EducationAndWorkViewModel EducationAndWork { get; set; }
        public InterestsViewModel Interests { get; set; }
        public ContactsViewModel Contacts { get; set; }
        public bool IsLiked { get; set; }
        public bool IsCurrentUser { get; set; }
        public string MemberSince { get; set; }
        public string VotesCount { get; set; }
        public string CommentsCount { get; set; }
        public string IdeasCount { get; set; }
        public string IssuesCount { get; set; }
        public string UsersThatLikeMe { get; set; }
        public string InvolvedIdeasCount { get; set; }
        public string MyProjectCount { get; set; }
        public string ProblemsCount { get; set; }
        public string Categories { get; set; }
        public List<SimpleLinkView> LikedUsers { get; set; }
        public List<SimpleLinkView> LikedOrganizations { get; set; }
        public List<SimpleLinkView> MemberOfOrganizations { get; set; }
        public bool HasProfilePicture { get; set; }
        public OAuthLoginModel FacebookLogin { get; set; }
        public ExpandableList<NewsFeedItemModel> UserActivityList { get; set; }
        public UserViews View { get; set; }
        public bool CanSendMessage { get; set; }
        public int Status { get; set; }
        public int Points { get; set; }
        public int Reputation { get; set; }
        public List<string> ExpertOf { get; set; }
        public List<short> Awards { get; set; }
        public SettingsModel Settings { get; set; }
        public bool IsProfileVisible { get; set; }
        public bool IsActivityVisible { get; set; }
        public bool IsReputationVisible { get; set; }
        public CommentsModel Comments { get; set; }
        public string ShortLink { get; set; }
        public bool IsOnline { get; set; }
        public bool RequireUniqueAuthentication { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsAmbasador { get; set; }
        public bool IsUnique { get; set; }
        public bool IsPolitician { get; set; }

        public UserAccountViewModel()
        {
            FacebookLogin = new OAuthLoginModel();
            ExpertOf = new List<string>();
            Awards = new List<short>();
            Settings = new SettingsModel();
        }
    }
}
