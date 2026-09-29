using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Enums;

namespace Data.ViewModels.Organization
{
    public class MemberModel
    {
        public int? DbId { get; set; }
        public string ObjectId { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Email { get; set; }
        public bool IsEditable { get; set; }
        public string OrganizationId { get; set; }
        public UserRoles Permission { get; set; }
        public bool IsPublic { get; set; }
        public bool IsCurrentUser { get; set; }
        public string InvitedBy { get; set; }
        public int VoteCount { get; set; }

        public MemberModel()
        {
            VoteCount = 1;
        }

        public override bool Equals(object obj)
        {
            var o = (MemberModel)obj;
            return this.ObjectId == o.ObjectId && this.Email == o.Email && this.FullName == o.FullName;
        }

        public override int GetHashCode()
        {
            return ObjectId.GetHashCode();
        }
    }
}
