using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Problem
{
    public class ProblemIdeaListModel
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public string ProblemId { get; set; }
        public bool CanDelete { get; set; }
    }
}
