using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Data.Enums;
using Framework.Mvc.DataAnnotations;

namespace Data.ViewModels.Account
{
    public class UserDeleteRequestViewModel
    {
        public UserDeleteReason Reason { get; set; }

        public bool IsCommentRequired { get { return Reason == UserDeleteReason.Other; } }

        [RequiredIf("IsCommentRequired")]
        public string Comment { get; set; }
    }
}
