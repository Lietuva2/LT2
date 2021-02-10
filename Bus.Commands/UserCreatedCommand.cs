using System;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Bus.Commands
{
    public class UserCreatedCommand : ActionCommand
    {
        public UserCreatedCommand()
        {
            ActionType = ActionTypes.UserCreated;
        }
    }
}
