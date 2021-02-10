using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class OrganizationInviteCommand : Command
    {
        public ActionTypes ActionType { get; set; }
        public string UserFullName { get; set; }
        public string UserLink { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationLink { get; set; }
        public string Email { get; set; }
        public string UserObjectId { get; set; }
        public int? UserId { get; set; }

        public OrganizationInviteCommand()
        {
            ActionType = ActionTypes.OrganizationUserInvited;
        }
    }
}
