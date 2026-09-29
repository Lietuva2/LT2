using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Problem;
using Framework.Mvc.DataAnnotations;
using Framework.Strings;
using Hyper.ComponentModel;

namespace Data.ViewModels.Idea
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class IdeaCreateEditModel
    {
        public string Id { get; set; }
        [Required]
        public string Subject { get; set; }
        [RichTextRequired]
        [AllowHtml]
        public string Summary { get; set; }

        public string VersionSubject { get; set; }

        public string DocumentUrl { get; set; }
        public List<UrlEditModel> Urls { get; set; }
        public List<int> CategoryIds { get; set; }
        public IEnumerable<SelectListItem> Categories { get; set; }
        public IEnumerable<SelectListItem> Municipalities { get; set; }
        public int? MunicipalityId { get; set; }
        [AllowHtml]
        public string Aim { get; set; }
        public bool CanCurrentUserEdit { get; set; }
        public List<SelectListItem> Organizations { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public ObjectVisibility Visibility { get; set; }
        public bool IsPrivateToOrganization { get { return Visibility != ObjectVisibility.Public; } }
        public bool SendMail { get; set; }
        public int EditIndex { get; set; }
        public List<RelatedIdeaListItem> RelatedIdeas { get; set; }
        public List<ListItem> RelatedIssues { get; set; }
        public string RelatedIdeaText { get; set; }
        public string RelatedIdeaId { get; set; }
        public IEnumerable<SelectListItem> Ideas { get; set; }
        public List<ProblemIndexItemModel> Problems { get; set; }
        public string Save { get; set; }
        public string Draft { get; set; }
        public bool IsDraft { get; set; }
        public bool IsMailSendable { get; set; }
        public bool AdditionalInfoRequiredForVoting { get; set; }
        public List<UrlViewModel> Attachments { get; set; }
        [Remote("ValidateShortLink", "Common")]
        public string ShortLink { get; set; }
        public bool ConfirmedUsersVoting { get; set; }
        public bool AllowPublicAlternativeIdeas { get; set; }
        public bool IsEdit
        {
            get { return !Id.IsNullOrEmpty(); }
        }

        public IdeaCreateEditModel()
        {
            CategoryIds = new List<int>();
            Organizations = new List<SelectListItem>();
            RelatedIdeas = new List<RelatedIdeaListItem>();
            Urls = new List<UrlEditModel>();
            Problems = new List<ProblemIndexItemModel>();
            SendMail = false;
            Attachments = new List<UrlViewModel>();
            RelatedIssues = new List<ListItem>();
            OrganizationId = string.Empty;
            AllowPublicAlternativeIdeas = true;
        }

        public class Category
        {
            public string Name { get; set; }
            public int DbId { get; set; }
            public bool IsDeleted { get; set; }
        }
    }
}