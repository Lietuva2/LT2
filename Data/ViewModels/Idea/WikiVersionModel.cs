using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using Data.MongoDB;
using Data.ViewModels.Base;
using Framework.Mvc.DataAnnotations;

namespace Data.ViewModels.Idea
{
    public class WikiVersionModel
    {
        public string Id { get; set; }
        public string CreatorObjectId { get; set; }
        public string CreatorFullName { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        [Required]
        public string Subject { get; set; }
        [RichTextRequired]
        [AllowHtml]
        public string Text { get; set; }
        public bool CreateNewVersion { get; set; }
        public int Number { get; set; }
        public int SupportingUserCount { get; set; }
        public int SupportingConfirmedUserCount { get; set; }
        public int SupportingUnconfirmedUserCount { get; set; }
        public List<SupportingUser> SupportingUsers { get; set; }
        public string SupportPercentage { get; set; }
        public bool IsLikedByCurrentUser { get; set; }
        public bool IsViewedByCurrentUser { get; set; }
        public bool IsCreatedByCurrentUser { get; set; }
        public List<SelectListItem> Organizations { get; set; }
        public DateTime CreatedOn { get; set; }
        public List<UrlViewModel> Attachments { get; set; }
        public bool IsEditable { get; set; }
        public List<WikiVersionModel> History { get; set; }

        public WikiVersionModel()
        {
            CreateNewVersion = true;
            History = new List<WikiVersionModel>();
            Attachments = new List<UrlViewModel>();
            Organizations = new List<SelectListItem>();
        }
    }
}
