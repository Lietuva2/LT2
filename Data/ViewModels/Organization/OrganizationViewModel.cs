using System.Collections.Generic;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Idea;
using Data.ViewModels.NewsFeed;
using Data.ViewModels.Organization.Project;
using Data.ViewModels.Problem;
using Data.ViewModels.Voting;
using Framework.Lists;

namespace Data.ViewModels.Organization
{
    public class OrganizationViewModel
    {
        public string Name { get; set; }
        public string ObjectId { get; set; }
        public InfoViewModel Info { get; set; }
        public ContactsViewModel Contacts { get; set; }
        public bool HasProfilePicture { get; set; }
        public ExpandableList<NewsFeedItemModel> ActivityList { get; set; }
        public OrganizationViews View { get; set; }
        public bool IsEditable { get; set; }
        public bool IsContributable { get; set; }
        public bool IsDeletable { get; set; }
        public bool IsLikeable { get; set; }
        public bool IsUnlikeable { get; set; }
        public bool IsJoinable { get; set; }
        public bool IsLeavable { get; set; }
        public bool WaitingForApprove { get; set; }
        public List<MemberModel> Members { get; set; }
        public int MembersCount { get; set; }
        public List<UserLinkModel> UnconfirmedMembers { get; set; }
        public ProjectsListModel Projects { get; set; }
        public List<InviteUserModel> InvitedUsers { get; set; }
        public List<InviteUserModel> UsersToInvite { get; set; }
        [AllowHtml]
        public string CustomInvitationText { get; set; }
        public string InvitedUserEmails { get; set; }
        public IdeaIndexModel Ideas { get; set; }
        public VotingIndexModel Issues { get; set; }
        public VotingResultsModel Results { get; set; }
        public ProblemIndexModel Problems { get; set; }
        public string ShortLink { get; set; }
        public int SupportersCount { get; set; }

        public OrganizationViewModel()
        {
            Members = new List<MemberModel>();
            UnconfirmedMembers = new List<UserLinkModel>();
            InvitedUsers = new List<InviteUserModel>();
            UsersToInvite = new List<InviteUserModel>();
            Ideas = new IdeaIndexModel();
            Issues = new VotingIndexModel();
            Results = new VotingResultsModel();
            Problems = new ProblemIndexModel();
        }
    }
}
