using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Base
{
    public class InviteUserModel
    {
        public string InvitedUser { get; set; }
        public int? UserId { get; set; }
        public bool InvitationSent { get; set; }
        public string Message { get; set; }

        public InviteUserModel()
        {
            InvitationSent = false;
        }
    }
}
