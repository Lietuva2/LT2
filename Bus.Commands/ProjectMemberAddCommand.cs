using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Bus.Commands
{
    public class ProjectMemberAddCommand : ActionCommand
    {
        public int AddedUserId { get; set; }

        public ProjectMemberAddCommand()
        {
            ActionType = ActionTypes.ProjectMemberAdded;
        }
    }
}
