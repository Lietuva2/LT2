using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.ViewModels.Base;
using Framework.Mvc.DataAnnotations;

namespace Data.ViewModels.Account
{
    public class MemberOfPartiesEditModel : EditableListModel
    {
        public string PartyName { get; set; }
        public string PartyUrl { get; set; }
        [RequiredIf("PartyName")]
        public int? StartYear { get; set; }
        public int? StartMonth { get; set; }
        [RequiredIfNot("IsCurrent")]
        public int? EndYear { get; set; }
        public int? EndMonth { get; set; }
        public string Description { get; set; }
        public bool IsCurrent { get; set; }
        public string IsCurrentClass
        {
            get
            {
                return IsCurrent ? "current" : "historic";
            }
        }

        public MemberOfPartiesEditModel()
        {
            IsCurrent = true;
        }
    }
}
