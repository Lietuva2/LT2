using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Framework.Mvc.DataAnnotations;
using Hyper.ComponentModel;

namespace Data.ViewModels.Organization.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class SettingsModel : ProjectBase
    {
        [Required]
        public new string Subject { get; set; }
        public string State { get; set; }
        public IList<SelectListItem> ProjectStates { get; set; }
        [RequiredIf("IsDescriptionRequired")]
        [AllowHtml]
        public string Description { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsDescriptionRequired { get { return State == IdeaStates.Closed.ToString() || State == IdeaStates.Realized.ToString(); }}

        public SettingsModel()
        {
        }
    }
}