using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class InterestsViewModel
    {
        public string UserObjectId { get; set; }
        public string Interests { get; set; }
        public string Groups { get; set; }
        public string Awards { get; set; }
        public string PoliticalViews { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool IsFilled { get
        {
            return !string.IsNullOrEmpty(Awards) || !string.IsNullOrEmpty(Interests) ||
                   !string.IsNullOrEmpty(PoliticalViews)
                   || !string.IsNullOrEmpty(Groups);
        }}
    }
}
