using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.ViewModels.Base;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ProjectTeamModel : ProjectModel
    {
        public List<MemberModel> Members { get; set; }
        public List<UserLinkModel> UnconfirmedMembers { get; set; }
        public List<InviteUserModel> InvitedUsers { get; set; }
        public List<InviteUserModel> UsersToInvite { get; set; }
        public string OrganizationId { get; set; }

        public ProjectTeamModel()
        {
            Members = new List<MemberModel>();
            UnconfirmedMembers = new List<UserLinkModel>();
            UsersToInvite = new List<InviteUserModel>();
            InvitedUsers = new List<InviteUserModel>();
        }
    }
}