using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Data.ViewModels.Organization.Project
{
    public class ProjectViewModel
    {
        public string Name { get; set; }
        public string ObjectId { get; set; }
        public string OrganizationId { get; set; }
        public int TasksCount { get; set; }
        public OrganizationProjectStates State { get; set; }
    }
}
