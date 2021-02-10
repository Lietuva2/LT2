using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Data.Enums;

namespace Data.ViewModels.Voting
{
    public class IssueDocumentModel
    {
        public string Subject { get; set; }
        public string Summary { get; set; }
        public int SupportingVotesCount { get; set; }
        public int NonSupportingVotesCount { get; set; }
        public int NeutralVotesCount { get; set; }
        public int SupportingUsersCount { get; set; }
        public int NonSupportingUsersCount { get; set; }
        public int NeutralUsersCount { get; set; }
        public List<IssueDocumentUserModel> Users { get; set; }
        public string CreatedByUserId { get; set; }
        public string OfficialVoteDesciprtion { get; set; }
        public string OrganizationId { get; set; }

        public IssueDocumentModel()
        {
            Users = new List<IssueDocumentUserModel>();
        }
    }

    public class IssueDocumentUserModel
    {
        public string UserId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public ForAgainst Vote { get; set; }
        public DateTime Date { get; set; }
        public string Source { get; set; }
        public bool IsValid { get; set; }
    }
}
