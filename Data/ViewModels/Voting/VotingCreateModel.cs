﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Framework.Mvc.DataAnnotations;
using Framework.Strings;
using Hyper.ComponentModel;
using Data.ViewModels.Problem;

namespace Data.ViewModels.Voting
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class VotingCreateEditModel
    {
        public string Id { get; set; }
        [Required]
        public string Subject { get; set; }
        [RichTextRequired, AllowHtml]
        public string Summary { get; set; }
        public DateTime? Deadline { get; set; }
        public TimeSpan? DeadlineTime { get; set; }
        public string DocumentUrl { get; set; }
        public List<UrlEditModel> Urls { get; set; }
        public List<int> CategoryIds { get; set; }
        public IEnumerable<SelectListItem> Categories { get; set; }
        public bool CanCurrentUserEdit { get; set; }
        public int EditIndex { get; set; }
        public List<RelatedIdeaListItem> RelatedIdeas { get; set; }
        public string RelatedIdeaId { get; set; }
        public IEnumerable<SelectListItem> Ideas { get; set; }
        public IEnumerable<SelectListItem> Municipalities { get; set; }
        public int? MunicipalityId { get; set; }
        public List<SelectListItem> Organizations { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public ObjectVisibility Visibility { get; set; }
        public bool IsPrivateToOrganization { get { return Visibility != ObjectVisibility.Public; } }
        public bool SendMail { get; set; }
        public List<ProblemIndexItemModel> Problems { get; set; }
        public bool IsMailSendable { get; set; }
        public bool AdditionalInfoRequiredForVoting { get; set; }
        [Remote("ValidateShortLink", "Common")]
        public string ShortLink { get; set; }
        public bool AllowSummaryWiki { get; set; }
        public bool AllowNeutralVotes { get; set; }

        public bool IsEdit
        {
            get { return !Id.IsNullOrEmpty(); }
        }

        public VotingCreateEditModel()
        {
            CategoryIds = new List<int>();
            RelatedIdeas = new List<RelatedIdeaListItem>();
            Urls = new List<UrlEditModel>() { };
            SendMail = false;
            Problems = new List<ProblemIndexItemModel>();
            AllowSummaryWiki = true;
            OrganizationId = string.Empty;
        }
    }
}