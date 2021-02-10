using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class ProjectInviteCommand : Command
    {
        public ActionTypes ActionType { get; set; }
        public string UserFullName { get; set; }
        public string UserLink { get; set; }
        public string ProjectId { get; set; }
        public string ProjectLink { get; set; }
        public string Email { get; set; }

        public ProjectInviteCommand()
        {
            ActionType = ActionTypes.ProjectUserInvited;
        }
    }
}
