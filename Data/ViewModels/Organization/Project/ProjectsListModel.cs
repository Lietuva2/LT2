using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Organization.Project
{
    public class ProjectsListModel
    {
        public string OrganizationId { get; set; }
        public List<ProjectViewModel> List { get; set; }
        public bool IsEditable { get; set; }
    }
}
