using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Organization.Project
{
    public class ProjectBase
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public string Subject { get; set; }
        public bool IsEditable { get; set; }
    }
}
